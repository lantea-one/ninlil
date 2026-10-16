using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Acme.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests how <see cref="AcmeCertificateIssuer" /> registers and maintains the ACME
///     account's contact addresses (RFC 8555 §7.3 and §7.3.2). Certes talks to a
///     <see cref="FakeCaHandler" /> through the issuer's internal constructor, so the real
///     Certes request signing runs and the test reads the JWS payloads the issuer sent. No
///     network is used.
/// </summary>
public sealed class AcmeAccountRegistrationTests
{
    private static readonly Uri DirectoryUri = new("https://ca.test/directory");

    /// <summary>
    ///     This method builds a fresh Certes account key as PEM, to stand in for a stored one.
    /// </summary>
    private static string NewAccountKeyPem() =>
        Certes.KeyFactory.NewKey(Certes.KeyAlgorithm.ES256).ToPem();

    /// <summary>
    ///     This method builds an issuer that sends through <paramref name="ca" />. The responder
    ///     and CAA service are null because <c>CreateContextAsync</c> never touches them.
    /// </summary>
    private static AcmeCertificateIssuer CreateIssuer(
        FakeCaHandler ca,
        InMemoryAccountStore store,
        RecordingLogger logger,
        params string[] contactEmails
    ) =>
        new(
            store,
            null,
            null,
            new StaticOptionsMonitor<AcmeOptions>(
                new AcmeOptions { DirectoryUri = DirectoryUri, ContactEmails = [.. contactEmails] }
            ),
            logger,
            ca
        );

    [Fact]
    public async Task NewAccount_WithNoContactEmails_SendsNoAdminAtLocalhostAndNoContact()
    {
        FakeCaHandler ca = new();
        InMemoryAccountStore store = new();

        await CreateIssuer(ca, store, new RecordingLogger()).CreateContextAsync(default);

        JsonElement payload = Assert.Single(ca.NewAccountPayloads);
        Assert.DoesNotContain("admin@localhost", payload.GetRawText());

        // RFC 8555 §7.3: "contact" is optional, and Certes omits it for no addresses.
        Assert.False(payload.TryGetProperty("contact", out _));
        Assert.True(payload.GetProperty("termsOfServiceAgreed").GetBoolean());
        Assert.NotNull(store.AccountKeyPem);
    }

