// Define our namespace.
namespace Ninlil.Web.Routing;

/// <summary>
///     This attribute marks a read-only GET action (or a controller whose actions are all GET)
///     as safe to serve from a read replica of the database. <see cref="Middleware.StorageReadIntentMiddleware" />
///     reads it from the endpoint metadata and asks for the replica before anything resolves a
///     repository; the storage router then sends the request there only while a replica is
///     configured and eligible, and the caller did not write within the sticky window
///     (<c>Storage:ReadReplica:StickyAfterWriteSeconds</c>). Without a replica it changes nothing.
///     <para>
///         Use it on admin lists and details and on query-log and statistics reads. Never use it
///         on an action that writes or reads in order to write (a PUT, PATCH or DELETE loads the
///         row it changes), on sign-in, token or session actions, or on backup export: those must
///         see the current state. <see cref="ReadFromReplicaEndpointCheck" /> refuses to start the
///         host when the attribute is on an action that can receive another method than GET or
///         HEAD. The attribute is not part of the REST API's documented shape.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ReadFromReplicaAttribute : Attribute;
