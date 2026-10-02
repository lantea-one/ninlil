/**
 * This variable block defines the build arguments to pass to the Docker build process.
 */
variable "build-arguments" {
  default = {}

  description = "The Docker build arguments."

  type = map(string)
}

/**
 * This variable block defines the Docker context directory to use for the Docker build process.
 */
variable "context" {
  default = "docker"

  description = "The Docker context"

  type = string
}

/**
 * This variable block defines the Dockerfile to use for the Docker build process.
 */
variable "dockerfile" {
  default = "Dockerfile"

  description = "The Dockerfile"

  type = string
}

/**
 * This variable block defines the host on which the Docker process
 * is listening. This defaults to the default Docker UNIX socket.
 */
variable "host" {
  default = "unix:///var/run/docker.sock"

  description = "The Docker host."

  type = string
}

/**
 * This variable block defines whether to keep the image in the remote registry after pushing or not.
 */
variable "keep-remotely" {
  default = true

  description = "Whether to keep the image remotely in the registry after pushing."

  type = bool
}

/**
 * This variable block defines the name of the Docker image being built.
 */
variable "name" {
  default = null

  description = "The name of the built image."

  type = string
}

/**
 * This variable block defines whether to ignore build cache or not during the Docker build process.
 */
variable "no-cache" {
  default = true

  description = "Whether to disable the build cache when building the Docker image."

  type = bool
}

/**
 * This variable block defines the platform to build for. The validation accepts only
 * linux/amd64 and linux/arm64/v8.
 */
variable "platform" {
  default = "linux/amd64"

  description = "The Docker platform."

  type = string

  validation {
    error_message = "The Docker platform must be in the format of <os>/<arch>."

    condition = lower(var.platform) == "linux/amd64" || lower(var.platform) == "linux/arm64/v8"
  }
}

/**
 * This variable block defines the registry to push the image to after it is built. The image is
 * pushed only when address, password and url are all non-empty.
 */
variable "registry" {
  default = { address = "", password = "", url = "", username = "" }

  description = "The Docker registry configuration."

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
 * This variable block defines images to use as build cache sources (cache_from). When it is
 * empty, the build pulls the base image instead.
 */
variable "sources" {
  default = []

  description = "The source images to build from."

  type = list(string)
}

/**
 * This variable block defines the tag for the image in the Docker registry.
 */
variable "tag" {
  default = "latest"

  description = "The tag for the built image."

  type = string
}

/**
 * This variable block defines the trigger for re-running the build and push: any change reruns
 * them. The timestamp() default is meant to change on every apply.
 */
variable "trigger" {
  default = timestamp()

  description = "The trigger for rebuilding the image."

  type = string
}
