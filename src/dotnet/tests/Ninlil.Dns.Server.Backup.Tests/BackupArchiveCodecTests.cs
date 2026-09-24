using System.Text;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Crypto;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class covers <see cref="BackupArchiveCodec" /> directly, against plain byte arrays,
///     with no database or filesystem involved at all -- the pure cryptographic contract every
///     higher-level backup/restore behavior ultimately depends on.
/// </summary>
public sealed class BackupArchiveCodecTests
{
    [Fact]
    public void EncryptThenDecryptWithTheSamePassphraseReturnsTheOriginalPlaintext()
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("This is a real, non-trivial plaintext payload.");

        byte[] archive = BackupArchiveCodec.Encrypt(plaintext, "correct-horse-battery-staple");
        byte[] decrypted = BackupArchiveCodec.Decrypt(archive, "correct-horse-battery-staple");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptProducesADifferentArchiveEveryTimeEvenForIdenticalInput()
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("identical input");

        byte[] first = BackupArchiveCodec.Encrypt(plaintext, "same-passphrase");
        byte[] second = BackupArchiveCodec.Encrypt(plaintext, "same-passphrase");

        // A fresh random salt and nonce per archive means two archives of the identical
        // plaintext, encrypted with the identical passphrase, must never be byte-for-byte equal.
        Assert.NotEqual(first, second);

        // Both must still decrypt back to the same original plaintext.
        Assert.Equal(plaintext, BackupArchiveCodec.Decrypt(first, "same-passphrase"));
        Assert.Equal(plaintext, BackupArchiveCodec.Decrypt(second, "same-passphrase"));
    }

    [Fact]
    public void DecryptWithTheWrongPassphraseThrowsBackupDecryptionFailedException()
    {
        byte[] archive = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("secret payload"),
            "the-real-passphrase"
        );

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(archive, "a-completely-different-passphrase")
        );
    }

    [Fact]
    public void DecryptWithATamperedCiphertextThrowsBackupDecryptionFailedException()
    {
        byte[] archive = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("this payload must never be silently corrupted"),
            "correct-horse-battery-staple"
        );

        // Flip one bit near the end of the archive, inside its own ciphertext -- authenticated
        // encryption must catch this via its own authentication tag check, the same real
        // cryptographic proof (not a heuristic) that also catches a wrong passphrase.
        archive[^1] ^= 0b0000_0001;

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(archive, "correct-horse-battery-staple")
        );
    }

    [Fact]
    public void DecryptWithATamperedHeaderThrowsBackupDecryptionFailedException()
    {
        byte[] archive = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("payload"),
            "correct-horse-battery-staple"
        );

        // Flip a bit inside the recorded nonce -- corrupting the envelope's own header, not just
        // the ciphertext, must be caught exactly the same way.
        archive[10] ^= 0b0000_0001;

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(archive, "correct-horse-battery-staple")
        );
    }

    [Fact]
    public void DecryptWithATooShortArrayThrowsBackupDecryptionFailedException() =>
        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt([1, 2, 3], "any-passphrase")
        );

    [Fact]
    public void DecryptWithTheWrongMagicHeaderThrowsBackupDecryptionFailedException()
    {
        byte[] notARealArchive = new byte[64];
        Array.Fill(notARealArchive, (byte)0x42);

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(notARealArchive, "any-passphrase")
        );
    }

    [Fact]
    public void WrapThenUnwrapReturnsTheOriginalPlaintextUnchanged()
    {
        byte[] plaintext = Encoding.UTF8.GetBytes("an archive an operator chose not to encrypt");

        byte[] archive = BackupArchiveCodec.Wrap(plaintext);
        byte[] unwrapped = BackupArchiveCodec.Unwrap(archive);

        Assert.Equal(plaintext, unwrapped);
    }

    [Fact]
    public void PeekKindReturnsUnencryptedForAWrappedArchiveAndEncryptedForAnEncryptedOne()
    {
        byte[] wrapped = BackupArchiveCodec.Wrap(Encoding.UTF8.GetBytes("plain"));
        byte[] encrypted = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("secret"),
            "correct-horse-battery-staple"
        );

        Assert.Equal(BackupArchiveKind.Unencrypted, BackupArchiveCodec.PeekKind(wrapped));
        Assert.Equal(BackupArchiveKind.Encrypted, BackupArchiveCodec.PeekKind(encrypted));
    }

    [Fact]
    public void PeekKindThrowsBackupArchiveInvalidExceptionForUnrecognizedBytes()
    {
        byte[] notARealArchive = new byte[64];
        Array.Fill(notARealArchive, (byte)0x42);

        Assert.Throws<BackupArchiveInvalidException>(() =>
            BackupArchiveCodec.PeekKind(notARealArchive)
        );
    }

    [Fact]
    public void PeekKindThrowsBackupArchiveInvalidExceptionForATooShortArray() =>
        Assert.Throws<BackupArchiveInvalidException>(() => BackupArchiveCodec.PeekKind([1, 2, 3]));

    [Fact]
    public void UnwrapOnAGenuinelyEncryptedArchiveThrowsBackupArchiveInvalidExceptionCleanly()
    {
        // The "vice versa" mismatch case: an archive that is genuinely encrypted must never be
        // silently misread as unencrypted plaintext -- Unwrap must reject it via its own header
        // check rather than returning corrupted-looking bytes.
        byte[] encrypted = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("secret payload"),
            "correct-horse-battery-staple"
        );

        Assert.Throws<BackupArchiveInvalidException>(() => BackupArchiveCodec.Unwrap(encrypted));
    }

    [Fact]
    public void DecryptOnAGenuinelyUnencryptedArchiveThrowsBackupDecryptionFailedExceptionCleanly()
    {
        // The other mismatch direction: an archive that was deliberately created without a
        // passphrase must never be treated as encrypted ciphertext -- Decrypt must reject it via
        // its own header check rather than attempting (and silently failing) authentication.
        byte[] wrapped = BackupArchiveCodec.Wrap(Encoding.UTF8.GetBytes("plain payload"));

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(wrapped, "any-passphrase")
        );
    }
}
