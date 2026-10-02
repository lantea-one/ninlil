/*
 * Writes the template unchanged as nuget.config; the null resources add the sources afterward.
 */
resource "local_file" "write-nuget-config" {
  content  = local.template

  filename = local.config-file
}
