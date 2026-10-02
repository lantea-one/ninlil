/**
 * This variable block defines the absolute path to the NuGet.config file that will be used for
 * publishing the NuGet package. If this variable is null, nuget.config in var.workspace is used.
 */
variable "config-file" {
  default = null

  description = "The absolute path to the NuGet.config file."

  type = string
}

/**
 * This variable block defines whether the NuGet package should include the source code or not.
 * If this variable is set to true, the source code will be included in the NuGet package.
 * If this variable is set to false, the source code will not be included in the NuGet package.
 */
variable "include-source" {
  default = false

  description = "Denotes whether the NuGet package should include the source code or not."

  type = bool
}

/**
 * This variable block defines whether the NuGet package should include the symbols or not.
 */
variable "include-symbols" {
  default = false

  description = "Denotes whether the NuGet package should include the symbols or not."

  type = bool
}

/**
 * This variable block defines whether the NuGet package is marked serviceable (dotnet pack
 * --serviceable).
 */
variable "serviceable" {
  default = false

  description = "Denotes whether the NuGet package is serviceable or not."

  type = bool
}

/**
 * This variable block defines the NuGet package name to use. No resource reads it, so the
 * package is always named after the project.
 */
variable "package-name" {
  default = null

  description = "The NuGet package name to use, if null the project name will be used."

  type = string
}

/**
 * This variable block defines the NuGet package version to use.
 * If this variable is null, the version will be determined from the project file.
 */
variable "package-version" {
  default = null

  description = join(" ", [
    "The version of the NuGet package to publish.",
    "If this variable is null, the version will be determined from the project file."
  ])

  type = string
}

/**
 * This variable block defines the .NET project that is to be built and published to a NuGet repository.
 */
variable "project" {
  description = "The .NET project that is to be built and published to a NuGet repository."

  type = string
}

/**
 * This variable block defines whether the .NET project should be published to the NuGet server repository or not.
 */
variable "publish" {
  default = true

  description = "Denotes whether the .NET project should be published to the NuGet server repository or not."

  type = bool

}

/**
 * This variable block defines the directory that clean and pack use as their artifacts path and
 * pack writes the packages to. If this variable is null or empty, var.workspace is used.
 */
variable "publish-directory" {
  default = null

  description = "The directory to which the .NET project will be published before being packaged."

  type = string

}

/**
 * This variable block defines the NuGet server repository to which the .NET project will be published.
 */
variable "server" {
  description = "The NuGet server repository to which the package will be deployed."

  type = string
}

/**
 * This variable block defines the authentication API key for the
 * NuGet server repository to which the .NET project will be published.
 */
variable "server-api-key" {
  default = null

  description = join(" ", [
    "The authentication API key for the NuGet server",
    "repository to which the .NET project will be published."
  ])

  type = string
}

/**
 * This variable block defines whether the NuGet server repository requires authentication or
 * not. No resource reads it; the push always passes var.server-api-key.
 */
variable "server-authenticate" {
  default = true

  description = "Denotes whether the NuGet server repository requires authentication."

  type = bool
}

/**
 * This variable block defines the absolute path to the project directory.
 */
variable "workspace" {
  description = "The absolute path to the project directory."

  type = string
}
