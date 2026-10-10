using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

// Define our namespace.
namespace Ninlil.Web.Routing;

/// <summary>
///     This class is the startup check for <see cref="ReadFromReplicaAttribute" />: a read
///     replica can be behind, so the attribute is allowed only on actions that cannot receive
///     anything but GET (or HEAD). <c>Program.cs</c> runs it once after the host is built and stops
///     the host with the list when it finds a misplaced attribute, so the mistake shows on the
///     first start and in every test that boots the host.
/// </summary>
public static class ReadFromReplicaEndpointCheck
{
    /// <summary>
    ///     This method lists the actions that carry <see cref="ReadFromReplicaAttribute" /> (on
    ///     the action or its controller) and accept a method other than GET or HEAD, or accept any
    ///     method because no HTTP method attribute limits them.
    /// </summary>
    /// <param name="actions">The controller actions of the application.</param>
    /// <returns>One line per misplaced action; empty when every use is valid.</returns>
    public static IReadOnlyList<string> FindMisplacedActions(
        IEnumerable<ControllerActionDescriptor> actions
    )
    {
        List<string> misplaced = [];

        foreach (ControllerActionDescriptor action in actions)
        {
            if (!action.EndpointMetadata.OfType<ReadFromReplicaAttribute>().Any())
                continue;

            string[] methods =
            [
                .. action
                    .EndpointMetadata.OfType<IHttpMethodMetadata>()
                    .SelectMany(metadata => metadata.HttpMethods),
            ];

            bool onlyReads =
                methods.Length > 0
                && methods.All(method => HttpMethods.IsGet(method) || HttpMethods.IsHead(method));

            if (!onlyReads)
                misplaced.Add(
                    $"{action.ControllerTypeInfo.Name}.{action.ActionName} "
                        + $"({(methods.Length == 0 ? "any method" : string.Join(", ", methods))})"
                );
        }

        return misplaced;
    }
}
