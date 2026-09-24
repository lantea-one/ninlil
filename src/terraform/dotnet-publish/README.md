# dotnet-publish

Publishes a .NET project or solution to a target output directory using chained `null_resource` local-exec commands.

## What it does

- Bootstraps/cleans the publish output directory (PowerShell on Windows, Bash on Unix).
- Runs `dotnet restore` (optionally with a custom NuGet config).
- Runs `dotnet clean`.
- Runs `dotnet publish` with runtime/configuration/self-contained settings.
- Re-runs on each apply unless `trigger` is overridden.

## Variables

| Name | Type | Default | Required | Description |
|---|---|---|---|---|
| `configuration` | `string` | `"Release"` | No | Build configuration used by clean/publish. |
| `nuget-config-file` | `string` | `null` | No | Optional path passed to `dotnet restore --configfile`. |
| `output` | `string` | n/a | Yes | Output directory for publish artifacts. |
| `project` | `string` | n/a | Yes | Project (`.csproj`) or solution path to publish. |
| `runtime` | `string` | `"linux-arm64"` | No | Runtime identifier for restore/clean/publish. |
| `self-contained` | `bool` | `false` | No | Whether publish should be self-contained. |
| `trigger` | `string` | `timestamp()` | No | Forced re-run trigger for resources. |
| `verbosity` | `string` | `"minimal"` | No | Verbosity for dotnet commands. |

## Outputs

This module currently defines no Terraform outputs.

## Terragrunt usage example

```hcl
terraform {
  source = "../src/terraform/dotnet-publish"
}

inputs = {
  project       = "${get_repo_root()}/Ninlil.slnx"
  output        = "${get_repo_root()}/live/publish/technitium-console"
  configuration = "Release"
  runtime       = "linux-x64"
  verbosity     = "minimal"

  # Hyphenated variable names are quoted in HCL maps.
  "self-contained"   = false
  "nuget-config-file" = "${get_repo_root()}/nuget.config"
}
```
