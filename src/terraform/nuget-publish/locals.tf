/**
 * The nuget.config path (var.config-file, or nuget.config in var.workspace) and the project file
 * path (var.project in var.workspace, with .csproj appended unless it ends in .csproj or .sln).
 */
locals {
  config-file = (coalesce(var.config-file, "_") == "_" ? format("%s/nuget.config", var.workspace) : var.config-file)

  project-file = (endswith(lower(var.project), ".csproj") || endswith(lower(var.project), ".sln") ?
    format("%s/%s", var.workspace, var.project) : format("%s/%s.csproj", var.workspace, var.project))
}
