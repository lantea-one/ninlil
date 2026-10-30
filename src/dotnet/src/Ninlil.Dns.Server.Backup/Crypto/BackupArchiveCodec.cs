using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Crypto;

/// <summary>
///     This class writes and reads the outer envelope of a backup archive. With a passphrase
///     the server never stores, it encrypts the payload so the file is safe to keep off-site:
///     AES-256-GCM, whose authentication makes decryption fail if any byte of the ciphertext
///     changed, with a key derived by PBKDF2-HMAC-SHA256 from the passphrase and a random salt
///     stored in the archive (so two archives with the same passphrase never share a key), and a
///     random nonce per archive (so a key is never used twice with one nonce, which would break
///     AES-GCM). Without a passphrase it only adds a header that marks the archive as
///     unencrypted. <see cref="SystemBackupService" /> calls it.
/// </summary>
public static class BackupArchiveCodec
{
    /// <summary>
    ///     This field contains the 5-byte header of every encrypted archive, naming the envelope
    ///     format and its version. It is separate from
    ///     <see cref="Model.BackupManifest.FormatVersion" />, which versions the payload layout
    ///     inside.
    /// </summary>
    private static readonly byte[] MagicHeaderEncrypted = "LTBK1"u8.ToArray();

    /// <summary>
    ///     This field contains the 5-byte header of every unencrypted archive <see cref="Wrap" />
    ///     writes. It has the same length as <see cref="MagicHeaderEncrypted" /> and differs only
    ///     in the last digit, so <see cref="PeekKind" /> compares one fixed-length prefix.
    /// </summary>
    private static readonly byte[] MagicHeaderUnencrypted = "LTBK0"u8.ToArray();

    /// <summary>
    ///     This constant contains how many PBKDF2-HMAC-SHA256 iterations derive the AES key for a
    ///     new archive: 600,000, the figure the OWASP Password Storage Cheat Sheet (section "PBKDF2") recommends for that pairing. It slows brute force
    ///     without making a backup or restore noticeably slow. The count is stored in each archive,
    ///     and <see cref="Decrypt" /> uses the stored value.
    /// </summary>
    private const int Pbkdf2Iterations = 600_000;

    /// <summary>
    ///     This constant contains the lowest iteration count <see cref="Decrypt" /> accepts from an
    ///     archive header. The header is not authenticated before the key is derived, so a count
    ///     that is too low would let a forged archive weaken the derivation, and zero or a negative
    ///     count would make the derivation throw.
    /// </summary>
    public const int MinimumPbkdf2Iterations = 100_000;

