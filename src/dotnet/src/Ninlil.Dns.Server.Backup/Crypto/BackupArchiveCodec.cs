using System.Security.Cryptography;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Crypto;

/// <summary>
///     This class encrypts and decrypts a backup archive's own plaintext payload with a
///     passphrase this server never stores anywhere, so the resulting file is safe to keep
///     off-site. It uses AES-256-GCM (authenticated encryption -- decryption fails loudly if
///     even one byte of the ciphertext was altered after encryption, which is what lets a
///     tampered archive be rejected with real cryptographic proof rather than a heuristic) with a
///     key derived from the passphrase via PBKDF2-HMAC-SHA256, a fresh random salt and iteration
///     count recorded per archive (so two archives encrypted with the identical passphrase never
///     share a key), and a fresh random nonce per archive (so the same key, if it were ever
///     reused, would never see nonce reuse -- the one mistake that fully breaks AES-GCM's own
///     security guarantee).
/// </summary>
public static class BackupArchiveCodec
{
    /// <summary>
    ///     This constant contains the fixed 5-byte sequence that opens every encrypted archive
    ///     this class produces, identifying both the file format itself and its own version --
    ///     distinct from <see cref="Model.BackupManifest.FormatVersion" />, which versions the
    ///     *decrypted* payload's own entity/file layout, not this envelope's own cryptographic
    ///     framing.
    /// </summary>
    private static readonly byte[] MagicHeaderEncrypted = "LTBK1"u8.ToArray();

    /// <summary>
    ///     This constant contains the fixed 5-byte sequence that opens every unencrypted archive
    ///     <see cref="Wrap" /> produces -- an operator-chosen alternative to
    ///     <see cref="MagicHeaderEncrypted" /> for a backup created with no passphrase at all.
    ///     Sharing the same length as the encrypted header, differing only in its final digit,
    ///     keeps <see cref="PeekKind" /> a single fixed-offset comparison rather than needing two
    ///     different header lengths.
    /// </summary>
    private static readonly byte[] MagicHeaderUnencrypted = "LTBK0"u8.ToArray();

    /// <summary>
    ///     This constant contains how many PBKDF2 rounds derive the AES key from the operator's
    ///     passphrase -- 600,000 rounds of HMAC-SHA256, matching OWASP's own current minimum
    ///     recommendation for this exact algorithm/hash pairing as of this writing, chosen to make
    ///     brute-forcing a weak passphrase materially slower without making a legitimate backup or
    ///     restore noticeably slow on ordinary hardware.
    /// </summary>
    private const int Pbkdf2Iterations = 600_000;

    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    /// <summary>
    ///     This method encrypts <paramref name="plaintext" /> with a key derived from
    ///     <paramref name="passphrase" />, returning a single self-contained byte array: the
    ///     magic header, the salt and iteration count PBKDF2 needs to re-derive the same key, the
    ///     nonce and authentication tag AES-GCM needs to decrypt and verify, and finally the
    ///     ciphertext itself.
    /// </summary>
    /// <param name="plaintext">The archive's own decrypted payload bytes.</param>
    /// <param name="passphrase">The operator-supplied passphrase to encrypt it with.</param>
    /// <returns>The complete, encrypted archive file's own bytes.</returns>
    public static byte[] Encrypt(byte[] plaintext, string passphrase)
    {
        // Why: a fresh salt and nonce are generated on every single call, from a cryptographic
        // random source, never derived from the passphrase or from each other -- this is what
        // this class's own doc comment means by "two archives encrypted with the identical
        // passphrase never share a key" (the salt) and "the same key ... would never see nonce
        // reuse" (the nonce), the one mistake that would otherwise fully break AES-GCM's own
        // confidentiality and authentication guarantees.
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] key = DeriveKey(passphrase, salt, Pbkdf2Iterations);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (AesGcm aesGcm = new(key, TagSizeBytes))
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // How: the finished archive's own byte layout is written in exactly the order Decrypt
        // expects to read it back in -- magic header, then everything Decrypt needs to
        // re-derive the same key and re-verify the same tag (salt, iteration count, nonce, tag),
        // and only then the ciphertext itself.
        using MemoryStream output = new();
        output.Write(MagicHeaderEncrypted);
        output.Write(salt);
        output.Write(BitConverter.GetBytes(Pbkdf2Iterations));
        output.Write(nonce);
        output.Write(tag);
        output.Write(ciphertext);

