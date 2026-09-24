/**
 * This local block defines the local variables used during the Docker build process.
 */
locals {

  // Define the name of our image with the tag.
  name-with-tag = (coalesce(var.name, "_") != "_" ?
    format("%s:%s", trimspace(var.name), trimspace(var.tag)) : trimspace(var.tag))

  // Define the trigger for our image build.
  trigger = var.trigger

  // Define the full URL of our image.
  url = (coalesce(var.registry.url, "_") != "_" ?
    format("%s:%s", var.registry.url, local.name-with-tag) : local.name-with-tag)
}
