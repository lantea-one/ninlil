using System.Runtime.Versioning;

// Ninlil.Web itself is decorated [SupportedOSPlatform("linux"/"macos"/"windows")] (it
// calls into Server.Forwarding's and Protocol.DnsOverQuic's own DI extensions, which require
// it) -- matching that same set here is what a consuming assembly needs to acknowledge it only
// targets those three platforms too.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]
