# Terraform modules

This directory contains reusable Terraform modules used by the repository for packaging, publishing, and local artifact generation.

## Module index

| Module                                         | Description                                                                |
| ---------------------------------------------- | -------------------------------------------------------------------------- |
| [`docker-image`](./docker-image/README.md)     | Build a Docker image and optionally push it to a registry.                 |
| [`dotnet-publish`](./dotnet-publish/README.md) | Restore, clean, and publish a .NET project/solution to a target directory. |
| [`file-json`](./file-json/README.md)           |  Serialize Terraform data to JSON and write it to a file.                  |
| [`nuget-config`](./nuget-config/README.md)     | Generate `nuget.config` and add package sources.                           |
| [`nuget-publish`](./nuget-publish/README.md)   |  Restore, pack, and optionally push NuGet packages.                        |

## Typical Terragrunt pattern

```hcl
terraform {
  source = "../src/terraform/<module-name>"
}

inputs = {
  # module-specific inputs
}
```
