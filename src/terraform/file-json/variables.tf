/**
 * This variable block defines the data object to be serialized into JSON for the JSON file.
 */
variable "data" {
  default = {}

  description = "The contents to be serialized into JSON and written to the filesystem."

  type = any
}

/**
 * This variable block defines the path to the file to which the JSON-serialized data object will be written.
 */
variable "file" {
  default = "environment.json"

  description = "The file path to which the JSON object will be written."

  type = string
}
