/**
 * Local values for the dotnet-publish module.
 */
locals {
  // A drive-letter output path (c:...) means the module runs on Windows.
  is-windows = (length(regexall("^[a-z]:", lower(local.output-path))) > 0 ? true : false)

  output-path = abspath(var.output)

  trigger = var.trigger
}
