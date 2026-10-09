using System.Linq.Expressions;
using MongoDB.Driver;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb;

/// <summary>
///     This class holds the write helpers the Mongo repositories use inside a staged operation.
///     A staged operation receives an <see cref="IClientSessionHandle" /> that is
///     <see langword="null" /> when the deployment cannot run transactions (a standalone
///     server). The driver's session overloads reject a null session, so each helper picks the
///     overload with or without one.
/// </summary>
public static class MongoSessionWrites
{
    /// <summary>
    ///     This method inserts one document, inside <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="document">The document to insert.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task InsertOneWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        TEntity document,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.InsertOneAsync(document, cancellationToken: stoppingToken)
            : collection.InsertOneAsync(session, document, cancellationToken: stoppingToken);

    /// <summary>
    ///     This method inserts many documents, inside <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="documents">The documents to insert.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task InsertManyWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        IEnumerable<TEntity> documents,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.InsertManyAsync(documents, cancellationToken: stoppingToken)
            : collection.InsertManyAsync(session, documents, cancellationToken: stoppingToken);

    /// <summary>
    ///     This method replaces the first document matching <paramref name="filter" />, inside
    ///     <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="filter">The filter that selects the document.</param>
    /// <param name="replacement">The new document.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task ReplaceOneWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        Expression<Func<TEntity, bool>> filter,
        TEntity replacement,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.ReplaceOneAsync(filter, replacement, cancellationToken: stoppingToken)
            : collection.ReplaceOneAsync(
                session,
                filter,
                replacement,
                cancellationToken: stoppingToken
            );

    /// <summary>
    ///     This method deletes the first document matching <paramref name="filter" />, inside
    ///     <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="filter">The filter that selects the document.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task DeleteOneWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        Expression<Func<TEntity, bool>> filter,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.DeleteOneAsync(filter, stoppingToken)
            : collection.DeleteOneAsync(session, filter, cancellationToken: stoppingToken);

    /// <summary>
    ///     This method deletes every document matching <paramref name="filter" />, inside
    ///     <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="filter">The filter that selects the documents.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task DeleteManyWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        Expression<Func<TEntity, bool>> filter,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.DeleteManyAsync(filter, stoppingToken)
            : collection.DeleteManyAsync(session, filter, cancellationToken: stoppingToken);

    /// <summary>
    ///     This method applies <paramref name="update" /> to every document matching
    ///     <paramref name="filter" />, inside <paramref name="session" /> when it is set.
    /// </summary>
    /// <typeparam name="TEntity">The document type.</typeparam>
    /// <param name="collection">The collection to write to.</param>
    /// <param name="session">The session of the running transaction, or <see langword="null" />.</param>
    /// <param name="filter">The filter that selects the documents.</param>
    /// <param name="update">The update to apply.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task UpdateManyWithSessionAsync<TEntity>(
        this IMongoCollection<TEntity> collection,
        IClientSessionHandle session,
        Expression<Func<TEntity, bool>> filter,
        UpdateDefinition<TEntity> update,
        CancellationToken stoppingToken
    ) =>
        session is null
            ? collection.UpdateManyAsync(filter, update, cancellationToken: stoppingToken)
            : collection.UpdateManyAsync(session, filter, update, cancellationToken: stoppingToken);
}
