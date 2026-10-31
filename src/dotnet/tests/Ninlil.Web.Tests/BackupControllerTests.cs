using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authentication;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests how <see cref="BackupController" /> maps the backup service's exceptions,
///     with a fake service so no restore has to be timed. A restore that is refused because
///     another one is running becomes a <see cref="ConflictException" />, which the shared
///     exception handler answers with 409 (see <c>NinlilExceptionHandlerTests</c>). It also
///     checks that a restore, finished or failed part-way, flushes the access-token cache, and
///     that a request refused for a missing archive does not.
/// </summary>
public class BackupControllerTests
{
    [Fact]
    public async Task Restore_WhileAnotherRestoreIsRunning_ThrowsAConflictThatBecomesHttp409()
    {
        BackupController controller = new(
            new FakeBackupService(new BackupRestoreInProgressException()),
            NullLogger<BackupController>.Instance,
            new CountingAccessTokenRevalidator()
        );

        ConflictException exception = await Assert.ThrowsAsync<ConflictException>(() =>
            controller.Restore(NewRequest(), CancellationToken.None)
        );

        Assert.Contains("already running", exception.Message);
    }

    [Fact]
    public async Task Restore_OfAnInvalidArchive_StillThrowsAValidationException()
    {
        BackupController controller = new(
            new FakeBackupService(new BackupArchiveInvalidException("bad archive")),
            NullLogger<BackupController>.Instance,
            new CountingAccessTokenRevalidator()
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Restore(NewRequest(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Restore_AfterASuccessfulRestore_FlushesTheAccessTokenCache()
    {
        CountingAccessTokenRevalidator revalidator = new();
        BackupController controller = new(
            new FakeBackupService(null),
            NullLogger<BackupController>.Instance,
            revalidator
        );

        await controller.Restore(NewRequest(), CancellationToken.None);

        Assert.Equal(1, revalidator.InvalidateCount);
    }

    [Fact]
    public async Task Restore_ThatFailsPartWay_StillFlushesTheAccessTokenCache()
    {
        CountingAccessTokenRevalidator revalidator = new();
        BackupController controller = new(
            new FakeBackupService(new BackupArchiveInvalidException("bad archive")),
            NullLogger<BackupController>.Instance,
            revalidator
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Restore(NewRequest(), CancellationToken.None)
        );

        Assert.Equal(1, revalidator.InvalidateCount);
    }

    [Fact]
    public async Task Restore_WithoutAnArchive_DoesNotFlushTheAccessTokenCache()
    {
        CountingAccessTokenRevalidator revalidator = new();
        BackupController controller = new(
            new FakeBackupService(null),
            NullLogger<BackupController>.Instance,
            revalidator
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Restore(new BackupRestoreRequest(), CancellationToken.None)
        );

        Assert.Equal(0, revalidator.InvalidateCount);
    }

    private static BackupRestoreRequest NewRequest()
    {
        byte[] content = Encoding.UTF8.GetBytes("archive");
        return new BackupRestoreRequest
        {
            Archive = new FormFile(
                new MemoryStream(content),
                0,
                content.Length,
                "Archive",
                "a.ltbk"
            ),
        };
    }

    private sealed class FakeBackupService(Exception restoreFailure) : IBackupService
    {
        public Task<byte[]> CreateBackupAsync(
            string passphrase,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<BackupRestoreSummary> RestoreBackupAsync(
            Stream archiveStream,
            string passphrase,
            CancellationToken stoppingToken = default,
            bool restoreFiles = true
        ) =>
            restoreFailure is null
                ? Task.FromResult(new BackupRestoreSummary(0, new Dictionary<string, int>()))
                : throw restoreFailure;
    }

    /// <summary>
    ///     This class is a fake <see cref="IAccessTokenRevalidator" /> that only counts the cache
    ///     flushes, so a test can check that a restore flushes it. It never revalidates anything.
    /// </summary>
    private sealed class CountingAccessTokenRevalidator : IAccessTokenRevalidator
    {
        /// <summary>
        ///     This property contains how many times <see cref="Invalidate" /> was called.
        /// </summary>
        public int InvalidateCount { get; private set; }

        /// <inheritdoc />
        public Task<bool> RevalidateAsync(
            ClaimsPrincipal principal,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(true);

        /// <inheritdoc />
        public void Invalidate() => InvalidateCount++;
    }
}
