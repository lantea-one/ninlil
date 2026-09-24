/**
 * This resource file defines the null resources used to publish a .NET project to a NuGet server.
 */
resource "null_resource" "nuget-publish" {

  // Only run this resource if the publish flag is set to true.
  count = (var.publish ? 1 : 0)

  // We'll need the nuget configuration file resource to run before this one.
  depends_on = [null_resource.nuget-publish-dotnet-package]

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our dotnet nuget push command to be executed.
    command = format(join(" ", [
      "dotnet", "nuget", "push", "'%s/*.nupkg'", "--api-key '%s'", "--no-symbols", "--skip-duplicate", "--source '%s'"
    ]), (((var.publish-directory != null) && (var.publish-directory != "")) ?
      (var.publish-directory) : (var.workspace)), var.server-api-key, var.server)
  }

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}

/**
 * This resource is used to clean the .NET project before packaging it for NuGet.
 * It will remove any previously built NuGet packages and the publishing directory if it exists.
 */
resource "null_resource" "nuget-publish-dotnet-clean" {

  // We'll need the restore resource to run before this one.
  depends_on = [null_resource.nuget-publish-dotnet-restore]

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our dotnet clean command to be executed.
    command = format(join(" ", [
      "dotnet clean '%s'", "--artifacts-path '%s'", "--configuration 'Release'", "--nologo", "--verbosity 'Minimal'"
    ]), local.project-file, (((var.publish-directory != null) && (var.publish-directory != "")) ?
      (var.publish-directory) : (var.workspace)))
  }

  // Define the local execution provisioner for this resource which removes any previously build NuGet packages.
  provisioner "local-exec" {

    // Build our rm command to be executed.
    command = format("rm -fr '%s/*.nupkg' 2> /dev/null || true;",
    (((var.publish-directory != null) && (var.publish-directory != "")) ? (var.publish-directory) : (var.workspace)))
  }

  // Define the local execution provisioner for this resource which
  // removes any previously build NuGet package publishing directory.
  provisioner "local-exec" {

    // Build our rm command to be executed.
    command = (((var.publish-directory != null) && (var.publish-directory != "")) ?
    (format("rm -fr '%s' 2> /dev/null || true;", var.publish-directory)) : ("echo '';"))
  }

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}

/**
 * This resource is used to package the .NET project into a NuGet package using the dotnet pack command.
 */
resource "null_resource" "nuget-publish-dotnet-package" {

  // We'll need the nuget configuration file resource to run before this one.
  depends_on = [null_resource.nuget-publish-dotnet-clean]

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our dotnet pack command to be executed.
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

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}

/**
 * This resource is used to restore the .NET project dependencies using the dotnet restore command.
 * It will use the NuGet configuration file to restore the packages from the specified sources.
 */
resource "null_resource" "nuget-publish-dotnet-restore" {

  // We'll need the nuget configuration file sources resource to run before this one.
  depends_on = [null_resource.nuget-publish-package-version]

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our dotnet restore command to be executed.
    command = format(join(" ", [
      "dotnet", "restore", "'%s'", "--configfile '%s'", "--no-cache", "--verbosity 'Minimal'"
    ]), local.project_file, local.nuget_config_file)
  }

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}

/**
 * This resource is used to extract the version of the .NET project from the project file.
 */
resource "null_resource" "nuget-publish-package-version" {

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // Build our cat command to be executed.
    command = format("cat '%s' | grep -oPm1 '(?<=<Version>)[^<]+'", local.project-file)
  }

  // Define the triggers for this resource.
  triggers = {

    // We'll always want this to trigger.
    always_run = timestamp()
  }
}