        return output.ToArray();
    }

    /// <summary>
    ///     This method reverses <see cref="Encrypt" />, re-deriving the same key from
    ///     <paramref name="passphrase" /> and the salt/iteration count recorded in
    ///     <paramref name="archive" />'s own header, then decrypting and authenticating the
    ///     ciphertext.
    /// </summary>
    /// <param name="archive">The complete, encrypted archive file's own bytes.</param>
    /// <param name="passphrase">The passphrase to attempt decryption with.</param>
    /// <returns>The archive's own decrypted payload bytes.</returns>
    /// <exception cref="BackupDecryptionFailedException">
    ///     Thrown when <paramref name="archive" /> is too short to be a real archive, does not
    ///     start with the expected magic header, or fails AES-GCM's own authentication check --
    ///     which happens both for a wrong passphrase and for a tampered ciphertext, since an
    ///     authenticated cipher cannot (and, for tamper-detection to mean anything, must not) tell
    ///     the two apart.
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

        int iterations = BitConverter.ToInt32(archive, offset);
        offset += sizeof(int);

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
            // AesGcm.Decrypt throws exactly this way both for a wrong key (a wrong passphrase)
            // and for ciphertext/tag tampering -- there is no way, by design, to tell those two
            // apart from the outside without weakening the authentication guarantee itself.
            throw new BackupDecryptionFailedException(
                "Failed to decrypt this backup archive. The passphrase may be incorrect, or the "
                    + "archive may be corrupted or have been tampered with."
            );
        }

        return plaintext;
    }

    /// <summary>
    ///     This method wraps <paramref name="plaintext" /> in the unencrypted archive envelope --
    ///     no key derivation, no cipher, nothing beyond <see cref="MagicHeaderUnencrypted" />
    ///     identifying this file as an archive an operator deliberately chose to create without a
    ///     passphrase. Used only when <c>SystemBackupService.CreateBackupAsync</c> receives a
    ///     null/blank passphrase -- a real, honest design choice (a clearly-marked unencrypted
    ///     archive) rather than deriving a weak key from an empty or default value, which would
    ///     dishonestly look encrypted while offering no real protection at all.
    /// </summary>
    /// <param name="plaintext">The archive's own decrypted payload bytes.</param>
    /// <returns>The complete, unencrypted archive file's own bytes.</returns>
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
    /// <param name="archive">The complete, unencrypted archive file's own bytes.</param>
    /// <returns>The archive's own payload bytes.</returns>
    /// <exception cref="BackupArchiveInvalidException">
    ///     Thrown when <paramref name="archive" /> is too short to carry the header, or does not
    ///     start with <see cref="MagicHeaderUnencrypted" /> -- most commonly because the archive
    ///     is actually encrypted (see <see cref="PeekKind" />, which every real caller checks
    ///     first, so reaching this exception at all means a caller bypassed that check).
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
    ///     This method inspects only <paramref name="archive" />'s own magic header to determine
    ///     whether it is encrypted or not, without attempting to decrypt or unwrap it -- the one
    ///     place this class lets a caller ask "what kind of archive is this" ahead of committing
    ///     to either <see cref="Decrypt" /> or <see cref="Unwrap" />. This is what lets
    ///     <c>SystemBackupService.RestoreBackupAsync</c> auto-detect an archive's own real kind
    ///     directly from the uploaded bytes themselves, rather than trusting a caller-supplied
    ///     flag (or the mere presence of a passphrase in the request) that could disagree with
    ///     what the file actually is.
    /// </summary>
    /// <param name="archive">The complete archive file's own bytes, of either kind.</param>
    /// <returns>Which envelope <paramref name="archive" /> actually carries.</returns>
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
