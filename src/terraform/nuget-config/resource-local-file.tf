/*
 * This resource block defines a local_file resource that writes the NuGet.config file to the specified path.
 */
resource "local_file" "write-nuget-config" {

  // Define the contents of the NuGet.config file to be written as the XML template contents.
  content  = local.template

  // Define the path to the NuGet.config file to be written.
  filename = local.config-file
}
