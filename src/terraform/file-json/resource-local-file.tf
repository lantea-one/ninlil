/**
 * This reource block defines the write action for the JSON file.
 */
resource "local_file" "write-json-file" {

  // Define the content of the environment file.
  content = jsonencode(coalesce(var.data, "_") == "_" ? {} : var.data)

  // Define the destination of the environment file.
  filename = abspath(trimspace(var.file))
}
