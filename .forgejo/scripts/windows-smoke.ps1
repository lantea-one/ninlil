<#
.SYNOPSIS
Starts a Ninlil executable on free loopback ports and checks that it serves /status, the admin
console, HTTPS on the first start, DNS-over-TLS and DNS-over-QUIC, and a DNS query.

.DESCRIPTION
This is the "windows-smoke" job of .forgejo/workflows/pipeline.yml. It proves that the Windows
executable published by publish-binary.sh starts, serves its embedded admin console and answers a
DNS query. It is a smoke test, not a test suite.

The HTTPS, DNS-over-TLS and DNS-over-QUIC checks run against the self-signed certificate the
executable generates on its first start. On Windows that key must be one Schannel can use for a
server handshake, so a failure here means the first start is broken even though a restart would
work. DNS-over-QUIC also needs msquic.dll, which System.Net.Quic loads only from the folder of the
executable; the release ships it in the zip beside the executable. PowerShell 7 sends a real DoQ query through
.NET's QuicConnection. Windows PowerShell 5.1 has no QUIC client, so there the script requires
a healthy /status (it is unhealthy when an enabled transport is not listening) and no "QUIC is
not supported" line in the executable's log.

The package comes from one of two places:
  - -ExecutablePath: a local file (how a developer runs this under pwsh on Linux, against
    ninlil-linux-amd64). A .zip is expanded like the release asset; a bare executable is copied
    together with a msquic.dll that sits beside it, if there is one.
  - otherwise the attachment named -Asset on the draft release for -ReleaseTag, downloaded
    through the Forgejo API. The Windows asset is a zip holding the executable and msquic.dll,
    which the script expands and tests exactly as shipped.

The package is placed in a fresh temporary folder first, because Ninlil keeps its data directory
next to the executable. On Windows Ninlil also keeps the certificate it generates in the current
user's certificate store (friendly name "Ninlil TLS ..."), so the script removes the store
entries, and their keys, that appeared during the run. The process gets three free loopback ports, a fixed bootstrap password
and its own native-library extraction folder. Both log files are printed when a check fails.

Windows PowerShell 5.1 and PowerShell 7 are both supported: the pipeline uses 5.1 because it is
always present on the runner.

.PARAMETER ReleaseTag
The draft release's tag (the computed version). Required unless -ExecutablePath is given.

.PARAMETER Asset
The attachment to download, for example ninlil-windows-amd64.zip.

.PARAMETER ExecutablePath
A local executable, or a zip with the same layout as the release asset, to test instead of
downloading one.

.PARAMETER StartupTimeoutSeconds
How long to wait for /status to answer 200. Default 120 (the first start extracts the bundle).

.PARAMETER DnsQueryPortOverride
Test switch: sends the DNS query to this port instead of the one the server was given, which
proves the script fails when nothing answers. Leave unset in the pipeline.

.NOTES
Environment: NINLIL_CI_TOKEN (download only; sent as a header built in this process, never on a
command line), FORGEJO_SERVER_URL or GITHUB_SERVER_URL and FORGEJO_REPOSITORY or
GITHUB_REPOSITORY (download only; set by the Actions runner).
Exit status: 0 when every check passes, 1 otherwise.
#>
[CmdletBinding()]
param(
    [string] $ReleaseTag,
    [string] $Asset = 'ninlil-windows-amd64.zip',
    [string] $ExecutablePath,
    [int] $StartupTimeoutSeconds = 120,
    [int] $DnsQueryPortOverride = 0
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell 5.1 negotiates TLS 1.0 by default; the Forgejo server needs 1.2.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$work = Join-Path ([IO.Path]::GetTempPath()) ('ninlil-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$process = $null
# The Ninlil certificates the user's store holds before the run, so the clean-up removes only
# what the run added. The store exists only on Windows.
$onWindows = ($PSVersionTable.PSVersion.Major -lt 6) -or $IsWindows
function Get-NinlilStoreEntries {
    if (-not $onWindows) { return @() }
    return @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.FriendlyName -like 'Ninlil TLS *' })
}
$storeBefore = @(Get-NinlilStoreEntries | ForEach-Object { $_.Thumbprint })
$stdoutFile = Join-Path $work 'stdout.log'
$stderrFile = Join-Path $work 'stderr.log'

