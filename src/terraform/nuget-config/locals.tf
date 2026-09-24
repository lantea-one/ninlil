/**
  * This file defines local variables for the NuGet.config Terraform module.
  * It includes the absolute path to the NuGet.config file, the absolute path to the NuGet.config template file,
  * and the contents of the NuGet.config template file.
 */
locals {

  // Define the absolute path to the NuGet.config file.
  config-file = format("%s/nuget.config", var.workspace)

  // Define the absolute path to the NuGet.config template file.
  template-file = (coalesce(var.template-file, "_") != "_" ? var.template-file : "${path.module}/nuget.config.tfpl")

  // Define the contents of the NuGet.config template file.
  template = file(local.template-file)
}
