using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class implements <see cref="IDnsQueryResolver" /> for recursive queries: declining
///     entirely (<see cref="DnsQueryResolution.NotHandled" />) unless recursion is both enabled
///     by configuration and desired by the client, so <see cref="IDnsQueryDispatcher" /> only
///     ever reaches this resolver for a name this server is not itself authoritative for -- an
///     authoritative answer always wins regardless of the RD bit.
///     <para>
///         This resolver is registered as a singleton, the same lifetime every
///         <see cref="IDnsQueryResolver" /> in the dispatch chain shares, and reads
///         <see cref="RecursiveServerOptions" /> via <see cref="IOptionsMonitor{TOptions}" />
///         rather than the usual, snapshotted <see cref="IOptions{TOptions}" /> every other
///         pre-<c>AppearanceOptions</c> runtime settings class in this project still uses --
///         <see cref="IOptions{TOptions}" />'s own <c>Value</c> is computed once and cached for
///         the life of the process, so a singleton capturing it would never see a write
///         <c>RecursiveSettingsController.Patch</c> merges into the configuration file. Reading
///         <c>options.CurrentValue</c> fresh on every query is what makes toggling
///         <see cref="RecursiveServerOptions.EnableRecursion" /> (and every other field this
///         resolver reads) take effect on this same running instance immediately, with no
///         restart.
///     </para>
/// </summary>
/// <param name="engine">The engine this resolver actually resolves through.</param>
/// <param name="validator">The DNSSEC chain-of-trust validator this resolver checks every answer against -- this is a full validating resolver, not merely an iterative one.</param>
/// <param name="options">This resolver's own recursion/validation toggles, read live -- see this class's own doc comment.</param>
/// <param name="accessControlEngine">
///     The real per-client/network access-control engine this resolver consults so a
///     client matching an enabled rule can be granted recursion even while
///     <see cref="RecursiveServerOptions.EnableRecursion" /> is globally off. Defaults to
///     <see langword="null" /> -- the same optional-cross-cutting-dependency idiom
///     <c>DnsQueryProcessingPipeline</c> already establishes for its own filter/rate-limit/rewrite
///     engines -- so every existing caller that constructs this resolver directly keeps compiling
///     unchanged; a <see langword="null" /> engine behaves as if no rule ever grants anything,
///     never as an error.
/// </param>
/// <param name="clientSubnetService">
///     The RFC 7871 EDNS Client Subnet builder this resolver consults to attach an ECS option to
///     every upstream query it issues on this client's behalf. Defaults to <see langword="null" />
///     (the same optional-cross-cutting-dependency idiom <paramref name="accessControlEngine" />
///     already establishes), in which case no ECS option is ever attached -- functionally
///     identical to <see cref="EdnsClientSubnetOptions.Enable" /> being off.
/// </param>
/// <param name="dns64Service">
///     The RFC 6147 DNS64 synthesis service this resolver consults whenever an AAAA query comes
///     back with <see cref="RecursiveResolutionStatusEnum.NoData" /> (the name exists, but has no
///     AAAA of its own) -- a second, explicit A lookup for the same name is issued only in that
///     one case, and only when <see cref="Dns64Options.Enable" /> is on, so this never adds a
///     second upstream round trip to any query DNS64 could not possibly apply to. Defaults to
///     <see langword="null" /> (the same optional-cross-cutting-dependency idiom
///     <paramref name="clientSubnetService" /> already establishes), in which case no synthesis is
///     ever attempted -- functionally identical to <see cref="Dns64Options.Enable" /> being off.
/// </param>
public class RecursiveQueryResolver(
    IIterativeResolutionEngine engine,
    IDnssecValidator validator,
    IOptionsMonitor<RecursiveServerOptions> options,
    IDnsAccessControlEngine accessControlEngine = null,
    EdnsClientSubnetService clientSubnetService = null,
    Dns64SynthesisService dns64Service = null
) : IDnsQueryResolver
{
    /// <inheritdoc />
    public async ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        // This resolver is registered as a singleton (the same lifetime every IDnsQueryResolver
        // in this project's dispatch chain shares) -- reading options.CurrentValue fresh on
        // every query, rather than capturing a snapshot at construction time, is what actually
        // makes a live RecursiveSettingsController.Patch write (in particular, flipping
        // EnableRecursion on) take effect on this same running instance with no restart.
        RecursiveServerOptions currentOptions = options.CurrentValue;

        // A client whose own source address matches an enabled access-control rule
        // granting RecursionAccess is treated as if EnableRecursion were true for them
        // specifically, even while it is globally off -- accessControlEngine defaults to null
        // (see this class's own constructor doc comment), in which case this check is skipped
        // entirely and only the global toggle governs, exactly as before this capability existed.
        bool recursionPermitted =
            currentOptions.EnableRecursion
            || (
                accessControlEngine is not null
                && await accessControlEngine.HasCapabilityAsync(
                    context.RemoteAddress,
                    DnsAccessControlCapabilityEnum.RecursionAccess,
                    stoppingToken
                )
            );

        if (!recursionPermitted || !request.RecursionDesired || request.Questions.Count == 0)
            return DnsQueryResolution.NotHandled;

        DnsQuestion question = request.Questions[0];

        if (question.Class != DnsClassEnum.Internet)
            return DnsQueryResolution.NotHandled;

        bool clientRequestedDnssec = request.Edns?.DnssecOk == true;

        // DNSSEC content is requested from upstream whenever
        // validation is enabled at all, regardless of whether the client's own query set DO --
        // validating the answer needs the RRSIGs either way; whether to also *show* them to the
        // client is a separate question, decided below.
        //
        // The ECS option this resolution's own outbound queries carry (if this feature is
        // enabled at all -- off by default, see EdnsClientSubnetOptions.Enable) is built once,
        // here, from this exact query's own client address and request, then threaded through
        // every upstream query the resolution issues for the client's real question (including a
        // followed CNAME's own target) -- never into a nested name-server-address lookup, which
        // is internal plumbing rather than an answer to what the client actually asked.
        DnsEdnsOption clientSubnetOption = clientSubnetService?.BuildOutboundOption(
            context.RemoteAddress,
            request
        );

        QueryBudget budget = new(currentOptions);
        IterativeResolutionResult result = await engine.ResolveAsync(
            question.Name,
            question.Type,
            question.Class,
            currentOptions.EnableDnssecValidation,
            budget,
            forceRootStart: false,
            clientSubnetOption,
            stoppingToken
        );

        // RFC 4035 §3.2.2/§4.6: a resolver always validates whether or not the client set CD,
        // so a later query for the same, now-cached name can still be judged correctly -- CD
        // only controls whether a Bogus result is enforced (SERVFAIL) or the client is simply
        // trusted to have wanted the unauthenticated data anyway.
        DnssecValidationResult validation =
            currentOptions.EnableDnssecValidation
            && result.Status
                is RecursiveResolutionStatusEnum.Success
                    or RecursiveResolutionStatusEnum.NxDomain
                    or RecursiveResolutionStatusEnum.NoData
                ? await validator.ValidateAsync(
                    question.Name,
                    question.Type,
                    question.Class,
                    result,
                    budget,
                    stoppingToken
                )
                : DnssecValidationResult.Indeterminate("DNSSEC validation is disabled.");

        bool isBogus = validation.State == DnssecValidationStateEnum.Bogus;
        bool enforceBogus = isBogus && !request.CheckingDisabled;

        // RFC 6147 DNS64: only ever attempted for an AAAA query that came back NoData (the name
        // exists but has no AAAA of its own) -- never for NXDOMAIN, ServFail, or a
        // Bogus-and-enforced result, since none of those means "this name might still have a
        // usable A record worth synthesizing from". dns64Service defaults to null (see this
        // class's own constructor doc comment), in which case this is skipped entirely, exactly
        // as if Dns64Options.Enable were off.
        IReadOnlyList<DnsResourceRecord> synthesizedAaaa = [];

        if (
            !enforceBogus
            && dns64Service is { IsEnabled: true }
            && question.Type == DnsResourceRecordTypeEnum.Aaaa
            && result.Status == RecursiveResolutionStatusEnum.NoData
        )
        {
            IterativeResolutionResult aResult = await engine.ResolveAsync(
                question.Name,
                DnsResourceRecordTypeEnum.A,
                question.Class,
                currentOptions.EnableDnssecValidation,
                budget,
                forceRootStart: false,
                clientSubnetOption,
                stoppingToken
            );

            if (aResult.Status == RecursiveResolutionStatusEnum.Success)
                synthesizedAaaa = dns64Service.SynthesizeAaaaRecords(aResult.Answer);
        }

        bool didSynthesizeDns64Answer = synthesizedAaaa.Count > 0;

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            RecursionAvailable = true,
            CheckingDisabled = request.CheckingDisabled,
            // Echoed back so a validating/cookie-aware client can correlate this response with
            // the query it sent -- see AuthoritativeQueryResolver.CreateAnswerHeader's own doc
            // comment for the fuller reasoning; this resolver needed the identical fix.
            Questions = [.. request.Questions],
            // A synthesized DNS64 answer is never presented as DNSSEC-authenticated, regardless
            // of what the original (NoData) AAAA validation state was -- see
            // Dns64SynthesisService's own doc comment for the full RFC 6147 §3 / RFC 6840 §5.1
            // reasoning. The response being sent is no longer the validated-or-not NoData answer
            // that validation.State actually describes; it is a fabricated record that was never
            // signed by anyone.
            AuthenticData =
                !didSynthesizeDns64Answer && validation.State == DnssecValidationStateEnum.Secure,
            ResponseCode = enforceBogus
                ? DnsResponseCodeEnum.ServerFailure
                : result.Status switch
                {
                    RecursiveResolutionStatusEnum.Success => DnsResponseCodeEnum.NoError,
                    RecursiveResolutionStatusEnum.NxDomain => DnsResponseCodeEnum.NxDomain,
                    RecursiveResolutionStatusEnum.NoData => DnsResponseCodeEnum.NoError,
                    _ => DnsResponseCodeEnum.ServerFailure,
                },
        };

        if (request.Edns is not null)
            response.Edns = new DnsEdnsOptions(
                0,
                DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                clientRequestedDnssec,
                []
            );

        if (!enforceBogus && result.Status == RecursiveResolutionStatusEnum.Success)
        {
            response.Answers.AddRange(result.Answer);

            if (clientRequestedDnssec)
            {
                // Every CNAME hop's own RRSIG (per-hop, now that each is independently validated
                // -- see DnssecValidator) is shown to the client alongside the terminal answer's
                // own signature, in chain order -- a downstream validating stub resolver needs
                // every hop's own signature to re-verify this chain itself, not merely the final
                // one.
                foreach (CnameChainHop hop in result.CnameChain)
                    response.Answers.AddRange(hop.DnssecRecords);

                response.Answers.AddRange(result.DnssecRecords);
            }
        }
        else if (didSynthesizeDns64Answer)
        {
            // No RRSIGs ever accompany a synthesized answer, requested or not -- there is no real
            // signature over a record this resolver itself just invented.
            response.Answers.AddRange(synthesizedAaaa);
        }
        else if (
            !enforceBogus
            && result.Status
                is RecursiveResolutionStatusEnum.NxDomain
                    or RecursiveResolutionStatusEnum.NoData
            && result.Soa is not null
        )
        {
            response.AuthorityRecords.Add(result.Soa);

            if (clientRequestedDnssec)
                response.AuthorityRecords.AddRange(result.DnssecRecords);
        }

        return DnsQueryResolution.Handled(response);
    }
}
