/**
 * This variable block defines the data object to be serialized into JSON for the JSON file.
 */
variable "data" {

  // Define the default value of the variable.
  default = {}

  // Describe our variable.
  description = "The contents to be serialized into JSON and written to the filesystem."

  // Define the type of the variable.
  type = any
}

/**
 * This variable block defines the path to the file to which the JSON-serialized data object will be written.
 */
variable "file" {

  // Define the default value for the variable.
  default = "environment.json"

  // Describe our variable
  description = "The file path to which the JSON object will be written."

  // Define the expected type for the variable.
  type = string
}
