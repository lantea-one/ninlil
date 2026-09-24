/**
 * This variable block defines the absolute path to the NuGet.config file that will be used for
 * publishing the NuGet package.  If this variable is null, the default NuGet.config file will be used.
 */
variable "config-file" {

  // Define the default value for the variable.
  default = null

  // Describe our variable.
  description = "The absolute path to the NuGet.config file."

  // Define our variable type.
  type = string
}

/**
 * This variable block defines whether the NuGet package should include the source code or not.
 * If this variable is set to true, the source code will be included in the NuGet package.
 * If this variable is set to false, the source code will not be included in the NuGet package.
 */
variable "include-source" {

  // Define the default value for the variable.
  default = false

  // Describe our variable.
  description = "Denotes whether the NuGet package should include the source code or not."

  // Define our variable type.
  type = bool
}

/**
 * This variable block defines whether the NuGet package should include the symbols or not.
 */
variable "include-symbols" {

  // Define the default value for the variable.
  default = false

  // Describe our variable.
  description = "Denotes whether the NuGet package should include the symbols or not."

  // Define our variable type.
  type = bool
}

/**
 * This variable block defines whether the NuGet package is serviceable or not.
 * If this variable is set to true, the NuGet package is considered serviceable and can receive updates.
 * If this variable is set to false, the NuGet package is considered non-serviceable and cannot receive updates.
 */
variable "serviceable" {

  // Define the default value for the variable.
  default = false

  // Describe our variable.
  description = "Denotes whether the NuGet package is serviceable or not."

  // Define our variable type.
  type = bool
}

/**
 * This variable block defines the NuGet package name to use.
 * If this variable is null, the project name will be used as the package name.
 */
variable "package-name" {

  // Define the default value for the variable.
  default = null

  // Describe our variable.
  description = "The NuGet package name to use, if null the project name will be used."

  // Define our variable type.
  type = string
}

/**
 * This variable block defines the NuGet package version to use.
 * If this variable is null, the version will be determined from the project file.
 */
variable "package-version" {

  // Define the default value for the variable.
  default = null

  // Describe our variable.
  description = join(" ", [
    "The version of the NuGet package to publish.",
    "If this variable is null, the version will be determined from the project file."
  ])

  // Define our variable type.
  type = string
}

/**
 * This variable block defines the .NET project that is to be built and published to a NuGet repository.
 */
variable "project" {

  // Describe our variable.
  description = "The .NET project that is to be built and published to a NuGet repository."

  // Define our variable type.
  type = string
}

/**
 * This variable block defines whether the .NET project should be published to the NuGet server repository or not.
 */
variable "publish" {

  // Define the default value for the variable.
  default = true

  // Describe our variable.
  description = "Denotes whether the .NET project should be published to the NuGet server repository or not."

  // Define our variable type.
  type = bool

}

/**
 * This variable block defines the directory to which the .NET project will be published before
 * being packaged. If this variable is null, the default publish directory will be used.
 */
variable "publish-directory" {

  // Define the default value for the variable.
  default = null

  // Describe our variable.
  description = "The directory to which the .NET project will be published before being packaged."

  // Define our variable type.
  type = string

}

/**
 * This variable block defines the NuGet server repository to which the .NET project will be published.
 */
variable "server" {

  // Describe our variable.
  description = "The NuGet server repository to which the package will be deployed."

  // Define our variable type.
  type = string
}

/**
 * This variable block defines the authentication API key for the
 * NuGet server repository to which the .NET project will be published.
 */
variable "server-api-key" {

  // Define the default value for the variable.
  default = null

  // Describe our variable.
  description = join(" ", [
    "The authentication API key for the NuGet server",
    "repository to which the .NET project will be published."
  ])

  // Define our variable type.
  type = string
}

/**
 * Thi variable block defines whether the NuGet server repository requires authentication or not.
 */
variable "server-authenticate" {

  // Define the default value for the variable.
  default = true

  // Describe our variable.
  description = "Denotes whether the NuGet server repository requires authentication."

  //Define our variable type.
  type = bool
}

/**
 * This variable block defines the absolute path to the project directory.
 */
variable "workspace" {

  // Describe our variable.
  description = "The absolute path to the project directory."

  // Define our variable type.
  type = string
}
