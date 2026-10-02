/**
 * This variable block defines the configuration to publish the project or solution for.
 */
variable "configuration" {
  default = "Release"

  description = "The configuration to publish the project or solution for."

  type = string
}

/*
 * This variable block defines the path to the NuGet configuration file to use when restoring packages.
 */
variable "nuget-config-file" {
  default = null

  description = "The path to the NuGet configuration file to use when restoring packages."

  type = string
}

/**
 * This variable block defines the output directory to publish the project or solution to.
 */
variable "output" {
  description = "The output directory to publish the project or solution to."

  type = string
}

/**
 * This variable block defines the path to the project or solution file to publish.
 */
variable "project" {
  description = "The path to the project or solution file to publish."

  type = string
}

/**
 * This variable block defines the runtime to publish the project or solution for.
 */
variable "runtime" {
  default = "linux-arm64"

  description = "The runtime to publish the project or solution for."

  type = string
}

/**
 * This variable block defines whether to publish the .NET Core runtime with your application.
 */
variable "self-contained" {
  default = false

  description = "Publish the .NET Core runtime with your application."

  type = bool
}

/**
 * This variable block defines a trigger to force re-publishing the project or solution.
 */
variable "trigger" {
  default = timestamp()

  description = "A trigger to force re-publishing the project or solution."

  type = string
}

/**
 * This variable block defines the verbosity of publish output.
 */
variable "verbosity" {
  default = "minimal"

  description = "The verbosity of publish output."

  type = string
}
