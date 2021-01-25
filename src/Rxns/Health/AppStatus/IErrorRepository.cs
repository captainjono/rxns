using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Rxns.Collections;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.Playback;

namespace Rxns.Health.AppStatus
{
    /// <summary>
    /// The repository used to store system error information
    /// </summary>
    public interface IErrorRepository
    {
        /// <summary>
        /// Retrieves errors from the store
        /// </summary>
        /// <param name="where">A clause which constrains the results</param>
        /// <param name="pageWith">Paging data used to contrain the results</param>
        /// <returns>The metrics which match the conditions</returns>
        IObservable<SystemErrors> GetErrors(Func<SystemErrors, bool> where = null, Page pageWith = null);

        /// <summary>
        /// Retrieves error log information from the store
        /// </summary>
        /// <param name="where">A clause which constrains the results</param>
        /// <returns>The metrics which match the conditions</returns>
        IObservable<SystemLogMeta> GetErrorMeta(string errorId, Func<SystemLogMeta, bool> where = null);
        
        /// <summary>
        /// Adds an error report the store
        /// </summary>
        /// <param name="report">The report</param>
        /// <returns>The ID of the error in the store</returns>
        string AddError(BasicErrorReport report);

        /// <summary>
        /// Adds a error log information the database
        /// </summary>
        /// <param name="meta">The log information for an error. Ensure the ErrorId is correctly set</param>
        /// <returns>The ID of the error log information in the store</returns>
        void AddErrorMeta(string errorReportId, SystemLogMeta[] meta);

        /// <summary>
        /// Removes an error
        /// </summary>
        /// <param name="errorId">The id of the error to delete</param>
        void DeleteError(long errorId);


        /// <summary>
        /// Removes an metric
        /// </summary>
        /// <param name="metricId">The id of the metric to delete</param>
        void DeleteMetric(long metricId);
    }
}
