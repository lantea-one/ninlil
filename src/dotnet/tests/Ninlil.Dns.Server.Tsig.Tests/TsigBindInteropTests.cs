using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class runs BIND's <c>dig</c> and <c>nsupdate</c> against a test responder built on
///     <see cref="TsigService" />, to prove that Ninlil and BIND accept each other's TSIG
///     signatures. The responder listens on 127.0.0.1 (UDP and TCP on one OS-assigned port),
///     verifies what the tool sends with <see cref="ITsigService.VerifyAsync" /> and answers with
///     replies signed by the service, so a tool that rejects a signature fails the assertion. A
///     test returns early, and so reports as passed, when the tool is not on <c>PATH</c>.
///     The responder is a hand-written loop, not the query pipeline, because the aim is the TSIG
///     bytes, not the DNS logic.
/// </summary>
public class TsigBindInteropTests(TsigServiceTestFixture fixture)
    : IClassFixture<TsigServiceTestFixture>
{
    private const string KeyName = "Mixed.Case.Key";
    private const string ZoneName = "interop.example.";

    private static readonly string[] AxfrHosts = ["a", "b", "c", "d"];

    /// <summary>
    ///     This method tells whether an executable is on <c>PATH</c>.
    /// </summary>
    private static bool HasTool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, name)));

    /// <summary>
    ///     This method creates an enabled hmac-sha256 key whose name starts with
    ///     <see cref="KeyName" /> (mixed case on purpose) and returns the name and the secret in
    ///     base64.
    /// </summary>
    private async Task<(string Name, string Secret)> NewKeyAsync()
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDataProtector protector = scope
            .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");

        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);
        string secret = Convert.ToBase64String(rawSecret);

        // Unique per test so the shared database never collides. The tools get the mixed-case
        // name on their command line; BIND lowercases it on the wire, and keys are stored
        // lowercase (TsigKeyLookupCache), so the row is lowercase.
        string name = $"{KeyName}.{Guid.NewGuid():N}";

        await keyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = name.ToLowerInvariant(),
                AlgorithmName = "hmac-sha256",
                SecretEncrypted = protector.Protect(secret),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        return (name, secret);
    }

    /// <summary>
    ///     This class is the test responder: it answers SOA queries, UPDATE messages (UDP) and AXFR
    ///     (TCP) for <see cref="ZoneName" />, after verifying the request's TSIG.
    /// </summary>
    private sealed class Responder : IDisposable
    {
        private readonly TsigServiceTestFixture _fixture;
        private readonly TcpListener _tcp;
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(60));

        public Responder(TsigServiceTestFixture fixture)
        {
            _fixture = fixture;

            while (true)
            {
                TcpListener tcp = new(IPAddress.Loopback, 0);
                tcp.Start();
                int candidate = ((IPEndPoint)tcp.LocalEndpoint).Port;

                try
                {
                    _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, candidate));
                    _tcp = tcp;
                    Port = candidate;
                    break;
                }
                catch (SocketException)
                {
                    tcp.Stop();
                }
            }

            _ = Task.Run(UdpLoopAsync);
            _ = Task.Run(TcpLoopAsync);
        }

        public int Port { get; }

        private async Task UdpLoopAsync()
        {
            try
            {
                while (true)
                {
                    UdpReceiveResult received = await _udp.ReceiveAsync(_cts.Token);
                    DnsMessage reply = (await AnswerAsync(received.Buffer)).Single();
                    await _udp.SendAsync(
                        DnsMessageWriter.Write(reply),
                        received.RemoteEndPoint,
                        _cts.Token
                    );
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        private async Task TcpLoopAsync()
        {
            try
            {
                while (true)
                {
                    using TcpClient accepted = await _tcp.AcceptTcpClientAsync(_cts.Token);
                    await using NetworkStream stream = accepted.GetStream();
                    byte[] prefix = new byte[2];
                    await stream.ReadExactlyAsync(prefix, _cts.Token);
                    byte[] raw = new byte[(prefix[0] << 8) | prefix[1]];
                    await stream.ReadExactlyAsync(raw, _cts.Token);

                    foreach (DnsMessage message in await AnswerAsync(raw))
                    {
                        byte[] bytes = DnsMessageWriter.Write(message);
                        await stream.WriteAsync(
                            new[] { (byte)(bytes.Length >> 8), (byte)bytes.Length },
                            _cts.Token
                        );
                        await stream.WriteAsync(bytes, _cts.Token);
                    }
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        private static DnsMessage Reply(DnsMessage request) =>
            new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                IsAuthoritativeAnswer = true,
                Questions = [.. request.Questions],
            };

        private static DnsResourceRecord Soa() =>
            new(
                DnsName.Parse(ZoneName),
                DnsResourceRecordTypeEnum.Soa,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(
                        DnsName.Parse("ns1.interop.example."),
                        DnsName.Parse("admin.interop.example."),
                        7,
                        3600,
                        900,
                        604800,
                        3600
                    )
                )
            );

        private static DnsResourceRecord Host(string label) =>
            new(
                DnsName.Parse($"{label}.{ZoneName}"),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.1"))
                )
            );

        /// <summary>
        ///     This method verifies the request and builds the reply messages: an error reply for
        ///     a failed signature, otherwise signed answers (four AXFR messages, the third one
        ///     unsigned, for an AXFR; one message for anything else).
        /// </summary>
        private async Task<List<DnsMessage>> AnswerAsync(byte[] raw)
        {
            DnsMessage request = DnsMessageReader.Read(raw, out int tsigOffset);
            await using AsyncServiceScope scope = _fixture.Services.CreateAsyncScope();
            ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

            DnsMessage reply = Reply(request);
            TsigVerificationResult result = await tsigService.VerifyAsync(raw, tsigOffset, request);

            if (!result.IsValid)
                return [await tsigService.SignErrorResponseAsync(reply, request.Tsig, result)];

            string keyName = request.Tsig.KeyName.ToString();
            ushort id = request.Id;

            if (request.Questions[0].Type == DnsResourceRecordTypeEnum.Axfr)
                return await AxfrAsync(request, tsigService, keyName, result.Key);

            if (request.Opcode == DnsOpcodeEnum.Query)
                reply.Answers.Add(Soa());

            return [await tsigService.SignResponseAsync(reply, keyName, id, request.Tsig.Mac)];
        }

        /// <summary>
        ///     This method builds the four AXFR messages: signed, signed, unsigned, signed. The
        ///     last one chains from the second message's MAC and digests the unsigned third
        ///     message between them (RFC 8945 §5.3.1), which <see cref="ITsigService" /> has no
        ///     method to produce, so its MAC is computed with the codec directly.
        /// </summary>
        private async Task<List<DnsMessage>> AxfrAsync(
            DnsMessage request,
            ITsigService tsigService,
            string keyName,
            TsigKeyEntity key
        )
        {
            DnsMessage m1 = Reply(request);
            m1.Answers.Add(Soa());
            m1.Answers.Add(Host(AxfrHosts[0]));
            DnsMessage m2 = Reply(request);
            m2.Answers.Add(Host(AxfrHosts[1]));
            DnsMessage m3 = Reply(request);
            m3.Answers.Add(Host(AxfrHosts[2]));
            DnsMessage m4 = Reply(request);
            m4.Answers.Add(Host(AxfrHosts[3]));
            m4.Answers.Add(Soa());

            m1 = await tsigService.SignResponseAsync(m1, keyName, request.Id, request.Tsig.Mac);
            m2 = await tsigService.SignContinuationAsync(m2, keyName, request.Id, m1.Tsig.Mac);

            byte[] secret = Convert.FromBase64String(
                _fixture
                    .Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret")
                    .Unprotect(key.SecretEncrypted)
            );
            TsigAlgorithm algorithm = TsigAlgorithm.Find(key.AlgorithmName);
            byte[] m3Bytes = DnsMessageWriter.Write(m3);
            DnsTsigOptions unsignedTsig = new(
                DnsName.Parse(keyName),
                algorithm.Name,
                (ulong)_fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
                300,
                [],
                request.Id,
                DnsResponseCodeEnum.NoError,
                []
            );
            byte[] mac = algorithm.ComputeMac(
                secret,
                TsigCodec.BuildDigestInput(
                    TsigDigestKindEnum.Continuation,
                    TsigCodec.ApplyOriginalId(DnsMessageWriter.Write(m4), request.Id),
                    unsignedTsig,
                    m2.Tsig.Mac,
                    [m3Bytes]
                )
            );
            m4.Tsig = new DnsTsigOptions(
                unsignedTsig.KeyName,
                unsignedTsig.AlgorithmName,
                unsignedTsig.TimeSigned,
                unsignedTsig.Fudge,
                mac,
                unsignedTsig.OriginalId,
                unsignedTsig.Error,
                []
            );

            return [m1, m2, m3, m4];
        }

        public void Dispose()
        {
            _cts.Cancel();
            _tcp.Stop();
            _udp.Dispose();
        }
    }

    /// <summary>
    ///     This method runs a tool and returns its exit code and combined output. A tool that does
    ///     not finish in 30 seconds is killed and the test fails.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunAsync(
        string tool,
        string[] arguments,
        string standardInput = null
    )
    {
        ProcessStartInfo startInfo = new(tool)
        {
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = Process.Start(startInfo)!;

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            process.StandardInput.Close();
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{tool} did not finish in 30 seconds.");
        }

        return (process.ExitCode, await output + await error);
    }

    [Fact]
    public async Task Dig_SoaQuerySignedByBind_GetsASignedAnswerBindAccepts()
    {
        if (!HasTool("dig"))
            return;

        (string name, string secret) = await NewKeyAsync();
        using Responder responder = new(fixture);

        (int exitCode, string output) = await RunAsync(
            "dig",
            [
                "-y",
                $"hmac-sha256:{name}:{secret}",
                "-p",
                responder.Port.ToString(),
                "@127.0.0.1",
                ZoneName,
                "SOA",
                "+norec",
                "+tries=1",
                "+time=5",
            ]
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("status: NOERROR", output);
        // dig prints the pseudosection for a signed answer and a "Couldn't verify signature" or
        // "TSIG error" line when it rejects the signature.
        Assert.Contains("TSIG PSEUDOSECTION", output);
        Assert.DoesNotContain("verify signature", output);
        Assert.DoesNotContain("TSIG error", output);
        Assert.Contains("ns1.interop.example.", output);
    }

    [Fact]
    public async Task Dig_AxfrOverFourMessagesWithOneUnsigned_PrintsEveryRecordAndNoError()
    {
        if (!HasTool("dig"))
            return;

        (string name, string secret) = await NewKeyAsync();
        using Responder responder = new(fixture);

        (int exitCode, string output) = await RunAsync(
            "dig",
            [
                "-y",
                $"hmac-sha256:{name}:{secret}",
                "-p",
                responder.Port.ToString(),
                "@127.0.0.1",
                ZoneName,
                "AXFR",
                "+tcp",
                "+tries=1",
                "+time=5",
            ]
        );

        Assert.Equal(0, exitCode);
        foreach (string host in AxfrHosts)
            Assert.Contains($"{host}.interop.example.", output);
        Assert.DoesNotContain("Transfer failed", output);
        Assert.DoesNotContain("tsig verify failure", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TSIG error", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nsupdate_SignedUpdate_GetsASignedNoErrorReplyAndExitsZero()
    {
        if (!HasTool("nsupdate"))
            return;

        (string name, string secret) = await NewKeyAsync();
        using Responder responder = new(fixture);

        (int exitCode, string output) = await RunAsync(
            "nsupdate",
            ["-y", $"hmac-sha256:{name}:{secret}", "-p", responder.Port.ToString()],
            $"server 127.0.0.1 {responder.Port}\nzone {ZoneName}\nupdate add new.{ZoneName} 300 A 192.0.2.9\nsend\n"
        );

        Assert.True(exitCode == 0, $"nsupdate exited {exitCode}: {output}");
    }

    [Fact]
    public async Task Dig_WithAWrongSecret_GetsNotAuth()
    {
        if (!HasTool("dig"))
            return;

        (string name, _) = await NewKeyAsync();
        using Responder responder = new(fixture);
        string wrongSecret = Convert.ToBase64String(new byte[32]);

        (int _, string output) = await RunAsync(
            "dig",
            [
                "-y",
                $"hmac-sha256:{name}:{wrongSecret}",
                "-p",
                responder.Port.ToString(),
                "@127.0.0.1",
                ZoneName,
                "SOA",
                "+norec",
                "+tries=1",
                "+time=5",
            ]
        );

        Assert.Contains("status: NOTAUTH", output);
    }
}
