using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class is the recursive step of query resolution, the last
///     <see cref="IDnsQueryResolver" /> <see cref="IDnsQueryDispatcher" /> tries, after the
///     authoritative and forwarding resolvers (see the dispatcher and
///     <c>DnsQueryProcessingPipeline</c> for the whole query path). It declines
///     (<see cref="DnsQueryResolution.NotHandled" />) unless recursion is allowed for the client,
///     the client set RD, and the question is class IN. Otherwise it resolves through
///     <see cref="IIterativeResolutionEngine" />, validates with <see cref="IDnssecValidator" />,
///     applies DNS64 when configured, and builds the response.
///     <para>
///         It is a singleton and reads <see cref="RecursiveServerOptions" /> through
///         <see cref="IOptionsMonitor{TOptions}" /> on every query, because
///         <see cref="IOptions{TOptions}" /> is computed once per process and would never see a
///         change written by <c>RecursiveSettingsController.Patch</c>.
///     </para>
/// </summary>
/// <param name="engine">The engine this resolver resolves through.</param>
/// <param name="validator">The DNSSEC validator this resolver checks answers with, when validation is on.</param>
/// <param name="options">The recursion and validation settings, read live (see the class summary).</param>
/// <param name="accessControlEngine">
///     The access-control engine, which can grant recursion to a client while
///     <see cref="RecursiveServerOptions.EnableRecursion" /> is off. <see langword="null" /> (the
///     default) grants nothing.
/// </param>
/// <param name="clientSubnetService">
///     The RFC 7871 EDNS Client Subnet service that builds the ECS option for the upstream
///     queries answering this client's question. <see langword="null" /> (the default) attaches
///     none, the same as <see cref="EdnsClientSubnetOptions.Enable" /> being off.
/// </param>
/// <param name="dns64Service">
///     The RFC 6147 DNS64 synthesis service. When it is enabled and an AAAA query comes back
///     <see cref="RecursiveResolutionStatusEnum.NoData" />, the resolver looks up A for the same
///     name and synthesizes AAAA records from it; no other query pays for the extra lookup. With
///     validation on, the A answer is validated first (RFC 6147 §5.5): a Bogus one is not used
///     (SERVFAIL, or NODATA when the client set CD), and a query with both DO and CD set is never
///     synthesized for, because that client wants the DNSSEC data and a synthesized answer has
///     none. <see langword="null" /> (the default) disables DNS64, the same as
///     <see cref="Dns64Options.Enable" /> being off.
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

        // Read once per query, so a settings change applies to the next query (see the class
        // summary).
        RecursiveServerOptions currentOptions = options.CurrentValue;

        // An access-control rule granting RecursionAccess to the client's address allows
        // recursion for that client while EnableRecursion is off.
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

        // DNSSEC records are requested upstream whenever validation is on, whatever the client's
        // DO bit, because validation needs them; whether the client sees them is decided below.
        //
        // The ECS option is built once from the client's address and passed to every upstream
        // query for the client's question (including CNAME targets), but not to name server
        // address lookups, which are not the client's question.
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

        // RFC 4035 §3.2.2 and §4.6: validate whether or not the client set CD. CD only decides
        // whether a Bogus result becomes SERVFAIL or is returned to the client.
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

        // RFC 6147 DNS64: only for an AAAA query that came back NoData, never for NXDOMAIN,
        // SERVFAIL or an enforced Bogus result, since only NoData means the name may have an A
        // record to synthesize from. RFC 6147 §5.5: a client that set DO and CD wants the
        // DNSSEC data itself, so nothing is synthesized for it.
        IReadOnlyList<DnsResourceRecord> synthesizedAaaa = [];
        bool dns64SourceBogus = false;

        if (
            !enforceBogus
            && dns64Service is { IsEnabled: true }
            && question.Type == DnsResourceRecordTypeEnum.Aaaa
            && result.Status == RecursiveResolutionStatusEnum.NoData
            && !(clientRequestedDnssec && request.CheckingDisabled)
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
            {
                // RFC 6147 §5.5: do not synthesize from an A answer that failed validation.
                // Secure, Insecure and Indeterminate answers are used.
                DnssecValidationResult aValidation = currentOptions.EnableDnssecValidation
                    ? await validator.ValidateAsync(
                        question.Name,
                        DnsResourceRecordTypeEnum.A,
                        question.Class,
                        aResult,
                        budget,
                        stoppingToken
                    )
                    : DnssecValidationResult.Indeterminate("DNSSEC validation is disabled.");
                dns64SourceBogus = aValidation.State == DnssecValidationStateEnum.Bogus;

                if (!dns64SourceBogus)
                    synthesizedAaaa = dns64Service.SynthesizeAaaaRecords(aResult.Answer);
            }
        }

        // A Bogus A answer is SERVFAIL for a client that did not set CD; a CD client gets the
        // plain NODATA of the AAAA lookup.
        bool dns64Servfail = dns64SourceBogus && !request.CheckingDisabled;
        enforceBogus |= dns64Servfail;

        bool didSynthesizeDns64Answer = synthesizedAaaa.Count > 0;

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            RecursionAvailable = true,
            CheckingDisabled = request.CheckingDisabled,
            // Echoed back so the client can match this response to its query (as
            // AuthoritativeQueryResolver.CreateAnswerHeader does).
            Questions = [.. request.Questions],
            // RFC 6840 §5.8: AD is set only for a Secure answer to a client that set DO or AD,
            // so a client that never asked for DNSSEC never sees it. A synthesized DNS64 answer
            // is never marked authenticated, whatever the NoData answer's validation state: the
            // records were made here and signed by no one (see Dns64SynthesisService).
            AuthenticData =
                !enforceBogus
                && !didSynthesizeDns64Answer
                && validation.State == DnssecValidationStateEnum.Secure
                && (clientRequestedDnssec || request.AuthenticData),
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
                // Each CNAME hop's RRSIGs, in chain order, then the final answer's, so a
                // validating client can check the whole chain itself. The NSEC and NSEC3
                // records that prove a wildcard expansion belong to the authority section
                // (RFC 4035 §3.1.3.3), with their RRSIGs.
                foreach (CnameChainHop hop in result.CnameChain)
                    AddPositiveDnssecRecords(response, hop.DnssecRecords);

                AddPositiveDnssecRecords(response, result.DnssecRecords);
            }
        }
        else if (didSynthesizeDns64Answer)
        {
            // A synthesized answer has no RRSIGs; nothing signed it.
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

    /// <summary>
    ///     This method adds the DNSSEC records of a positive answer for a DO client: RRSIGs over
    ///     the answer go to the answer section; NSEC and NSEC3 records and the RRSIGs over them go
    ///     to the authority section.
    /// </summary>
    private static void AddPositiveDnssecRecords(
        DnsMessage response,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        foreach (DnsResourceRecord record in dnssecRecords)
            (IsDenialProof(record) ? response.AuthorityRecords : response.Answers).Add(record);
    }

    /// <summary>
    ///     This method tells whether a record is an NSEC or NSEC3 record, or an RRSIG covering
    ///     one.
    /// </summary>
    private static bool IsDenialProof(DnsResourceRecord record) =>
        record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
        || (
            record.Type == DnsResourceRecordTypeEnum.RrSig
            && DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                is RrSigRecordData
                {
                    TypeCovered: DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
                }
        );
}