# Prints both process logs, to explain a failure.
function Show-Logs {
    foreach ($file in @($stdoutFile, $stderrFile)) {
        Write-Host "---- $file"
        if (Test-Path $file) { Get-Content $file | Select-Object -Last 200 | ForEach-Object { Write-Host $_ } }
    }
}

# Downloads the release attachment $Asset from the draft for $ReleaseTag into $destination.
function Get-DraftAsset([string] $destination) {
    if (-not $ReleaseTag) { throw 'Give -ReleaseTag, or -ExecutablePath for a local file.' }
    if (-not $env:NINLIL_CI_TOKEN) { throw 'NINLIL_CI_TOKEN is required to download the asset.' }
    $server = if ($env:FORGEJO_SERVER_URL) { $env:FORGEJO_SERVER_URL } else { $env:GITHUB_SERVER_URL }
    $repository = if ($env:FORGEJO_REPOSITORY) { $env:FORGEJO_REPOSITORY } else { $env:GITHUB_REPOSITORY }
    if (-not $server -or -not $repository) { throw 'No server URL or repository in the environment.' }
    $headers = @{ Authorization = "token $($env:NINLIL_CI_TOKEN)" }
    $api = "$server/api/v1/repos/$repository"

    # Read every page: a failed page throws instead of reading as "no draft".
    $release = $null
    for ($page = 1; -not $release; $page++) {
        $drafts = @(Invoke-RestMethod -Headers $headers -UseBasicParsing -Uri "$api/releases?draft=true&limit=50&page=$page")
        if ($drafts.Count -eq 0) { break }
        $release = $drafts | Where-Object { $_.draft -and $_.tag_name -eq $ReleaseTag } | Select-Object -First 1
    }
    if (-not $release) { throw "No draft release exists for $ReleaseTag." }
    $attachment = @($release.assets | Where-Object { $_.name -eq $Asset }) | Select-Object -First 1
    if (-not $attachment) { throw "The draft for $ReleaseTag has no attachment named $Asset." }
    Write-Host "Downloading $Asset"
    Invoke-WebRequest -Headers $headers -UseBasicParsing -Uri $attachment.browser_download_url -OutFile $destination
}

# Returns a TCP port the operating system reports as free, also free for UDP (the DNS port).
function Get-FreePort([bool] $alsoUdp) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $listener.Stop()
        if (-not $alsoUdp) { return $port }
        try {
            $udp = New-Object Net.Sockets.UdpClient(New-Object Net.IPEndPoint([Net.IPAddress]::Loopback, $port))
            $udp.Close()
            return $port
        } catch {
            continue
        }
    }
    throw 'No free port found.'
}

# A small compiled client for the TLS checks. It is C# because the certificate callback runs on a
# thread without a PowerShell runspace, where a script block would fail; the code stays at C# 5
# so Windows PowerShell 5.1 can compile it. The server's certificate is self-signed, so every
# certificate is accepted: the checks are about the handshake, not about trust.
Add-Type -TypeDefinition @'
using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

public static class SmokeTls
{
    private static SslStream Connect(int port)
    {
        TcpClient client = new TcpClient();
        client.Connect("127.0.0.1", port);
        client.ReceiveTimeout = 10000;
        client.SendTimeout = 10000;
        SslStream stream = new SslStream(client.GetStream(), false, delegate { return true; });
        // 0 lets the operating system choose; Tls12 and Tls13 are named by value because the
        // enum of a .NET Framework 4.x host may not define Tls13.
        stream.AuthenticateAsClient("localhost", null, (SslProtocols)0, false);
        return stream;
    }

