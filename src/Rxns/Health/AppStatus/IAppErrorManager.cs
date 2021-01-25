using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Rxns.Logging;
using Rxns.Metrics;

namespace Rxns.Health.AppStatus
{
    /// <summary>
    /// Errors are exceptional circumstances encounerted by a component of the system
    /// and generally have log information (meta data) associated with them. This manager
    /// records errors for later retrieval and debugging.
    /// </summary>
    public interface IAppErrorManager
    {
        /// <summary>
        /// Gets a list of all errors in the system. 
        /// All parameters are optional.
        /// </summary>
        /// <param name="page">The page number</param>
        /// <param name="size">The number of results per page</param>
        /// <param name="tenant">The tenant to restrict the query too</param>
        /// <returns>A list of errors</returns>
        IObservable<SystemErrors> GetAllErrors(int page = 0, int size = 10, string tenant = null);
        /// <summary>
        /// Lists the errors that have not been actioned in the system.
        /// All parameters are optional.
        /// </summary>
        /// <param name="page"></param>
        /// <param name="size"></param>
        /// <param name="tenant"></param>
        /// <param name="systemName"></param>
        /// <returns></returns>
        IObservable<SystemErrors> GetOutstandingErrors(int page = 0, int size = 10, string tenant = null, string systemName = null);
        /// <summary>
        /// Retrieves the log information related to an error
        /// </summary>
        /// <param name="errorId">The error</param>
        /// <returns>The log information</returns>
        IObservable<SystemLogMeta> GetErrorMeta(string errorId);
        /// <summary>
        /// Reports log information for an error to the system
        ///
        /// Note:
        /// use errors/publish to report both error and logs in one call.
        /// </summary>
        /// <param name="meta">The log information associated with the error</param>
        void InsertErrorMeta(string errorId, SystemLogMeta[] meta);
        /// <summary>
        /// Reports an error report to the system
        /// </summary>
        /// <param name="error">The report to publish</param>
        void InsertError(BasicErrorReport error);
    }
}
