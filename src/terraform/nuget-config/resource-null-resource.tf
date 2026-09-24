/**
 * This resource will add the default NuGet.org source repository to the nuget.config file.
 */
resource "null_resource" "nuget-source-default" {

  // We'll need the local_file resource to run before this one.
  depends_on = [local_file.write-nuget-config]

  // Define the local execution provisioner for this resource which will add the default NuGet.org source repository.
  provisioner "local-exec" {

    // Build our dotnet nuget command to be executed.
    command = format(join(" ", [
      "dotnet", "nuget", "add",
      "source 'https://api.nuget.org/v3/index.json'",
      "--configfile '%s'",
      "--name 'NuGet.org'",
      "--protocol-version '3'"
    ]), local.config-file)
  }

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}

/**
 * This resource will add any custom NuGet source repositories to the nuget.config file.
 */
resource "null_resource" "nuget-source-custom" {

  // We'll need the nuget configuration file resource to run before this one.
  depends_on = [null_resource.nuget-config]

  // We'll want to execute this for each source provided plus the default NuGet.org source.
  for_each = { for i, s in var.sources : s.url => s }

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our dotnet nuget command to be executed.
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

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}
