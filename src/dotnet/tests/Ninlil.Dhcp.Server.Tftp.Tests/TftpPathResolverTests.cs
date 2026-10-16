using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests <see cref="TftpPathResolver" /> on a temporary directory tree with real
///     symbolic links, plus one end-to-end check that <see cref="TftpServer" /> answers a request
///     through an escaping link with an Access Violation. The link tests return early on Windows,
///     where creating links needs a privilege.
/// </summary>
public sealed class TftpPathResolverTests : IDisposable
{
    private readonly string _base = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-resolver-{Guid.NewGuid()}"
    );

    private readonly string _root;
    private readonly string _outside;

    /// <summary>
    ///     This constructor builds <c>root/</c> with a boot file and <c>outside/</c> with a secret.
    /// </summary>
    public TftpPathResolverTests()
    {
        _root = Path.Combine(_base, "root");
        _outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(Path.Combine(_root, "images"));
        Directory.CreateDirectory(_outside);
        File.WriteAllText(Path.Combine(_root, "pxelinux.0"), "boot");
        File.WriteAllText(Path.Combine(_root, "images", "default.cfg"), "cfg");
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "secret");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_base))
            Directory.Delete(_base, recursive: true);
    }

    [Fact]
    public void TryResolve_WithALeadingSlash_ResolvesInsideTheRoot()
    {
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(
            realRoot,
            "/pxelinux.0",
            out string path,
            out _,
            out _
        );

        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "pxelinux.0"), path);
    }

    [Theory]
    [InlineData(@"boot\x64\wdsnbp.com")]
    [InlineData(@"\boot\x64\wdsnbp.com")]
    [InlineData(@"boot/x64\wdsnbp.com")]
    [InlineData(@"\/boot//x64\wdsnbp.com")]
    public void TryResolve_WithBackslashSeparators_ResolvesLikeTheSlashForm(string requested)
    {
        Directory.CreateDirectory(Path.Combine(_root, "boot", "x64"));
        File.WriteAllText(Path.Combine(_root, "boot", "x64", "wdsnbp.com"), "wds");
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(realRoot, requested, out string path, out _, out _);

        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "boot", "x64", "wdsnbp.com"), path);
    }

    [Fact]
    public void TryResolve_WithALeadingBackslash_ResolvesInsideTheRoot()
    {
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(
            realRoot,
            @"\pxelinux.0",
            out string path,
            out _,
            out _
        );

        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "pxelinux.0"), path);
    }

    [Theory]
    [InlineData(@"..\..\etc\passwd")]
    [InlineData(@"..\outside\secret.txt")]
    [InlineData(@"images\..\..\outside\secret.txt")]
    [InlineData(@"\..\outside\secret.txt")]
    public void TryResolve_WithABackslashTraversal_IsDenied(string requested)
    {
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(realRoot, requested, out _, out _, out bool notFound);

        Assert.False(ok);
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_WithADotDotEscape_IsDenied()
    {
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(
            realRoot,
            "../outside/secret.txt",
            out _,
            out _,
            out bool notFound
        );

        Assert.False(ok);
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ForAMissingFileOrADirectory_IsNotFound()
    {
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        Assert.False(TftpPathResolver.TryResolve(realRoot, "nope", out _, out _, out bool missing));
        Assert.True(missing);
        Assert.False(TftpPathResolver.TryResolve(realRoot, "images", out _, out _, out bool dir));
        Assert.True(dir);
    }

    [Fact]
    public void TryResolve_ForAFileLinkOutsideTheRoot_IsDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "leak"), Path.Combine(_outside, "secret.txt"));

        Assert.False(AllowedAndFound("leak", out bool notFound));
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ThroughADirectoryLinkOutsideTheRoot_IsDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateSymbolicLink(Path.Combine(_root, "boot"), _outside);

        Assert.False(AllowedAndFound("boot/secret.txt", out bool notFound));
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ThroughALinkChainThatEndsOutside_IsDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "b"), Path.Combine(_outside, "secret.txt"));
        File.CreateSymbolicLink(Path.Combine(_root, "a"), "b");

        Assert.False(AllowedAndFound("a", out bool notFound));
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ForALinkLoop_IsDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "l0"), "l1");
        File.CreateSymbolicLink(Path.Combine(_root, "l1"), "l0");

        Assert.False(AllowedAndFound("l0", out bool notFound));
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ForAChainOfFortyOneLinks_IsDenied()
    {
        if (OperatingSystem.IsWindows())
            return;
        for (int i = 0; i <= TftpPathResolver.MaximumLinkHops; i++)
        {
            string target = i == TftpPathResolver.MaximumLinkHops ? "pxelinux.0" : $"c{i + 1}";
            File.CreateSymbolicLink(Path.Combine(_root, $"c{i}"), target);
        }

        Assert.False(AllowedAndFound("c0", out bool notFound));
        Assert.False(notFound);
    }

    [Fact]
    public void TryResolve_ForADanglingLinkInsideTheRoot_IsNotFound()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "dangling"), "gone.bin");

        Assert.False(AllowedAndFound("dangling", out bool notFound));
        Assert.True(notFound);
    }

    [Fact]
    public void TryResolve_ForAnInRootLink_ResolvesToTheTarget()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "default"), "images/default.cfg");
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(realRoot, "default", out string path, out _, out _);

        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "images", "default.cfg"), path);
    }

    [Fact]
    public void TryResolve_ForALinkInsideALinkedInRootDirectory_ResolvesToTheTarget()
    {
        if (OperatingSystem.IsWindows())
            return;
        Directory.CreateSymbolicLink(Path.Combine(_root, "cfg"), "images");
        File.CreateSymbolicLink(Path.Combine(_root, "images", "default"), "default.cfg");
        string realRoot = TftpPathResolver.ResolveRealRoot(_root);

        bool ok = TftpPathResolver.TryResolve(
            realRoot,
            "cfg/default",
            out string path,
            out _,
            out _
        );

        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "images", "default.cfg"), path);
    }

    [Fact]
    public void TryResolve_WhenTheRootItselfIsALink_Works()
    {
        if (OperatingSystem.IsWindows())
            return;
        string rootLink = Path.Combine(_base, "rootlink");
        Directory.CreateSymbolicLink(rootLink, _root);
        File.CreateSymbolicLink(Path.Combine(_root, "alias"), "pxelinux.0");

        string realRoot = TftpPathResolver.ResolveRealRoot(rootLink);
        bool ok = TftpPathResolver.TryResolve(realRoot, "alias", out string path, out _, out _);

        Assert.Equal(TftpPathResolver.ResolveRealRoot(_root), realRoot);
        Assert.True(ok);
        Assert.Equal(Path.Combine(realRoot, "pxelinux.0"), path);
    }

    [Fact]
    public async Task Server_ForAReadThroughAnEscapingLink_ReturnsAccessViolation()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.CreateSymbolicLink(Path.Combine(_root, "leak"), Path.Combine(_outside, "secret.txt"));
        TftpServer server = new(
            Microsoft.Extensions.Options.Options.Create(
                new TftpServerOptions
                {
                    Enable = true,
                    Port = 0,
                    RootDirectory = _root,
                }
            ),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TftpServer>.Instance
        );
        await server.StartAsync(CancellationToken.None);

        try
        {
            using UdpClient client = new();
            byte[] request = [0, 1, .. "leak"u8.ToArray(), 0, .. "octet"u8.ToArray(), 0];
            await client.SendAsync(request, new IPEndPoint(IPAddress.Loopback, server.BoundPort));

            UdpReceiveResult result = await client
                .ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(
                TftpPacketReader.TryReadError(
                    result.Buffer,
                    out TftpErrorCodeEnum errorCode,
                    out _,
                    out _
                )
            );
            Assert.Equal(TftpErrorCodeEnum.AccessViolation, errorCode);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    private bool AllowedAndFound(string requested, out bool notFound) =>
        TftpPathResolver.TryResolve(
            TftpPathResolver.ResolveRealRoot(_root),
            requested,
            out _,
            out _,
            out notFound
        );
}
