/**
 * Image naming. name-with-tag is "<name>:<tag>", or only the tag when no name is given; url is
 * "<registry url>:<name-with-tag>" when a registry URL is set, otherwise name-with-tag.
 */
locals {
  name-with-tag = (coalesce(var.name, "_") != "_" ?
    format("%s:%s", trimspace(var.name), trimspace(var.tag)) : trimspace(var.tag))

  trigger = var.trigger

  url = (coalesce(var.registry.url, "_") != "_" ?
    format("%s:%s", var.registry.url, local.name-with-tag) : local.name-with-tag)
}
