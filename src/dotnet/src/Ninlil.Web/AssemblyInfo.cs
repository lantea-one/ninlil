using System.Runtime.Versioning;

// The DI extensions of Server.Forwarding and Protocol.DnsOverQuic are marked
// [SupportedOSPlatform] for linux, macos and windows. A calling assembly declares the same set
// to acknowledge that it targets only those platforms, as the test projects do (for example
// Ninlil.Dns.Server.Forwarding.Tests.ForwarderQueryResolverTests).
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]
