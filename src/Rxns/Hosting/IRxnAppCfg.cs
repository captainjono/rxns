using System;
using System.Collections.Generic;
using System.Text;

namespace Rxns.Hosting
{
    public interface IRxnAppCfg
    {
        string[] Args { get; }

        string Version { get; set; }

        string AppPath { get; }
        string SystemName { get; set; }
        bool KeepUpdated { get; }
        string AppStatusUrl { get; }
    }
}
