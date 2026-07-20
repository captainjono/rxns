using System;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace Rxns.Azure
{
    public class AzureHelper
    {
        public static BlobRequestOptions GetRelabilityOptions()
        {
            return new BlobRequestOptions()
            {
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(2), 3),
                ServerTimeout = TimeSpan.FromMinutes(2),
                StoreBlobContentMD5 = true
            };
        }

        public static TraceSource GetEmulatorLogger(string name)
        {
            var traceSource = new TraceSource(name, SourceLevels.All);
            
            traceSource.Listeners.Add(
                Activator.CreateInstance(
                    Type.GetType("Microsoft.ServiceHosting.Tools.DevelopmentFabric.Runtime.DevelopmentFabricTraceListener, Microsoft.ServiceHosting.Tools.DevelopmentFabric.Runtime, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35")) as TraceListener);
            return traceSource;
        }

    }
}
