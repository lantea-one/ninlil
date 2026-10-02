/**
 * The built image's name (local.url).
 */
output "name" {
  depends_on = [docker_image.build-image]

  description = "The name of the image we just built."

  value = docker_image.build-image.name
}

/**
 * The image's registry URL (local.url).
 */
output "url" {
  depends_on = [docker_registry_image.push-image]

  description = "The URL of the image we just built."

  value = local.url
}

/**
 * The registry URL pinned to the pushed image's SHA256 digest, or the plain URL when no registry
 * URL is set.
 */
output "url-with-digest" {
  depends_on = [docker_registry_image.remote]

  description = "The URL of the image we just built."

  value = (coalesce(var.registry.url, "_") != "_" ?
    format("%s@%s", local.url, docker_registry_image.push-image[0].sha256_digest) : local.url)
}
