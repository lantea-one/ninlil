/**
 * This Terraform module is responsible for building a Docker image and optionally pushing it to a remote registry.
 */
terraform {

  // Define our required providers.
  required_providers {

    // Define the docker provider.
    docker = {

      // Define the docker provider source.
      source = "kreuzwerker/docker"

      // Define the required Docker provider version.
      version = ">= 4.5.0"
    }
  }

  // Define the required Terraform version.
  required_version = ">= 1.16.0"
}

// Configure our Docker provider
provider "docker" {

  // Set the host for Docker
  host = var.host
}
