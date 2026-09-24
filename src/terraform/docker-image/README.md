# docker-image

Builds a Docker image using the `kreuzwerker/docker` provider and optionally pushes it to a registry.

## What it does

- Builds an image from `var.context` + `var.dockerfile`.
- Tags the image as `<registry.url>:<name>:<tag>` when `registry.url` is set, otherwise `<name>:<tag>`.
- Optionally pushes to a remote registry when `registry.address`, `registry.url`, and `registry.username`/`registry.password` are provided.
- Forces rebuilds through `trigger` (defaults to `timestamp()`).

## Variables

| Name | Type | Default | Required | Description |
|---|---|---|---|---|
| `build-arguments` | `map(string)` | `{}` | No | Docker build args passed to `docker build`. |
| `context` | `string` | `"docker"` | No | Docker build context directory. |
| `dockerfile` | `string` | `"Dockerfile"` | No | Dockerfile path (relative to context or absolute). |
| `host` | `string` | `"unix:///var/run/docker.sock"` | No | Docker daemon host for provider configuration. |
| `keep-remotely` | `bool` | `true` | No | Keep the pushed image in remote registry. |
| `name` | `string` | `null` | No | Image name component. |
| `no-cache` | `bool` | `true` | No | Disable Docker build cache. |
| `platform` | `string` | `"linux/amd64"` | No | Build platform (`linux/amd64` or `linux/arm64/v8`). |
| `registry` | `object({ address=string, password=string, url=string, username=string })` | `{ address = "", password = "", url = "", username = "" }` | No | Registry auth and URL metadata for push. |
| `sources` | `list(string)` | `[]` | No | Source images for cache warmup (`cache_from`). |
| `tag` | `string` | `"latest"` | No | Image tag. |
| `trigger` | `string` | `timestamp()` | No | Rebuild trigger value. |

## Outputs

| Name | Description |
|---|---|
| `name` | Built image name from `docker_image.build-image.name`. |
| `url` | Resolved image URL from module local `url`. |
| `url-with-digest` | Image URL with SHA256 digest when registry push is enabled. |

## Terragrunt usage example

```hcl
terraform {
  source = "../src/terraform/docker-image"
}

inputs = {
  name       = "ninlil/technitium-console"
  tag        = "0.1.0"
  context    = "${get_repo_root()}/src"
  dockerfile = "${get_repo_root()}/src/Dockerfile"
  platform   = "linux/amd64"
  "build-arguments" = {
    DOTNET_CONFIGURATION = "Release"
  }
  registry = {
    address  = "ghcr.io"
    url      = "ghcr.io/fabigail"
    username = get_env("GITHUB_ACTOR", "")
    password = get_env("GITHUB_TOKEN", "")
  }
}
```
