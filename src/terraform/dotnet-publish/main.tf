/**
 * Publishes a .NET project or solution into var.output with the dotnet CLI. Chained null_resource
 * local-exec steps empty the output directory (PowerShell on Windows, bash elsewhere), then run
 * restore, clean and publish, all again whenever var.trigger changes. It has no outputs.
 * Nothing in this repository's pipeline uses this module.
 */
terraform {
  required_version = ">= 1.16.0"
}
