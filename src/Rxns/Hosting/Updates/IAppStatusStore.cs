﻿using System;
using System.Collections.Generic;
using System.IO;
using Rxns.Health.AppStatus;
using Rxns.Logging;

namespace Rxns.Hosting.Updates
{

    public interface IAppStatusCfg
    {
        bool ShouldAutoUnzipLogs { get; }
        string AppRoot { get; set; }
    }

    public class AppStatusCfg : IAppStatusCfg
    {
        public bool ShouldAutoUnzipLogs { get; set; }
        public string AppRoot { get; set; }
    }

    public interface IAppCmdManager
    {
        IEnumerable<IRxnQuestion> FlushCommands(string route);

        void Add(IRxnQuestion cmds);
    }


    public interface IAppStatusStore : IAppCmdManager
    {
        IDictionary<object, object> Cache { get; }

        IDictionary<string, Dictionary<SystemStatusEvent, object[]>> GetSystemStatus();

        /// <summary>
        /// Resets  the entire systemstatus cache
        /// </summary>
        void Clear();

        /// <summary>
        /// resets a particular systemstatus cache
        /// </summary>
        /// <param name="route">The route to clear</param>
        void ClearSystemStatus(string route);

        //this is SystemLogMeta but it doesnt appear part of this interface lib so its object
        //could be a generic i know, this is a work in progress though so im not fussed
        IEnumerable<object> GetLog();
        IObservable<string> SaveLog(string tenant, Stream log, string file);
        IObservable<AppLogInfo[]> ListLogs(string tenantId, int top = 3);
        IObservable<Stream> GetLogs(string tenantId, string file);
        void Add(LogMessage<Exception> message);
        void Add(LogMessage<string> message);
    }
}
