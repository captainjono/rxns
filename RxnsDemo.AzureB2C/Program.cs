using System;
using System.Reactive;
using System.Reactive.Linq;
using Rxns;
using Rxns.Hosting;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using Rxns.Scheduling;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using RxnsDemo.AzureB2C.Rxns;

namespace RxnsDemo.AzureB2C
{
    class Program
    {
        static void Main(string[] args)
        {
            ReportStatus.StartupLogger = ReportStatus.Log.ReportToConsole();

            "Configuring App".LogDebug();

            AzureB2CWebApiAdapter.Cfg = new WebApiCfg()
            {
                BindingUrl = "http://*:888",
                Html5IndexHtml = "index.html",
                Html5Root =
                    @"C:\jan\Rxns\Rxns.AppSatus\Web\dist" // @"/Users/janison/rxns/Rxns.AppSatus/Web/dist/" //the rxns appstatus portal
            };
            
            AspNetCoreWebApiAdapter.StartWebServices<AzureB2CWebApiAdapter>(AzureB2CWebApiAdapter.Cfg).ToObservable()
                .LastAsync()
                .Select(_ => new Unit())
                .Until();
        }

        public class AzureB2CWebApiAdapter : ConfigureAndStartAspnetCore
        {
            public static IWebApiCfg Cfg { get; set; }
            public override Func<string, Action<IRxnLifecycle>> App { get; } = url =>
            {
                RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);

                return lifecycle => RxnApp.SpareReator(url)( 
                    lifecycle
                        .CreatesOncePerApp<AzureB2CToLegacyDbProcessor>()
                        .CreatesOncePerApp<LegacyDbCfg>()
                        .RunsTask<TenantSqlTask>()
                        .RunsTask<SqlTask>()
                        .CreatesOncePerApp<SqlDatabaseConnection>()
                        .Includes<RxnsTenantDDDModule>()
                    );
            };

            public override IRxnAppInfo AppInfo => new ClusteredAppInfo("AzureB2CAdapter", "1.0.0", new string[0], false);

            public override IWebApiCfg WebApiCfg => Cfg;
        }

        public class LegacyDbCfg : ITenantDatabaseConfiguration
        {
            public string SqlServer { get; } = "weqpc";
            public string SqlUsername { get; } = "weq";
            public string SqlPassword { get; } = "qew";
            public string DbNameFormat { get; } = "learningm";
        }

    }

    public class AzureB2CNewUserTrigger
    {
        public void Go()
        {
            //cb.Register(c => new AzureBackingChannel<IRxn>(new AzureCfg()
            //{
            //    StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=rxns;AccountKey=MlxQL7N/9eMvm2vdAwiKmzPTda5GycIDE+WyCKxmkb+83OQztFf03o057yq8G1cb5AcfRHaQTBzdBnBS7/Temg==;EndpointSuffix=core.windows.net"
            //}, c.Resolve<IComponentContext>().Resolve<IResolveTypes>()))
        }
    }
}
