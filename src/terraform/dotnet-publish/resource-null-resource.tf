/**
 * Empties the output directory on Windows.
 */
resource "null_resource" "bootstrap-dos" {
  count = (local.is_windows ? 1 : 0)

  provisioner "local-exec" {
    // Windows, so the commands are PowerShell.
    interpreter = ["PowerShell", "-Command"]

    command = join(" ; ", [
      format("Remove-Item -ErrorAction SilentlyContinue -Force -Path '%s' -Recurse",
        local.output_path),
      format("New-Item -Force -ItemType Directory -Path '%s' | Out-Null", local.output_path)
    ])
  }

  triggers = {
    // Runs again whenever var.trigger changes.
    always_run = local.trigger
  }
}

/**
 * Empties the output directory on Linux and macOS.
 */
resource "null_resource" "bootstrap-unix" {
  count = (local.is_windows ? 0 : 1)

  provisioner "local-exec" {
    // Not Windows, so the commands run in bash.
    interpreter = ["/bin/bash", "-c"]

    command = join(" ; ", [
      format("rm -force --recursive '%s' 2> /dev/null", local.output_path),
      format("mkdir -p '%s'", local.output_path)
    ])
  }

  triggers = {
    // Runs again whenever var.trigger changes.
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to clean the .NET project or solution before publishing.
 */
resource "null_resource" "clean" {
  depends_on = [null_resource.restore]

  provisioner "local-exec" {
    command = join(" ", [
      "dotnet clean", var.project,
      "--configuration", coalesce(var.configuration, "Release"),
      "--nologo", "--runtime", var.runtime, "--verbosity", var.verbosity])
  }

  triggers = {
    // Runs again whenever var.trigger changes.
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to publish the .NET project or solution to the specified output directory.
 */
resource "null_resource" "publish" {
  depends_on = [null_resource.clean]

  provisioner "local-exec" {
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

  triggers = {
    // Runs again whenever var.trigger changes.
    always_run = local.trigger
  }
}

/**
 * This resource block defines the resource to restore the .NET project or solution before publishing,
 * bypassing the HTTP cache and using var.nuget-config-file when one is given.
 */
resource "null_resource" "restore" {
  depends_on = null_resource.bootstrap-dos, null_resource.bootstrap-unix]

  provisioner "local-exec" {
    command = join(" ", concat([
      "dotnet restore", var.project, "--no-cache",
      "--runtime", var.runtime,
      "--verbosity", var.verbosity
    ], (coalesce(var.nuget-config-file, "_") == "_" ? [] : ["--configfile", abspath(var.nuget-config-file)])))
  }

  triggers = {
    // Runs again whenever var.trigger changes.
    always_run = local.trigger
  }
}
