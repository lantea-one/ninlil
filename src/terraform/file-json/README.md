# file-json

Serializes an input value as JSON and writes it to a local file.

## What it does

- Accepts arbitrary `data`.
- Writes JSON content via `jsonencode(...)` into `file`.
- Returns the absolute path of the written file.

## Variables

| Name | Type | Default | Required | Description |
|---|---|---|---|---|
| `data` | `any` | `{}` | No | Value serialized to JSON file contents. |
| `file` | `string` | `"environment.json"` | No | Destination file path for JSON output. |

## Outputs

| Name | Description |
|---|---|
| `filename` | Absolute path to the generated JSON file. |

## Terragrunt usage example

```hcl
terraform {
  source = "../src/terraform/file-json"
}

inputs = {
  file = "${get_repo_root()}/live/environment.json"
  data = {
    application = "Ninlil"
    environment = "dev"
    runtime     = "net10.0"
  }
}
```
