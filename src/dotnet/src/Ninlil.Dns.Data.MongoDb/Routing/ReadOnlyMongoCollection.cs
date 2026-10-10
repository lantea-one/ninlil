using System.Reflection;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Routing;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Routing;

/// <summary>
///     This class wraps a MongoDB collection so that every write method throws
///     <see cref="StorageReadOnlyException" /> and every other member passes through to the real
///     collection. <see cref="NinlilDnsMongoContext" /> uses it for the replica view. The wrapper
///     is a dynamic proxy, so it needs no member-by-member copy of
///     <see cref="IMongoCollection{TDocument}" /> and cannot fall behind a driver upgrade. A
///     method counts as a write when its name starts with Insert, Update, Replace, Delete,
///     FindOneAnd or BulkWrite (sync and async forms alike).
/// </summary>
public static class ReadOnlyMongoCollection
{
    /// <summary>
    ///     This method wraps <paramref name="collection" />.
    /// </summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    /// <param name="collection">The collection to guard.</param>
    /// <returns>A collection that reads like <paramref name="collection" /> and refuses writes.</returns>
    public static IMongoCollection<TDocument> Wrap<TDocument>(
        IMongoCollection<TDocument> collection
    )
    {
        IMongoCollection<TDocument> proxy = DispatchProxy.Create<
            IMongoCollection<TDocument>,
            Proxy<TDocument>
        >();
        ((Proxy<TDocument>)proxy).Target = collection;
        return proxy;
    }

    /// <summary>
    ///     This class is the dynamic proxy behind <see cref="Wrap{TDocument}" />.
    /// </summary>
    /// <typeparam name="TDocument">The document type.</typeparam>
    public class Proxy<TDocument> : DispatchProxy
    {
        private static readonly string[] WritePrefixes =
        [
            "Insert",
            "Update",
            "Replace",
            "Delete",
            "FindOneAnd",
            "BulkWrite",
        ];

        /// <summary>
        ///     This property contains the real collection the calls are forwarded to.
        /// </summary>
        public IMongoCollection<TDocument> Target { get; set; }

        /// <inheritdoc />
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (
                WritePrefixes.Any(prefix =>
                    targetMethod.Name.StartsWith(prefix, StringComparison.Ordinal)
                )
            )
                throw StorageReadOnlyException.For($"MongoDB {targetMethod.Name}");

            try
            {
                return targetMethod.Invoke(Target, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                // Show the driver's own exception, not the reflection wrapper.
                System
                    .Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                        exception.InnerException
                    )
                    .Throw();
                throw;
            }
        }
    }
}
