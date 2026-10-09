using System.Runtime.Versioning;

// Ninlil.Web's health checks carry [SupportedOSPlatform] for linux, macos and windows. This
// assembly declares the same set to acknowledge that it targets only those platforms, as
// Ninlil.Web does for its own callers.
[assembly: SupportedOSPlatform("linux")]
[assembly: SupportedOSPlatform("macos")]
[assembly: SupportedOSPlatform("windows")]
