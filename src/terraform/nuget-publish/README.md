# nuget-publish

Builds, packs, and optionally publishes `.nupkg` files to a NuGet server using `dotnet` CLI commands executed from Terraform `null_resource` resources.

## What it does

- Resolves project and config paths from `workspace` + optional overrides.
- Extracts package version from the project file.
- Runs `dotnet restore`, `dotnet clean`, and `dotnet pack`.
- Optionally runs `dotnet nuget push` when `publish = true`.
- Supports symbol/source/serviceable packaging flags.

## Variables

| Name | Type | Default | Required | Description |
|---|---|---|---|---|
| `config-file` | `string` | `null` | No | Absolute path to `nuget.config`; defaults to `<workspace>/nuget.config`. |
| `include-source` | `bool` | `false` | No | Include source files in package. |
| `include-symbols` | `bool` | `false` | No | Include symbol package output. |
| `serviceable` | `bool` | `false` | No | Mark package as serviceable. |
| `package-name` | `string` | `null` | No | Package name override (declared for module input). |
| `package-version` | `string` | `null` | No | Package version override for `dotnet pack`. |
| `project` | `string` | n/a | Yes | Project identifier/path to package. |
| `publish` | `bool` | `true` | No | Whether to push packages to remote server. |
| `publish-directory` | `string` | `null` | No | Artifact output directory. |
| `server` | `string` | n/a | Yes | NuGet server/source name or URL. |
| `server-api-key` | `string` | `null` | No | API key used for package push. |
| `server-authenticate` | `bool` | `true` | No | Declared auth toggle for server usage. |
| `workspace` | `string` | n/a | Yes | Absolute project workspace path. |

## Outputs

This module currently defines no Terraform outputs.

## Terragrunt usage example

```hcl
terraform {
  source = "../src/terraform/nuget-publish"
}

inputs = {
  workspace = get_repo_root()
  project   = "src/dotnet/Ninlil.Common/Ninlil.Common.csproj"
  server    = "https://api.nuget.org/v3/index.json"

  "publish-directory" = "${get_repo_root()}/live/nuget"
  "server-api-key"    = get_env("NUGET_API_KEY", "")
  "package-version"   = "0.1.0"
  "include-symbols"   = true
  "include-source"    = true
  publish              = true
}
```
