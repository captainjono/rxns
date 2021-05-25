using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rxns.Interfaces;

namespace Rxns.Cloud
{
    public class AppStatusInfo
    {
        public string Key { get; set; }
        public object Value { get; set; }

        public AppStatusInfo()
        {
            
        }
        
        public AppStatusInfo(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }
    
    public class AppHeartbeat : IRxn
    {
        public SystemStatusEvent Status { get; set; }
        public AppStatusInfo[] Meta { get; set; }

        public AppHeartbeat()
        {
            
        }
        public AppHeartbeat(SystemStatusEvent status, AppStatusInfo[] meta)
        {
            Status = status;
            Meta = meta;
        }
    }

    
    public class AppStatusInfoProviderEvent : IRxn
    {
        public string Component = Guid.NewGuid().ToString();
        public string ReporterName;
        public Func<AppStatusInfo[]> Info;
    }
}
