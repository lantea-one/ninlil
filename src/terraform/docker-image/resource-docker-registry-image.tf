/**
 * This resource block defines the Docker Registry details for pushing the built image remotely.
 */
resource "docker_registry_image" "push-image" {

  // Define the authentication configuration for the registry.
  dynamic "auth_config" {

    // Define the content for the authentication configuration.
    content {

      // Define the address for the registry authentication configuration.
      address = var.registry.address

      // Define the password for the registry authentication configuration.
      password = var.registry.password

      // Define the username for the registry authentication configuration.
      username = var.registry.username
    }

    // Define the condition in which to include this authentication configuration.
    for_each = (coalesce(var.registry.address, "_") != "_"
      && coalesce(var.registry.password, "_") != "_"
      && coalesce(var.registry.username, "_") != ""
      ? [1] : [])
  }

  // Define the condition in which to include this resource.
  count = (coalesce(var.registry.address, "_") != "_"
    && coalesce(var.registry.password, "_") != "_"
    && coalesce(var.registry.url, "_") != "_"
    && coalesce(var.registry.username, "_") != ""
    ? 1 : 0)

  // Define our dependencies.
  depends_on = [docker_image.build-image]

  // We'll want to keep the image remotely.
  keep_remotely = var.keep-remotely

  // Define the name of our image.
  name = docker_image.local.name

  // Define the triggers for the image.
  triggers = {

    // Trigger always.
    always_run = local.trigger
  }
}
