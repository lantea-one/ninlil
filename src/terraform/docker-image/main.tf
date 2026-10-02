/**
 * Builds a Docker image with the kreuzwerker/docker provider and, when var.registry is filled in,
 * pushes it to that registry. Its inputs are in variables.tf; its outputs are the image name, its
 * registry URL, and that URL pinned to the pushed digest.
 * Nothing in this repository's pipeline uses this module.
 */
terraform {
  required_providers {
    docker = {
      source = "kreuzwerker/docker"

      version = ">= 4.5.0"
    }
  }

  required_version = ">= 1.16.0"
}

// The Docker daemon to build with (var.host, the local socket by default).
provider "docker" {
  host = var.host
}
