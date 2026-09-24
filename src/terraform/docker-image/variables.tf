/**
 * This variable block defines the build arguments to pass to the Docker build process.
 */
variable "build-arguments" {

  // Define the default value for the variable.
  default = {}

  // Define the description for the variable.
  description = "The Docker build arguments."

  // Define the type of the variable.
  type = map(string)
}

/**
 * This varable block defines the Docker context directory to use for the Docker build process.
 */
variable "context" {

  // Define the default value for the variable.
  default = "docker"

  // Define the description for the variable.
  description = "The Docker context"

  // Define the type of the variable.
  type = string
}

/**
 * This variable block defines the Dockerfile to use for the Docker build process.
 */
variable "dockerfile" {

  // Define the default value for the variable.
  default = "Dockerfile"

  // Define the description for the variable.
  description = "The Dockerfile"

  // Define the type of the variable.
  type = string
}

/**
 * This variable block defines the host on which the Docker process
 * is listening. This defaults to the default Docker UNIX socket.
 */
variable "host" {

  // Define the default value for the variable.
  default = "unix:///var/run/docker.sock"

  // Define the description for the variable.
  description = "The Docker host."

  // Define the type of the variable.
  type = string
}

/**
 * This variable block defines whether to keep the image in the remote registry after pushing or not.
 */
variable "keep-remotely" {

  // Define the default value for the variable.
  default = true

  // Define the description for the variable.
  description = "Whether to keep the image remotely in the registry after pushing."

  // Define the type of the variable.
  type = bool
}

/**
 * This variable block defines the name of the Docker image being built.
 */
variable "name" {

  // Define the default value for the variable.
  default = null

  // Define the description for the variable.
  description = "The name of the built image."

  // Define the type of the variable.
  type = string
}

/**
 * This variable block defines whether to ignore build cache or not during the Docker build process.
 */
variable "no-cache" {

  // Define the default value for the variable.
  default = true

  // Define the description for the variable.
  description = "Whether to disable the build cache when building the Docker image."

  // Define the type of the variable.
  type = bool
}

/**
 * This variable block defines the platform for which the Docker build process will target.
 */
variable "platform" {

  // Define the default value for the variable.
  default = "linux/amd64"

  // Define the description for the variable.
  description = "The Docker platform."

  // Define the type of the variable.
  type = string

  // Validate the variable.
  validation {

    // Define the error message.
    error_message = "The Docker platform must be in the format of <os>/<arch>."

    // Define the condition for the validation.
    condition = lower(var.platform) == "linux/amd64" || lower(var.platform) == "linux/arm64/v8"
  }
}

/**
 * This variable block defines the Docker Registry information in order to push the image after its built.
 */
variable "registry" {

  // Define the default value for the variable.
  default = { address = "", password = "", url = "", username = "" }

  // Describe our variable.
  description = "The Docker registry configuration."

  // Define the type of the variable.
  type = object({

    // The address of the Docker image registry to push the built image to.
    address  = string

    // The password with which to authenticate to the Docker image registry.
    password = string

    // The URI of the registry image to push to.
    url = string

    // The username with which to authenticate to the Docker image registry.
    username = string
  })
}

/**
 * This variable block defines image sources to preload for the Docker build process.
 */
variable "sources" {

  // Define the default value for the variable.
  default = []

  // Define the description for the variable.
  description = "The source images to build from."

  // Define the type of the variable.
  type = list(string)
}

/**
 * This variable block defines the tag for the image in the Docker registry.
 */
variable "tag" {

  // Define the default value for the variable.
  default = "latest"

  // Define the description for the variable.
  description = "The tag for the built image."

  // Define the type of the variable.
  type = string
}

/**
 * This variable block defines the trigger for re-running the Docker build process and pushing the image.
 */
variable "trigger" {

  // Define the default value for the variable.
  default = timestamp()

  // Define the description for the variable.
  description = "The trigger for rebuilding the image."

  // Define the type of the variable.
  type = string
}