    /// <summary>
    ///     This constant contains the highest iteration count <see cref="Decrypt" /> accepts from
    ///     an archive header: 10 million, about ten seconds of work, which leaves room to raise
    ///     <see cref="Pbkdf2Iterations" /> later but stops a forged header from keeping a core busy
    ///     for hours.
    /// </summary>
    public const int MaximumPbkdf2Iterations = 10_000_000;

    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    /// <summary>
    ///     This method encrypts <paramref name="plaintext" /> with a key derived from
    ///     <paramref name="passphrase" /> and returns one self-contained byte array: the header, the
    ///     salt and iteration count needed to derive the key again, the nonce and authentication
    ///     tag, and the ciphertext.
    /// </summary>
    /// <param name="plaintext">The payload bytes.</param>
    /// <param name="passphrase">The operator's passphrase to encrypt with.</param>
    /// <returns>The bytes of the encrypted archive file.</returns>
    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        // A new random salt and nonce on every call: the salt gives each archive its own key,
        // and the nonce is never reused with a key (see the class summary).
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] key = DeriveKey(passphrase, salt, Pbkdf2Iterations);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (AesGcm aesGcm = new(key, TagSizeBytes))
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // The layout Decrypt reads: header, salt, iteration count, nonce, tag, ciphertext.
        using MemoryStream output = new();
        output.Write(MagicHeaderEncrypted);
        output.Write(salt);
        Span<byte> iterationBytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(iterationBytes, Pbkdf2Iterations);
        output.Write(iterationBytes);
        output.Write(nonce);
        output.Write(tag);
        output.Write(ciphertext);

        return output.ToArray();
    }

    /// <summary>
    ///     This method reverses <see cref="Encrypt" />: it derives the key again from
    ///     <paramref name="passphrase" /> and the salt and iteration count stored in the archive,
    ///     then decrypts and authenticates the ciphertext. The stored iteration count is used as
    ///     is.
    /// </summary>
    /// <param name="archive">The bytes of the encrypted archive file.</param>
    /// <param name="passphrase">The passphrase to decrypt with.</param>
    /// <returns>The decrypted payload bytes.</returns>
    /// <exception cref="BackupDecryptionFailedException">
    ///     Thrown when <paramref name="archive" /> is too short, does not start with the encrypted
    ///     header, stores an iteration count outside <see cref="MinimumPbkdf2Iterations" /> to
    ///     <see cref="MaximumPbkdf2Iterations" />, or fails AES-GCM authentication, which happens
    ///     both for a wrong passphrase and for a changed ciphertext; the two cannot be told apart.
    /// </exception>
    public static byte[] Decrypt(byte[] archive, string passphrase)
    {
        int headerLength =
            MagicHeaderEncrypted.Length
            + SaltSizeBytes
            + sizeof(int)
            + NonceSizeBytes
            + TagSizeBytes;

        if (archive is null || archive.Length < headerLength)
            throw new BackupDecryptionFailedException(
                "This file is too short to be a valid Ninlil backup archive."
            );

        int offset = 0;
        ReadOnlySpan<byte> magic = archive.AsSpan(offset, MagicHeaderEncrypted.Length);
        offset += MagicHeaderEncrypted.Length;

        if (!magic.SequenceEqual(MagicHeaderEncrypted))
            throw new BackupDecryptionFailedException(
                "This file does not look like an encrypted Ninlil backup archive. If it was "
                    + "created without a passphrase, restore it with the passphrase field left blank."
            );

        byte[] salt = archive.AsSpan(offset, SaltSizeBytes).ToArray();
        offset += SaltSizeBytes;

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(
            archive.AsSpan(offset, sizeof(int))
        );
        offset += sizeof(int);

        // Checked before anything is derived: the count comes from an unauthenticated header.
        if (iterations is < MinimumPbkdf2Iterations or > MaximumPbkdf2Iterations)
            throw new BackupDecryptionFailedException(
                "This archive's key-derivation settings are outside the accepted range; it was "
                    + "not made by Ninlil or has been altered."
            );

        byte[] nonce = archive.AsSpan(offset, NonceSizeBytes).ToArray();
        offset += NonceSizeBytes;

        byte[] tag = archive.AsSpan(offset, TagSizeBytes).ToArray();
        offset += TagSizeBytes;

        byte[] ciphertext = archive.AsSpan(offset).ToArray();
        byte[] key = DeriveKey(passphrase, salt, iterations);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using AesGcm aesGcm = new(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            // AesGcm.Decrypt throws the same way for a wrong key (wrong passphrase) and for a
            // changed ciphertext or tag; the two cannot be told apart.
            throw new BackupDecryptionFailedException(
                "Failed to decrypt this backup archive. The passphrase may be incorrect, or the "
                    + "archive may be corrupted or have been tampered with."
            );
        }

        return plaintext;
    }

    /// <summary>
    ///     This method wraps <paramref name="plaintext" /> in the unencrypted envelope: only
    ///     <see cref="MagicHeaderUnencrypted" /> in front, no key and no cipher.
    ///     <c>SystemBackupService.CreateBackupAsync</c> uses it when no passphrase is given. A key
    ///     derived from an empty or default value would look encrypted without protecting
    ///     anything, so the archive is marked as unencrypted instead.
    /// </summary>
    /// <param name="plaintext">The payload bytes.</param>
    /// <returns>The bytes of the unencrypted archive file.</returns>
    public static byte[] Wrap(byte[] plaintext)
    {
        using MemoryStream output = new();
        output.Write(MagicHeaderUnencrypted);
        output.Write(plaintext);
        return output.ToArray();
    }

    /// <summary>
    ///     This method reverses <see cref="Wrap" />, validating <see cref="MagicHeaderUnencrypted" />
    ///     and returning the payload bytes underneath it unchanged.
    /// </summary>
    /// <param name="archive">The bytes of the unencrypted archive file.</param>
    /// <returns>The payload bytes.</returns>
    /// <exception cref="BackupArchiveInvalidException">
    ///     Thrown when <paramref name="archive" /> is too short to carry the header, or does not
    ///     start with <see cref="MagicHeaderUnencrypted" />, usually because it is encrypted.
    ///     <c>SystemBackupService</c> calls <see cref="PeekKind" /> first, so it does not reach
    ///     this case.
    /// </exception>
    public static byte[] Unwrap(byte[] archive)
    {
        if (archive is null || archive.Length < MagicHeaderUnencrypted.Length)
            throw new BackupArchiveInvalidException(
                "This file is too short to be a valid Ninlil backup archive."
            );

        if (!archive.AsSpan(0, MagicHeaderUnencrypted.Length).SequenceEqual(MagicHeaderUnencrypted))
            throw new BackupArchiveInvalidException(
                "This file does not look like an unencrypted Ninlil backup archive. If it was "
                    + "created with a passphrase, restore it with that same passphrase supplied."
            );

        return archive.AsSpan(MagicHeaderUnencrypted.Length).ToArray();
    }

    /// <summary>
    ///     This method reads only the header of <paramref name="archive" /> to tell whether it is
    ///     encrypted, before calling <see cref="Decrypt" /> or <see cref="Unwrap" />.
    ///     <c>SystemBackupService.RestoreBackupAsync</c> uses it so the file itself, not a request
    ///     flag or the presence of a passphrase, decides how it is read.
    /// </summary>
    /// <param name="archive">The bytes of an archive file of either kind.</param>
    /// <returns>The envelope kind of <paramref name="archive" />.</returns>
    /// <exception cref="BackupArchiveInvalidException">
    ///     Thrown when <paramref name="archive" /> is too short to carry a header, or its header
    ///     matches neither known magic sequence.
    /// </exception>
    public static BackupArchiveKind PeekKind(byte[] archive)
    {
        int headerLength = Math.Max(MagicHeaderEncrypted.Length, MagicHeaderUnencrypted.Length);
        if (archive is null || archive.Length < headerLength)
            throw new BackupArchiveInvalidException(
                "This file is too short to be a valid Ninlil backup archive."
            );

        if (archive.AsSpan(0, MagicHeaderUnencrypted.Length).SequenceEqual(MagicHeaderUnencrypted))
            return BackupArchiveKind.Unencrypted;

        if (archive.AsSpan(0, MagicHeaderEncrypted.Length).SequenceEqual(MagicHeaderEncrypted))
            return BackupArchiveKind.Encrypted;

        throw new BackupArchiveInvalidException(
            "This file does not look like a Ninlil backup archive."
        );
    }

    /// <summary>
    ///     This method derives a 256-bit AES key from a passphrase, salt, and iteration count via
    ///     PBKDF2-HMAC-SHA256.
    /// </summary>
    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase ?? string.Empty),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySizeBytes
        );
}
