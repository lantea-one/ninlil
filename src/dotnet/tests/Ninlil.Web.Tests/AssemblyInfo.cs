using System.Runtime.Versioning;

// Ninlil.Web is marked [SupportedOSPlatform] for Linux, macOS and Windows, because the DI
// extensions it calls in Server.Forwarding and Protocol.DnsOverQuic require it. The test
// assembly declares the same three platforms so the platform analyzer accepts its calls.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]
