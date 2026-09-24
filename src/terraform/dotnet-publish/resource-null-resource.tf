/**
 * This resource block defines the resource to bootstrap the output directory on a Windows system.
 */
resource "null_resource" "bootstrap-dos" {

  // Define whether to include this resource or not.
  count = (local.is_windows ? 1 : 0)

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // We're on a UNIX system, so we'll use bash to execute our script.
    interpreter = ["PowerShell", "-Command"]

    // Define the command to bootstrap our output directory.
    command = join(" ; ", [
      format("Remove-Item -ErrorAction SilentlyContinue -Force -Path '%s' -Recurse",
        local.output_path),
      format("New-Item -Force -ItemType Directory -Path '%s' | Out-Null", local.output_path)
    ])
  }

  // Define the triggers for this resource
  triggers = {

    // We'll always want this to trigger
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to bootstrap the output directory on a UNIX system.
 */
resource "null_resource" "bootstrap-unix" {

  // Define whether to include this resource or not.
  count = (local.is_windows ? 0 : 1)

  // Define the local execution provisioner for this resource.
  provisioner "local-exec" {

    // We're on a UNIX system, so we'll use bash to execute our script.
    interpreter = ["/bin/bash", "-c"]

    // Define the command to bootstrap our output directory.
    command = join(" ; ", [
      format("rm -force --recursive '%s' 2> /dev/null", local.output_path),
      format("mkdir -p '%s'", local.output_path)
    ])
  }

  // Define the triggers for this resource
  triggers = {

    // We'll always want this to trigger
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to clean the .NET project or solution before publishing.
 */
resource "null_resource" "clean" {

  // We'll need the restore resource to run before this one
  depends_on = [null_resource.restore]

  // Define the local execution provisioner for this resource
  provisioner "local-exec" {

    // Build our dotnet clean command to be executed
    command = join(" ", [
      "dotnet clean", var.project,
      "--configuration", coalesce(var.configuration, "Release"),
      "--nologo", "--runtime", var.runtime, "--verbosity", var.verbosity])
  }

  // Define the triggers for this resource
  triggers = {

    // We'll always want this to trigger
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to publish the .NET project or solution to the specified output directory.
 */
resource "null_resource" "publish" {

  // We'll need the clean resource to run before this one
  depends_on = [null_resource.clean]

  // Define the local execution provisioner for this resource
  provisioner "local-exec" {

    // Build our dotnet publish command to be executed
    command = join(" ", [
      "dotnet publish", var.project,
      "--configuration", coalesce(var.configuration, "Release"),
      "--force", "--nologo", "--no-restore",
      "--output", local.output_path,
      "--runtime", var.runtime,
      "--self-contained", (var.self_contained ? "true" : "false"),
      "--verbosity", var.verbosity
    ])
  }

  // Define the triggers for this resource
  triggers = {

    // We'll always want this to trigger
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to restore the .NET project or solution before publishing.
 */
resource "null_resource" "restore" {

  // We'll need the environment file to run before this one
  depends_on = null_resource.bootstrap-dos, null_resource.bootstrap-unix]

  // Define the local execution provisioner for this resource
  provisioner "local-exec" {

    // Build our dotnet restore command to be executed
    command = join(" ", concat([
      "dotnet restore", var.project, "--no-cache",
      "--runtime", var.runtime,
      "--verbosity", var.verbosity
    ], (coalesce(var.nuget-config-file, "_") == "_" ? [] : ["--configfile", abspath(var.nuget-config-file)])))
  }

  // Define the triggers for this resource
  triggers = {

    // We'll always want this to trigger
    always_run = local.trigger
  }
}
