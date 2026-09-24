/**
 * This variable block defines the configuration to publish the project or solution for.
 */
variable "configuration" {

  // Define the default value for the variable.
  default = "Release"

  // Describe our variable
  description = "The configuration to publish the project or solution for."

  // Define the expected type for the variable.
  type = string
}

/*
 * This variable block defines the path to the NuGet configuration file to use when restoring packages.
 */
variable "nuget-config-file" {

  // Define the default value for the variable.
  default = null

  // Describe our variable
  description = "The path to the NuGet configuration file to use when restoring packages."

  // Define the expected type for the variable.
  type = string
}

/**
 * This variable block defines the output directory to publish the project or solution to.
 */
variable "output" {

  // Describe our variable.
  description = "The output directory to publish the project or solution to."

  // Define the expected type for the variable.
  type = string
}

/**
 * This variable block defines the path to the project or solution file to publish.
 */
variable "project" {

  // Describe our variable.
  description = "The path to the project or solution file to publish."

  // Define the expected type for the variable.
  type = string
}

/**
 * This variable block defines the runtime to publish the project or solution for.
 */
variable "runtime" {

  // Define the default value for the variable.
  default = "linux-arm64"

  // Describe our variable.
  description = "The runtime to publish the project or solution for."

  // Define the expected type for the variable.
  type = string
}

/**
 * This variable block defines whether to publish the .NET Core runtime with your application.
 */
variable "self-contained" {

  // Define the default value for the variable.
  default = false

  // Describe our variable.
  description = "Publish the .NET Core runtime with your application."

  // Define the expected type for the variable.
  type = bool
}

/**
 * This variable block defines a trigger to force re-publishing the project or solution.
 */
variable "trigger" {

  // Define the default value for the variable.
  default = timestamp()

  // Describe our variable.
  description = "A trigger to force re-publishing the project or solution."

  // Define the expected type for the variable.
  type = string
}

/**
 * This variable block defines the verbosity of publish output.
 */
variable "verbosity" {

  // Define the default value for the variable.
  default = "minimal"

  // Describe our variable.
  description = "The verbosity of publish output."

  // Define the expected type for the variable.
  type = string
}
