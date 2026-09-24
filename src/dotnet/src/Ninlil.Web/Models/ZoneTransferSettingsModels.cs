// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ZoneTransferOptions</c> -- how this server chunks AXFR/IXFR responses it serves out to a
///     secondary. This record's own fields are the exact same ones a <c>PATCH</c> to the same
///     endpoint accepts.
/// </summary>
public record ZoneTransferSettingsDetail(int RecordsPerMessage);
