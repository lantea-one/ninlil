/**
 * This output block defines the name of the image we just built.
 */
output "name" {

  // Define the dependencies for this output.
  depends_on = [docker_image.build-image]

  // Describe our output.
  description = "The name of the image we just built."

  // Set the value of the output to the name of the image we just built.
  value = docker_image.build-image.name
}

/**
 * This output block defines the full registry URL of the image we just built.
 */
output "url" {

  // Define the dependencies for this output.
  depends_on = [docker_registry_image.push-image]

  // Describe our output.
  description = "The URL of the image we just built."

  // Set the value of the output to the URL of the image we just built.
  value = local.url
}

/**
 * This output block defines the full registry URL [including SHA256 digest] of the image we just built.
 */
output "url-with-digest" {

  // Define the dependencies for this output.
  depends_on = [docker_registry_image.remote]

  // Describe our output.
  description = "The URL of the image we just built."

  // Set the value of the output to the URL of the image we just built.
  value = (coalesce(var.registry.url, "_") != "_" ?
    format("%s@%s", local.url, docker_registry_image.push-image[0].sha256_digest) : local.url)
}
