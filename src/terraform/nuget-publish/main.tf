/**
 * Packs a .NET project with dotnet pack and, when var.publish is true, pushes the packages to
 * var.server with dotnet nuget push. The steps, chained with depends_on and all run on every
 * apply: read the version from the project file, restore, clean (also deleting old packages and
 * the publish directory), pack, push. It has no outputs.
 * Nothing in this repository's pipeline uses this module.
 */
terraform {
  required_version = ">= 1.16.0"
}
