/**
 * This resource block defines the Docker build process for the image defined by the input variables.
 */
resource "docker_image" "build-image" {

  // Define the build configuration for the image
  build {

    // Define our build arguments
    build_args = var.build-arguments

    // Define allowed cache sources
    cache_from = var.sources

    // Define the context build directory
    context = var.context

    // Define the dockerfile to use
    dockerfile = var.dockerfile

    // Always pull the latest images
    no_cache = var.no-cache

    // Define the build platform
    platform = var.platform

    // Define whether or not to pull the parent image
    pull_parent = (length(var.sources) == 0 ? true : false)

    // We'll want to remove any ephemeral containers and images
    remove = true
  }

  // We'll want to force the removal of the image
  force_remove = true

  // We don't want to keep the image locally
  keep_locally = false

  // Define the name of our image
  name = local.url

  // Define the triggers for the image
  triggers = {

    // Trigger always
    always_run = local.trigger
  }
}
