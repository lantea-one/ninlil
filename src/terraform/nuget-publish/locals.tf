/**
 * This Terraform locals file defines the absolute paths for the NuGet.config file and the .NET project file.
 */
locals {

  // Define the absolute path to the NuGet.config file.
  config-file = (coalesce(var.config-file, "_") == "_" ? format("%s/nuget.config", var.workspace) : var.config-file)

  // Define the absolute path to the .NET project file.
  project-file = (endswith(lower(var.project), ".csproj") || endswith(lower(var.project), ".sln") ?
    format("%s/%s", var.workspace, var.project) : format("%s/%s.csproj", var.workspace, var.project))
}
