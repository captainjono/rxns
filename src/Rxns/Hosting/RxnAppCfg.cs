using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Rxns.NewtonsoftJson;

namespace Rxns.Hosting
{
    public class RxnAppCfg : IRxnAppCfg
    {
        public string[] Args { get; set; } = new string[0];
        public string Version { get; set; }
        public string AppPath { get; set; }

        public static string CfgName { get; } = "rxn.cfg";
        public string SystemName { get; set; }
        public bool KeepUpdated { get; set; } = true;
        public string AppStatusUrl { get; set; }

        public static RxnAppCfg Detect(string[] args, string cfgName = null)
        {
            var cfg = LoadCfg(cfgName ?? CfgName);

            if (args != null)
                cfg.Args = args;

            return cfg;
        }



        public static RxnAppCfg LoadCfg(string cfgFile)
        {
            if (File.Exists(cfgFile))
            {
                return File.ReadAllText(cfgFile).FromJson<RxnAppCfg>();
            }

            return new RxnAppCfg();
        }
    }

    public static class RxnCfgExt
    {
        public static T Save<T>(this T cfg, string location = null) where T : IRxnAppCfg
        {
            File.WriteAllText(location != null ? Path.Combine(location, RxnAppCfg.CfgName) : RxnAppCfg.CfgName, cfg.ToJson());

            return cfg;
        }
    }




}
