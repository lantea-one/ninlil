<#
.SYNOPSIS
Starts a Ninlil executable on free loopback ports and checks that it serves /status, the admin
console and a DNS query.

.DESCRIPTION
This is the "windows-smoke" job of .forgejo/workflows/pipeline.yml. It proves that the Windows
executable published by publish-binary.sh starts, serves its embedded admin console and answers a
DNS query. It is a smoke test, not a test suite.

The executable comes from one of two places:
  - -ExecutablePath: a local file (how a developer runs this under pwsh on Linux, against
    ninlil-linux-amd64).
  - otherwise the attachment named -Asset on the draft release for -ReleaseTag, downloaded
    through the Forgejo API.

The file is copied into a fresh temporary folder first, because Ninlil keeps its data directory
next to the executable. The process gets three free loopback ports, a fixed bootstrap password
and its own native-library extraction folder. Both log files are printed when a check fails.

Windows PowerShell 5.1 and PowerShell 7 are both supported: the pipeline uses 5.1 because it is
always present on the runner.

.PARAMETER ReleaseTag
The draft release's tag (the computed version). Required unless -ExecutablePath is given.

.PARAMETER Asset
The attachment to download, for example ninlil-windows-amd64.exe.

.PARAMETER ExecutablePath
A local executable to test instead of downloading one.

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
    [string] $Asset = 'ninlil-windows-amd64.exe',
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

# Sends one A query for localhost. over UDP and returns the reply bytes, or throws on timeout.
function Send-DnsQuery([int] $port, [byte[]] $id) {
    $query = New-Object 'System.Collections.Generic.List[byte]'
    $query.AddRange($id)
    $query.AddRange([byte[]](0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00))
    $query.Add(9)
    $query.AddRange([Text.Encoding]::ASCII.GetBytes('localhost'))
    $query.AddRange([byte[]](0x00, 0x00, 0x01, 0x00, 0x01))
    $client = New-Object Net.Sockets.UdpClient
    try {
        $client.Client.ReceiveTimeout = 5000
        $client.Connect([Net.IPAddress]::Loopback, $port)
        [void] $client.Send($query.ToArray(), $query.Count)
        $remote = New-Object Net.IPEndPoint([Net.IPAddress]::Any, 0)
        return , $client.Receive([ref] $remote)
    } finally {
        $client.Close()
    }
}

$failed = $false
try {
    $executable = Join-Path $work 'ninlil-under-test.exe'
    if ($ExecutablePath) {
        Copy-Item -LiteralPath $ExecutablePath -Destination $executable
    } else {
        Get-DraftAsset $executable
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
    $env:NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD = 'Smoke-Test-Passw0rd!'
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $work 'extract'
    # The test must not inherit the runner's own token.
    Remove-Item Env:NINLIL_CI_TOKEN -ErrorAction SilentlyContinue

    Write-Host "Starting $executable (DNS $dnsPort, HTTP $httpPort, HTTPS $httpsPort)"
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
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failed) { exit 1 }