    // Returns the status line of GET /status over HTTPS (HTTP/1.0, so the server closes the connection).
    public static string HttpsStatusLine(int port)
    {
        using (SslStream stream = Connect(port))
        {
            byte[] request = Encoding.ASCII.GetBytes("GET /status HTTP/1.0\r\nHost: localhost\r\n\r\n");
            stream.Write(request, 0, request.Length);
            byte[] buffer = new byte[256];
            int read = stream.Read(buffer, 0, buffer.Length);
            string text = Encoding.ASCII.GetString(buffer, 0, read);
            int end = text.IndexOf('\r');
            return end < 0 ? text : text.Substring(0, end);
        }
    }

    // Sends one length-prefixed DNS message over DNS-over-TLS (RFC 7858) and returns the reply.
    public static byte[] DotQuery(int port, byte[] query)
    {
        using (SslStream stream = Connect(port))
        {
            byte[] framed = new byte[query.Length + 2];
            framed[0] = (byte)(query.Length >> 8);
            framed[1] = (byte)query.Length;
            Array.Copy(query, 0, framed, 2, query.Length);
            stream.Write(framed, 0, framed.Length);
            byte[] prefix = ReadExactly(stream, 2);
            return ReadExactly(stream, (prefix[0] << 8) | prefix[1]);
        }
    }

    private static byte[] ReadExactly(SslStream stream, int count)
    {
        byte[] data = new byte[count];
        int done = 0;
        while (done < count)
        {
            int read = stream.Read(data, done, count - done);
            if (read <= 0) throw new System.IO.EndOfStreamException("The connection closed early.");
            done += read;
        }
        return data;
    }
}
'@

# Builds the wire bytes of one A query for localhost. with the given 2-byte id and the RD flag.
function New-DnsQuery([byte[]] $id) {
    $query = New-Object 'System.Collections.Generic.List[byte]'
    $query.AddRange($id)
    $query.AddRange([byte[]](0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00))
    $query.Add(9)
    $query.AddRange([Text.Encoding]::ASCII.GetBytes('localhost'))
    $query.AddRange([byte[]](0x00, 0x00, 0x01, 0x00, 0x01))
    return , $query.ToArray()
}

# Sends one DNS-over-QUIC query (RFC 9250 §4.2.1: message id 0, one stream per query) and returns
# the reply. Only PowerShell 7 (.NET 7 or newer) has QuicConnection.
function Send-DoqQuery([int] $port) {
    Add-Type -ReferencedAssemblies System.Net.Quic, System.Net.Security, System.Net.Primitives, System.Security.Cryptography, System.Collections, System.Runtime -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Threading.Tasks;

public static class SmokeDoq
{
    public static async Task<byte[]> QueryAsync(int port, byte[] query)
    {
        var options = new QuicClientConnectionOptions
        {
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, port),
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol("doq") },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            },
        };
        await using QuicConnection connection = await QuicConnection.ConnectAsync(options);
        await using QuicStream stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
        byte[] framed = new byte[query.Length + 2];
        framed[0] = (byte)(query.Length >> 8);
        framed[1] = (byte)query.Length;
        query.CopyTo(framed, 2);
        await stream.WriteAsync(framed, completeWrites: true);
        byte[] prefix = new byte[2];
        await stream.ReadExactlyAsync(prefix);
        byte[] reply = new byte[(prefix[0] << 8) | prefix[1]];
        await stream.ReadExactlyAsync(reply);
        return reply;
    }
}
'@
    $query = New-DnsQuery ([byte[]](0, 0))
    return , [SmokeDoq]::QueryAsync($port, $query).GetAwaiter().GetResult()
}

