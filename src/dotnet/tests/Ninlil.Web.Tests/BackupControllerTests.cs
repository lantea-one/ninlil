using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Exceptions;
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
///     exception handler answers with 409 (see <c>NinlilExceptionHandlerTests</c>).
/// </summary>
public class BackupControllerTests
{
    [Fact]
    public async Task Restore_WhileAnotherRestoreIsRunning_ThrowsAConflictThatBecomesHttp409()
    {
        BackupController controller = new(
            new FakeBackupService(new BackupRestoreInProgressException()),
            NullLogger<BackupController>.Instance
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
            NullLogger<BackupController>.Instance
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Restore(NewRequest(), CancellationToken.None)
        );
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
        ) => throw restoreFailure;
    }
}
