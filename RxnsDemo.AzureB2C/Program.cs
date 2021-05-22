using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns;
using Rxns.DDD;
using Rxns.DDD.Sql;
using Rxns.DDD.Tenant;
using Rxns.Health.AppStatus;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;
using Rxns.Playback;
using Rxns.Scheduling;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using RxnsDemo.AzureB2C.RxnApps;
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

            Console.ReadLine();
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
                            .RunsTask<TenantSqlTask>()
                            .RunsTask<SqlTask>()
                            .CreatesOncePerApp<SqlDatabaseConnection>()
                            .Includes<RxnsTenantDDDModule>()
                            .CreatesOncePerApp(() => new AggViewCfg()
                            {
                                ReportDir = "reports"
                            })
                            .CreatesOncePerApp(() => new AppServiceRegistry()
                            {
                                AppStatusUrl = "http://localhost:888",
                            })
                            .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                            .Includes<AspNetCoreWebApiAdapterModule>()


                            .Includes<ImportUserModule>()

                            .CreatesOncePerApp<AzureB2CToLegacyDbProcessor>()
                            .CreatesOncePerApp<LegacyDbCurrentTenantAndUserEventSourcedCache>()

                            .CreatesOncePerApp<LegacyDbCfg>()
                            .CreatesOncePerApp<UseDeserialiseCodec>()
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
            
            //}, c.Resolve<IComponentContext>().Resolve<IResolveTypes>()))
        }
    }
}
