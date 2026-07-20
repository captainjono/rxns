using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.WindowsAzure.Storage.Table;

namespace Rxns.Azure
{
    public interface IAzureTableRepository
    {
        /// <summary>
        /// Gets the data from table by it's row key.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="rowKey">The key value of the data.</param>
        /// <returns>
        /// The data.
        /// </returns>
        TEntity GetByRowKey<TEntity>(string rowKey)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Gets the data from table by it's partition key.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="key">The key value of the data.</param>
        /// <returns>
        /// The data.
        /// </returns>
        IReadOnlyCollection<TEntity> GetByPartitionKey<TEntity>(string partitionKey)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Gets all data from table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <returns>
        /// The collection of data.
        /// </returns>
        IReadOnlyCollection<TEntity> GetAll<TEntity>()
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Gets the data from table by using predicate as filter. And return specific collection of data in specific page if specified.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="predicate">The predicate to filter the data. No predicate means no filter.</param>
        /// <param name="pageNo">The page number. Invalid page number will trigger no result.</param>
        /// <param name="pageSize">Size of the page. Invalid page size will trigger no result.</param>
        /// <returns>
        /// Collection of data.
        /// </returns>
        IReadOnlyCollection<TEntity> Get<TEntity>(Expression<Func<TEntity, bool>> predicate = null, int? pageNo = null, int? pageSize = null)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Gets the data from table by using predicate as filter. And return specific collection of data in specific page if specified after they are sorted first.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <typeparam name="TKey">The type of the key.</typeparam>
        /// <param name="predicate">The predicate to filter the data. No predicate means no filter.</param>
        /// <param name="keySelector">This selector determine which field the data will be sorted by.</param>
        /// <param name="ascending">if set to <c>true</c> [ascending].</param>
        /// <param name="pageNo">The page number. Invalid page number will trigger no result.</param>
        /// <param name="pageSize">Size of the page. Invalid page size will trigger no result.</param>
        /// <returns>
        /// Collection of data.
        /// </returns>
        IReadOnlyCollection<TEntity> Get<TEntity, TKey>(Expression<Func<TEntity, bool>> predicate = null, Expression<Func<TEntity, TKey>> keySelector = null, bool ascending = true, int? pageNo = null, int? pageSize = null)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Insert the specified data into table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Add<TEntity>(params TEntity[] entities)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Updates the specified data within table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Update<TEntity>(params TEntity[] entities)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Deletes all data within table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the entity.</typeparam>
        void DeleteAll<TEntity>()
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Deletes the specified data from table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Delete<TEntity>(params TEntity[] entities)
            where TEntity: ITableEntity, new();

        /// <summary>
        /// Deletes data from table by the specified predicate or filter.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="predicate">The predicate to filter the data. No predicate no delete.</param>
        void Delete<TEntity>(Expression<Func<TEntity, bool>> predicate)
            where TEntity : ITableEntity, new();

        /// <summary>
        /// Deletes data from table by it's row key.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="rowKey">The key.</param>
        void DeleteByRowKey<TEntity>(string rowKey)
            where TEntity : ITableEntity, new();

        /// <summary>
        /// Deletes data from table by it's partition key.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="partitionKey">The key.</param>
        void DeleteByPartitionKey<TEntity>(string partitionKey)
            where TEntity: ITableEntity, new();
    }
}
