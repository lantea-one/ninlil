/**
 * This variable block defines the additional sources to populate the nuget.config file with.
 */
variable "sources" {
  default = []

  description = "The additional sources to populate the nuget.config file with."

  type = list(object({
    authenticate: optional(bool, false)
    name: string,
    password: optional(string, null),
    url: string,
    username: optional(string, null)
  }))
}

/**
 * This variable block defines the template file to use for generating the nuget.config file.
 */
variable "template-file" {
  default = null

  description = join(" ", [
    "The nuget.config XML file path to use as the base configuration file for NuGet.",
    "Please do not include NuGet.org in the sources, it is included by default.",
    "See nuget.config.tfpl for an example of the template."
  ])

  type = string
}

/**
 * This variable block defines the workspace path to use for generating the nuget.config file.
 */
variable "workspace" {
  description = "The absolute path to the project directory."

  type = string
}
