using System;

namespace Rxns.Hosting
{
    public class HttpClientCfg
    {
        public bool EnableCompression { get; set; }
        public TimeSpan TotalTransferTimeout { get; set; }
    }
}
