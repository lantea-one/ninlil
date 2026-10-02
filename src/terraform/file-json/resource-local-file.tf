/**
 * Writes var.data as JSON to the absolute form of var.file.
 */
resource "local_file" "write-json-file" {
  content = jsonencode(coalesce(var.data, "_") == "_" ? {} : var.data)

  filename = abspath(trimspace(var.file))
}
