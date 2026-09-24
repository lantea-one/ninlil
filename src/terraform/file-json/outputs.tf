/**
 * This output block defines the full file path to the JSON file that was written.
 */
output "filename" {

  // Define the dependency of the output.
  depends_on = [local_file.write-json-file]

  // Define the value of the output.
  value = abspath(local_file.write-json-file.filename)
}
