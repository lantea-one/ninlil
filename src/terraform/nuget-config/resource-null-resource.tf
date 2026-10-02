/**
 * This resource will add the default NuGet.org source repository to the nuget.config file.
 */
resource "null_resource" "nuget-source-default" {
  depends_on = [local_file.write-nuget-config]

  provisioner "local-exec" {
    command = format(join(" ", [
      "dotnet", "nuget", "add",
      "source 'https://api.nuget.org/v3/index.json'",
      "--configfile '%s'",
      "--name 'NuGet.org'",
      "--protocol-version '3'"
    ]), local.config-file)
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}

/**
 * This resource will add any custom NuGet source repositories to the nuget.config file.
 */
resource "null_resource" "nuget-source-custom" {
  depends_on = [null_resource.nuget-config]

  // One resource per entry of var.sources, keyed by its URL; NuGet.org is added above.
  for_each = { for i, s in var.sources : s.url => s }

  provisioner "local-exec" {
    command = (each.value.auth ?
      format(join(" ", [
        "dotnet", "nuget", "add",
        "source '%s'",
        "--configfile '%s'",
        "--name '%s'",
        "--password '%s'",
        "--protocol-version '3'",
        "--store-password-in-clear-text",
        "--username '%s'"
      ]), each.key, local.config-file, each.value.name, each.value.password, each.value.username) : format(join(" ", [
        "dotnet", "nuget", "add",
        "source '%s'",
        "--configfile '%s'",
        "--name '%s'",
        "--protocol-version '3'"
      ]), each.key, local.config-file, each.value.name))
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}
