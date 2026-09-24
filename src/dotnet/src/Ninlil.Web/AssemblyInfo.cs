using System.Runtime.Versioning;

// This host calls into Server.Forwarding's and Protocol.DnsOverQuic's own DI extensions, both of
// which are decorated [SupportedOSPlatform("linux"/"macos"/"windows")] -- matching that same set
// here is what a calling assembly needs to acknowledge it only targets those three platforms,
// the same convention this repo's own test projects already use for calling the same APIs (see,
// for example, Ninlil.Dns.Server.Forwarding.Tests.ForwarderQueryResolverTests).
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]