    [Fact]
    public async Task NewAccount_WithSeveralEmails_RegistersEachOnceInOrder()
    {
        FakeCaHandler ca = new();

        await CreateIssuer(
                ca,
                new InMemoryAccountStore(),
                new RecordingLogger(),
                "a@example.org",
                " ",
                "b@example.org",
                "A@example.org"
            )
            .CreateContextAsync(default);

        JsonElement payload = Assert.Single(ca.NewAccountPayloads);
        Assert.Equal(
            ["mailto:a@example.org", "mailto:b@example.org"],
            payload.GetProperty("contact").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task ExistingAccount_WhoseContactsDiffer_IsUpdatedOnce()
    {
        FakeCaHandler ca = new() { AccountContacts = ["mailto:old@example.org"] };
        InMemoryAccountStore store = new() { AccountKeyPem = NewAccountKeyPem() };

        await CreateIssuer(ca, store, new RecordingLogger(), "new@example.org")
            .CreateContextAsync(default);

        JsonElement update = Assert.Single(ca.UpdatePayloads);
        Assert.Equal(
            ["mailto:new@example.org"],
            update.GetProperty("contact").EnumerateArray().Select(c => c.GetString())
        );
    }

    [Fact]
    public async Task ExistingAccount_WithTheSameContactSet_IsNotUpdated()
    {
        FakeCaHandler ca = new()
        {
            AccountContacts = ["mailto:B@example.org", "mailto:a@example.org"],
        };
        InMemoryAccountStore store = new() { AccountKeyPem = NewAccountKeyPem() };

        await CreateIssuer(ca, store, new RecordingLogger(), "a@example.org", "b@example.org")
            .CreateContextAsync(default);

        Assert.Empty(ca.UpdatePayloads);

        // Only the lookup of the existing account (onlyReturnExisting) was sent, never a create.
        Assert.DoesNotContain(
            ca.NewAccountPayloads,
            p => !p.TryGetProperty("onlyReturnExisting", out _)
        );
    }

    [Fact]
    public async Task ExistingAccount_WhenTheUpdateFails_StillReturnsTheContextAndLogsAWarning()
    {
        FakeCaHandler ca = new()
        {
            AccountContacts = ["mailto:old@example.org"],
            UpdateStatusCode = HttpStatusCode.InternalServerError,
        };
        InMemoryAccountStore store = new() { AccountKeyPem = NewAccountKeyPem() };
        RecordingLogger logger = new();

        Certes.AcmeContext context = await CreateIssuer(ca, store, logger, "new@example.org")
            .CreateContextAsync(default);

        Assert.NotNull(context);
        Assert.Single(ca.UpdatePayloads);
        Assert.Single(logger.Entries, entry => entry == LogLevel.Warning);
    }

    [Fact]
    public void BuildContactUris_DropsBlanksAndDuplicatesAndKeepsOrder()
    {
        Assert.Equal(
            ["mailto:z@example.org", "mailto:a@example.org"],
            AcmeCertificateIssuer.BuildContactUris([
                "z@example.org",
                "",
                "  ",
                null,
                " a@example.org ",
                "Z@EXAMPLE.org",
            ])
        );
        Assert.Empty(AcmeCertificateIssuer.BuildContactUris([]));
        Assert.Empty(AcmeCertificateIssuer.BuildContactUris(null));
    }

    /// <summary>
    ///     This class is a minimal in-memory ACME CA for <see cref="AcmeCertificateIssuer" />. It
    ///     serves the directory, <c>newNonce</c>, <c>newAccount</c> (a create, or a lookup when the
    ///     payload has <c>onlyReturnExisting</c>), the account POST-as-GET and the account update,
    ///     and records each decoded JWS payload. Every response carries a fresh
    ///     <c>Replay-Nonce</c>, as RFC 8555 §6.5 requires.
    /// </summary>
    private sealed class FakeCaHandler : HttpMessageHandler
    {
        private int _nonce;

        /// <summary>The contacts the CA reports for the existing account.</summary>
        public List<string> AccountContacts { get; set; } = [];

        /// <summary>The status the CA answers an account update with.</summary>
        public HttpStatusCode UpdateStatusCode { get; set; } = HttpStatusCode.OK;

        /// <summary>The payloads of the <c>newAccount</c> requests that create an account or look one up.</summary>
        public List<JsonElement> NewAccountPayloads { get; } = [];

        /// <summary>The payloads of the account updates (a POST with a non-empty payload).</summary>
        public List<JsonElement> UpdatePayloads { get; } = [];

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri.AbsolutePath;
            HttpResponseMessage response;

            if (path == "/directory")
            {
                response = Json(
                    HttpStatusCode.OK,
                    """
                    {"newNonce":"https://ca.test/nonce","newAccount":"https://ca.test/new-account",
                     "newOrder":"https://ca.test/new-order","revokeCert":"https://ca.test/revoke",
                     "keyChange":"https://ca.test/key-change"}
                    """
                );
            }
            else if (path == "/nonce")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK);
            }
            else
            {
                JsonElement? payload = await DecodePayloadAsync(request, cancellationToken);

                if (path == "/new-account")
                {
                    NewAccountPayloads.Add(payload.Value);
                    bool lookup = payload.Value.TryGetProperty("onlyReturnExisting", out _);
                    response = Json(
                        lookup ? HttpStatusCode.OK : HttpStatusCode.Created,
                        AccountJson()
                    );
                    response.Headers.Location = new Uri("https://ca.test/acct/1");
                }
                else if (path == "/acct/1")
                {
                    if (payload is null)
                    {
                        // An empty payload is a POST-as-GET (RFC 8555 §6.3).
                        response = Json(HttpStatusCode.OK, AccountJson());
                    }
                    else
                    {
                        UpdatePayloads.Add(payload.Value);
                        response =
                            UpdateStatusCode == HttpStatusCode.OK
                                ? Json(HttpStatusCode.OK, AccountJson())
                                : Json(
                                    UpdateStatusCode,
                                    """{"type":"urn:ietf:params:acme:error:serverInternal","detail":"fake failure"}"""
                                );
                    }
                }
                else
                {
                    response = new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }

            response.Headers.Add("Replay-Nonce", $"nonce-{Interlocked.Increment(ref _nonce)}");
            return response;
        }

        /// <summary>
        ///     This method returns the account object the CA reports, with its current contacts.
        /// </summary>
        private string AccountJson() =>
            JsonSerializer.Serialize(new { status = "valid", contact = AccountContacts });

        /// <summary>
        ///     This method builds a JSON response with the ACME media type Certes accepts.
        /// </summary>
        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status)
            {
                Content = new StringContent(
                    body,
                    Encoding.UTF8,
                    MediaTypeHeaderValue.Parse("application/json")
                ),
            };

        /// <summary>
        ///     This method decodes the base64url "payload" of a JWS request body. It returns
        ///     <see langword="null" /> for an empty payload (a POST-as-GET).
        /// </summary>
        private static async Task<JsonElement?> DecodePayloadAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = await request.Content.ReadAsStringAsync(cancellationToken);
            string encoded = JsonDocument
                .Parse(body)
                .RootElement.GetProperty("payload")
                .GetString();

            if (string.IsNullOrEmpty(encoded))
                return null;

            string padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return JsonDocument.Parse(Convert.FromBase64String(padded)).RootElement.Clone();
        }
    }

    /// <summary>
    ///     This class is an <see cref="IAcmeAccountStore" /> that keeps the account key in memory.
    ///     The registration tests only read and write the key.
    /// </summary>
    private sealed class InMemoryAccountStore : IAcmeAccountStore
    {
        /// <inheritdoc />
        public string AccountKeyPem { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates { get; } =
            new Dictionary<string, AcmeDomainCertificateState>();

        /// <inheritdoc />
        public IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures { get; } =
            new Dictionary<string, AcmeDomainRenewalFailure>();

        /// <inheritdoc />
        public void SaveAccountKey(string accountKeyPem) => AccountKeyPem = accountKeyPem;

        /// <inheritdoc />
        public void SaveDomainState(string domain, AcmeDomainCertificateState state) { }

        /// <inheritdoc />
        public void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure) { }

        /// <inheritdoc />
        public void ClearDomainFailure(string domain) { }
    }

    /// <summary>
    ///     This class is an <see cref="ILogger{TCategoryName}" /> that records the level of every
    ///     entry, so a test can assert that a Warning was logged.
    /// </summary>
    private sealed class RecordingLogger : ILogger<AcmeCertificateIssuer>
    {
        /// <summary>The level of every entry logged, in order.</summary>
        public List<LogLevel> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) =>
            NullLogger.Instance.BeginScope(state);

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add(logLevel);
    }
}
