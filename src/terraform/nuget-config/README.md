# nuget-config

Generates a `nuget.config` file in a workspace and adds package sources via `dotnet nuget add source`.

## What it does

- Writes a base `nuget.config` file to `<workspace>/nuget.config`.
- Uses either:
  - a user-provided `template-file`, or
  - module default `nuget.config.tfpl`.
- Adds the default NuGet.org source.
- Iterates `sources` and adds custom feeds, with optional credentials.

## Variables

| Name | Type | Default | Required | Description |
|---|---|---|---|---|
| `sources` | `list(object({ authenticate=optional(bool,false), name=string, password=optional(string,null), url=string, username=optional(string,null) }))` | `[]` | No | Additional NuGet sources to add. |
| `template-file` | `string` | `null` | No | Optional base XML template path for `nuget.config`. |
| `workspace` | `string` | n/a | Yes | Absolute path where `nuget.config` is written. |

## Outputs

| Name | Description |
|---|---|
| `nuget-config` | Contents of generated `nuget.config` (sensitive). |
| `nuget-config-file` | Absolute path to generated `nuget.config`. |

## Terragrunt usage example

```hcl
terraform {
  source = "../src/terraform/nuget-config"
}

inputs = {
  workspace = get_repo_root()
  sources = [
    {
      name         = "GitHubPackages"
      url          = "https://nuget.pkg.github.com/fabigail/index.json"
      authenticate = true
      username     = get_env("GITHUB_ACTOR", "")
      password     = get_env("GITHUB_TOKEN", "")
    }
  ]
}
```
