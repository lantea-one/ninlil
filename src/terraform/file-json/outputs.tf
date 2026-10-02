/**
 * This output block defines the full file path to the JSON file that was written.
 */
output "filename" {
  depends_on = [local_file.write-json-file]

  value = abspath(local_file.write-json-file.filename)
}
