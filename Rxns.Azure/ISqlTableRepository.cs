using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Rxns.Azure
{
    /// <summary>
    /// Provides standard methods to manage data.
    /// </summary>
    public interface ISqlTableRepository
    {
        void Create(params Type[] tables);

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
            where TEntity : new();

        /// <summary>
        /// Insert the specified data into table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Add<TEntity>(params TEntity[] entities)
            where TEntity : new();

        /// <summary>
        /// Updates the specified data within table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Update<TEntity>(params TEntity[] entities)
            where TEntity : new();

        /// <summary>
        /// Deletes the specified data from table.
        /// </summary>
        /// <typeparam name="TEntity">The type of the data, also represents table.</typeparam>
        /// <param name="entities">The data.</param>
        void Delete<TEntity>(params TEntity[] entities)
            where TEntity : new();
        
    }
}
