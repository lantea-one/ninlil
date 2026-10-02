/**
 * Pushes every .nupkg in the publish directory (or var.workspace) to var.server, without symbol
 * packages, skipping versions the server already has.
 */
resource "null_resource" "nuget-publish" {
  count = (var.publish ? 1 : 0)

  depends_on = [null_resource.nuget-publish-dotnet-package]

  provisioner "local-exec" {
    command = format(join(" ", [
      "dotnet", "nuget", "push", "'%s/*.nupkg'", "--api-key '%s'", "--no-symbols", "--skip-duplicate", "--source '%s'"
    ]), (((var.publish-directory != null) && (var.publish-directory != "")) ?
      (var.publish-directory) : (var.workspace)), var.server-api-key, var.server)
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}

/**
 * This resource is used to clean the .NET project before packaging it for NuGet.
 * It will remove any previously built NuGet packages and the publishing directory if it exists.
 */
resource "null_resource" "nuget-publish-dotnet-clean" {
  depends_on = [null_resource.nuget-publish-dotnet-restore]

  provisioner "local-exec" {
    command = format(join(" ", [
      "dotnet clean '%s'", "--artifacts-path '%s'", "--configuration 'Release'", "--nologo", "--verbosity 'Minimal'"
    ]), local.project-file, (((var.publish-directory != null) && (var.publish-directory != "")) ?
      (var.publish-directory) : (var.workspace)))
  }

  provisioner "local-exec" {
    command = format("rm -fr '%s/*.nupkg' 2> /dev/null || true;",
    (((var.publish-directory != null) && (var.publish-directory != "")) ? (var.publish-directory) : (var.workspace)))
  }

  provisioner "local-exec" {
    command = (((var.publish-directory != null) && (var.publish-directory != "")) ?
    (format("rm -fr '%s' 2> /dev/null || true;", var.publish-directory)) : ("echo '';"))
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}

/**
 * This resource is used to package the .NET project into a NuGet package using the dotnet pack command.
 */
resource "null_resource" "nuget-publish-dotnet-package" {
  depends_on = [null_resource.nuget-publish-dotnet-clean]

  provisioner "local-exec" {
    command = format(join(" ", [
      "dotnet pack '%s'",
      "--artifacts-path '%s'",
      "--configfile '%s'",
      "--configuration 'Release'%s%s%s",
      "--nologo", "--output '%s'%s", "--verbosity 'Minimal';"
      ]), local.project-file, (((var.publish-directory != null) && (var.publish-directory != "")) ?
      (var.publish-directory) : (var.workspace)), local.config-file, ((var.include-source == true) ?
      (" --include-source") : ("")), ((var.include-symbols == true) ?
      (" --include-symbols") : ("")), (((var.package-version != null) && (var.package-version != "")) ?
      (format(" -p:PackageVersion=%s", var.package-version)) : ("")),
      (((var.publish-directory != null) && (var.publish-directory != "")) ?
    (var.publish-directory) : (var.workspace)), ((var.serviceable == true) ? (" --serviceable") : ("")))
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}

/**
 * This resource is used to restore the .NET project dependencies using the dotnet restore command.
 * It will use the NuGet configuration file to restore the packages from the specified sources.
 */
resource "null_resource" "nuget-publish-dotnet-restore" {
  depends_on = [null_resource.nuget-publish-package-version]

  provisioner "local-exec" {
    command = format(join(" ", [
      "dotnet", "restore", "'%s'", "--configfile '%s'", "--no-cache", "--verbosity 'Minimal'"
    ]), local.project_file, local.nuget_config_file)
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}

/**
 * This resource is used to read the first <Version> from the project file. The value is only
 * printed; nothing else uses it.
 */
resource "null_resource" "nuget-publish-package-version" {
  provisioner "local-exec" {
    command = format("cat '%s' | grep -oPm1 '(?<=<Version>)[^<]+'", local.project-file)
  }

  triggers = {
    // timestamp() changes on every apply, so this runs every time.
    always_run = timestamp()
  }
}
