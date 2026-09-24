// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This record holds the HTTP cache validators one registry file was served with, so the next
///     refresh can ask the IEEE server for it conditionally and skip the download when unchanged.
/// </summary>
/// <param name="ETag">The response's <c>ETag</c> header value, or <see langword="null" />.</param>
/// <param name="LastModified">The response's <c>Last-Modified</c> header value, or <see langword="null" />.</param>
public sealed record MacVendorRegistryValidator(string ETag, string LastModified);
