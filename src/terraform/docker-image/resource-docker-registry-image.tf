/**
 * Pushes the built image to the registry in var.registry.
 */
resource "docker_registry_image" "push-image" {
  dynamic "auth_config" {
    content {
      address = var.registry.address

      password = var.registry.password

      username = var.registry.username
    }

    // Credentials only when the registry address and password are set.
    for_each = (coalesce(var.registry.address, "_") != "_"
      && coalesce(var.registry.password, "_") != "_"
      && coalesce(var.registry.username, "_") != ""
      ? [1] : [])
  }

  // Push only when the registry address, password and URL are set.
  count = (coalesce(var.registry.address, "_") != "_"
    && coalesce(var.registry.password, "_") != "_"
    && coalesce(var.registry.url, "_") != "_"
    && coalesce(var.registry.username, "_") != ""
    ? 1 : 0)

  depends_on = [docker_image.build-image]

  keep_remotely = var.keep-remotely

  name = docker_image.local.name

  triggers = {
    // Pushes again whenever var.trigger changes.
    always_run = local.trigger
  }
}
