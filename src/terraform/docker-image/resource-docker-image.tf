/**
 * Builds the image from var.context and var.dockerfile for var.platform, named local.url.
 */
resource "docker_image" "build-image" {
  build {
    build_args = var.build-arguments

    cache_from = var.sources

    context = var.context

    dockerfile = var.dockerfile

    // Skip the build cache (true by default).
    no_cache = var.no-cache

    platform = var.platform

    // Pull the base image only when no cache source images are given.
    pull_parent = (length(var.sources) == 0 ? true : false)

    // Remove intermediate containers after the build.
    remove = true
  }

  // On destroy, remove the image even when a container still uses it...
  force_remove = true

  // ...and do not keep it in the local image store.
  keep_locally = false

  name = local.url

  triggers = {
    // Rebuilds whenever var.trigger changes.
    always_run = local.trigger
  }
}
