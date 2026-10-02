/**
 * Writes a nuget.config into var.workspace from a template (nuget.config.tfpl by default, which
 * has no sources), then adds NuGet.org and each entry of var.sources with `dotnet nuget add
 * source`. A source that authenticates has its password stored in clear text in the file. Its
 * outputs are the file's path and its contents (marked sensitive).
 * Nothing in this repository's pipeline uses this module.
 */
terraform {
  required_version = ">= 1.16.0"
}
