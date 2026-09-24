/**
 * This file contains local values used in the code-dotnet-publish module.
 */
locals {

  // Define our Windows OS determinant.
  is-windows = (length(regexall("^[a-z]:", lower(local.output-path))) > 0 ? true : false)

  // Define our absolute output path.
  output-path = abspath(var.output)

  // Define our trigger to force re-publishing the project or solution.
  trigger = var.trigger
}