# Sends one A query for localhost. over UDP and returns the reply bytes, or throws on timeout.
function Send-DnsQuery([int] $port, [byte[]] $id) {
    $query = New-DnsQuery $id
    $client = New-Object Net.Sockets.UdpClient
    try {
        $client.Client.ReceiveTimeout = 5000
        $client.Connect([Net.IPAddress]::Loopback, $port)
        [void] $client.Send($query, $query.Length)
        $remote = New-Object Net.IPEndPoint([Net.IPAddress]::Any, 0)
        return , $client.Receive([ref] $remote)
    } finally {
        $client.Close()
    }
}

$failed = $false
try {
    $package = Join-Path $work 'package'
    New-Item -ItemType Directory -Path $package | Out-Null
    $source = $ExecutablePath
    if (-not $source) {
        $source = Join-Path $work $Asset
        Get-DraftAsset $source
    }
    if ($source -like '*.zip') {
        # Expand-Archive is in Windows PowerShell 5.1 and PowerShell 7.
        Expand-Archive -LiteralPath $source -DestinationPath $package
        $found = @(Get-ChildItem -LiteralPath $package -Filter '*.exe' | Select-Object -First 1)
        if ($found.Count -eq 0) { throw "The zip $Asset holds no executable." }
        $executable = $found[0].FullName
        if (-not (Test-Path (Join-Path $package 'msquic.dll'))) { throw 'The zip holds no msquic.dll next to the executable.' }
    } else {
        $executable = Join-Path $package 'ninlil-under-test.exe'
        Copy-Item -LiteralPath $source -Destination $executable
        $quicLibrary = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $source)) 'msquic.dll'
        if (Test-Path $quicLibrary) { Copy-Item -LiteralPath $quicLibrary -Destination $package }
    }
    if ($PSVersionTable.PSVersion.Major -ge 6 -and -not $IsWindows) {
        # Local run on Linux or macOS against a non-Windows binary.
        chmod +x $executable
    }

    $dnsPort = Get-FreePort $true
    $httpPort = Get-FreePort $false
    $httpsPort = Get-FreePort $false
    $env:NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT = "$dnsPort"
    $env:NINLIL_DNS_SERVER_WEB_HTTP_PORT = "$httpPort"
    $env:NINLIL_DNS_SERVER_WEB_HTTPS_PORT = "$httpsPort"
    $dotPort = Get-FreePort $false
    $doqPort = Get-FreePort $true
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_ENABLE = 'true'
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_PORT = "$dotPort"
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_ENABLE = 'true'
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_PORT = "$doqPort"
    # The runner's own network interfaces may carry other services, and the checks only use
    # loopback, so every listener binds loopback.
    $env:NINLIL_DNS_SERVER_PROTOCOL_PLAIN_LISTEN_ADDRESSES = '127.0.0.1'
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_LISTEN_ADDRESSES = '127.0.0.1'
    $env:NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_LISTEN_ADDRESSES = '127.0.0.1'
    $env:NINLIL_DNS_SERVER_WEB_LISTEN_ADDRESSES = '127.0.0.1'
    $env:NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD = 'Smoke-Test-Passw0rd!'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $work 'extract'
    # The test must not inherit the runner's own token.
    Remove-Item Env:NINLIL_CI_TOKEN -ErrorAction SilentlyContinue

    Write-Host "Starting $executable (DNS $dnsPort, HTTP $httpPort, HTTPS $httpsPort, DoT $dotPort, DoQ $doqPort)"
    $process = Start-Process -FilePath $executable -WorkingDirectory $work -PassThru `
        -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile

    # 1. /status answers 200.
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $healthy = $false
    while (-not $healthy) {
        if ($process.HasExited) { throw "The executable exited with code $($process.ExitCode) before /status answered." }
        if ((Get-Date) -gt $deadline) { throw "/status did not answer 200 within $StartupTimeoutSeconds seconds." }
        try {
            $status = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$httpPort/status" -TimeoutSec 5
            $healthy = ($status.StatusCode -eq 200)
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    Write-Host "ok: /status answered 200: $($status.Content)"

    # 2. The embedded admin console is served. index.html holds the <app-root> element.
    $index = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$httpPort/" -TimeoutSec 10
    if ($index.StatusCode -ne 200 -or $index.Content -notmatch '<app-root') {
        throw 'GET / did not return the admin console (no <app-root> element).'
    }
    Write-Host 'ok: the admin console loads'

    # 3. A DNS query over UDP gets a reply with the same id and QR=1; any RCODE passes, because
    # the check is about the listener, not about resolution.
    $id = New-Object byte[] 2
    (New-Object Random).NextBytes($id)
    $target = if ($DnsQueryPortOverride -gt 0) { $DnsQueryPortOverride } else { $dnsPort }
    $reply = Send-DnsQuery $target $id
    if ($reply.Length -lt 12 -or $reply[0] -ne $id[0] -or $reply[1] -ne $id[1] -or ($reply[2] -band 0x80) -eq 0) {
        throw 'The DNS reply has the wrong id or is not a response.'
    }
    Write-Host "ok: DNS answered with RCODE $($reply[3] -band 0x0F)"
    # 4. HTTPS on the first start. This is the first start of this executable in a fresh folder, so
    # the certificate is the freshly generated one, not one reloaded from the saved PFX.
    $line = [SmokeTls]::HttpsStatusLine($httpsPort)
    if ($line -notmatch '^HTTP/1\.[01] 200') { throw "GET /status over HTTPS answered '$line'." }
    Write-Host "ok: HTTPS handshake and request on the first start ($line)"

    # 5. DNS-over-TLS: handshake with the same certificate, then one query.
    $tlsId = New-Object byte[] 2
    (New-Object Random).NextBytes($tlsId)
    $tlsReply = [SmokeTls]::DotQuery($dotPort, (New-DnsQuery $tlsId))
    if ($tlsReply.Length -lt 12 -or $tlsReply[0] -ne $tlsId[0] -or $tlsReply[1] -ne $tlsId[1] -or ($tlsReply[2] -band 0x80) -eq 0) {
        throw 'The DNS-over-TLS reply has the wrong id or is not a response.'
    }
    Write-Host "ok: DNS-over-TLS answered with RCODE $($tlsReply[3] -band 0x0F)"

    # 6. DNS-over-QUIC. Every enabled transport must be listening: /status is Unhealthy otherwise.
    $health = ($status.Content | ConvertFrom-Json).status
    if ($health -ne 'Healthy') { throw "/status reports '$health', not Healthy: $($status.Content)" }
    if ((Get-Content $stdoutFile -Raw) -match 'QUIC is not supported') {
        throw 'The executable reports that QUIC is not supported (msquic.dll did not load beside it).'
    }
    if ($PSVersionTable.PSVersion.Major -ge 7 -and $IsWindows) {
        $doqReply = Send-DoqQuery $doqPort
        if ($doqReply.Length -lt 12 -or ($doqReply[2] -band 0x80) -eq 0) { throw 'The DNS-over-QUIC reply is not a response.' }
        Write-Host "ok: DNS-over-QUIC answered with RCODE $($doqReply[3] -band 0x0F)"
    } else {
        Write-Host 'ok: DNS-over-QUIC is listening (healthy /status, no QUIC error in the log; no QUIC client in this PowerShell)'
    }
    Write-Host 'Smoke test passed.'
} catch {
    $failed = $true
    Write-Host "Smoke test FAILED: $($_.Exception.Message)"
} finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void] $process.WaitForExit(15000)
    }
    if ($failed) { Show-Logs }
    # Delete each new store entry's key first: removing the entry does not remove the key file.
    foreach ($entry in @(Get-NinlilStoreEntries | Where-Object { $storeBefore -notcontains $_.Thumbprint })) {
        try {
            $key = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($entry)
            if ($key) { $key.Key.Delete() }
        } catch { Write-Host "Could not delete the key of $($entry.Thumbprint): $($_.Exception.Message)" }
        Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($entry.Thumbprint)" -ErrorAction SilentlyContinue
    }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failed) { exit 1 }
