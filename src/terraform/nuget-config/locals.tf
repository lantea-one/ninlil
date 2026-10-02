/**
 * Local values: the nuget.config path in var.workspace, the template it starts from
 * (var.template-file, or nuget.config.tfpl next to this module), and that template's contents.
 */
locals {
  config-file = format("%s/nuget.config", var.workspace)

  template-file = (coalesce(var.template-file, "_") != "_" ? var.template-file : "${path.module}/nuget.config.tfpl")

  template = file(local.template-file)
}
