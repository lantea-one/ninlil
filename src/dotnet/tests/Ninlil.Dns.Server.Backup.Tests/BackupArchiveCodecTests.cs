using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Crypto;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class tests <see cref="BackupArchiveCodec" /> on plain byte arrays, with no database
///     or files: round trips, rejection of wrong passphrases and changed bytes, and the
///     encrypted/unencrypted envelope checks.
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

        // A new salt and nonce per archive means two archives of the same plaintext and
        // passphrase must differ.
        Assert.NotEqual(first, second);

        // Both must still decrypt to the original plaintext.
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

        // Flip one bit in the ciphertext near the end; the authentication tag check must catch
        // it, as it catches a wrong passphrase.
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

        // Flip a bit in the stored salt (bytes 5 to 20, after the 5-byte header). A changed
        // header field must be caught the same way, because it changes the derived key.
        archive[10] ^= 0b0000_0001;

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(archive, "correct-horse-battery-staple")
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99_999)]
    [InlineData(10_000_001)]
    [InlineData(int.MaxValue)]
    public void DecryptWithAnIterationCountOutsideTheAcceptedRangeThrowsBeforeDerivingAKey(
        int iterations
    )
    {
        byte[] archive = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("payload"),
            "correct-horse-battery-staple"
        );

        // The count follows the 5-byte header and the 16-byte salt.
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(21, sizeof(int)), iterations);

        Stopwatch stopwatch = Stopwatch.StartNew();
        BackupDecryptionFailedException exception = Assert.Throws<BackupDecryptionFailedException>(
            () =>
                BackupArchiveCodec.Decrypt(archive, "correct-horse-battery-staple")
        );

        Assert.Contains("outside the accepted range", exception.Message);
        // A derivation of even the lowest count takes longer than this.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TheIterationCountIsStoredLittleEndianAndTheCurrentCountStillDecrypts()
    {
        byte[] archive = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("payload"),
            "correct-horse-battery-staple"
        );

        Assert.Equal(600_000, BinaryPrimitives.ReadInt32LittleEndian(archive.AsSpan(21, 4)));
        Assert.Equal(
            Encoding.UTF8.GetBytes("payload"),
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
        // An encrypted archive must not be read as unencrypted: Unwrap rejects it by its header
        // instead of returning ciphertext as the payload.
        byte[] encrypted = BackupArchiveCodec.Encrypt(
            Encoding.UTF8.GetBytes("secret payload"),
            "correct-horse-battery-staple"
        );

        Assert.Throws<BackupArchiveInvalidException>(() => BackupArchiveCodec.Unwrap(encrypted));
    }

    [Fact]
    public void DecryptOnAGenuinelyUnencryptedArchiveThrowsBackupDecryptionFailedExceptionCleanly()
    {
        // The other direction: Decrypt rejects an unencrypted archive by its header instead of
        // trying to authenticate it.
        byte[] wrapped = BackupArchiveCodec.Wrap(Encoding.UTF8.GetBytes("plain payload"));

        Assert.Throws<BackupDecryptionFailedException>(() =>
            BackupArchiveCodec.Decrypt(wrapped, "any-passphrase")
        );
    }
}
