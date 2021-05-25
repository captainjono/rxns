using System;
using System.Net.NetworkInformation;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Rxns;
using Rxns.Azure;
using Rxns.Cloud;
using Rxns.Collections;
using Rxns.DDD.BoundedContext;
using Rxns.Health;
using Rxns.Hosting;
using Rxns.Hosting.Cluster;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;
using Rxns.WebApi;
using Rxns.WebApiNET5;
using Rxns.WebApiNET5.NET5WebApiAdapters;
using Rxns.WebApiNET5.NET5WebApiAdapters.RxnsApiAdapters;
using Rxns.WebApiNET5.NET5WebApiAdapters.System.Web.Http;
using Rxns.Windows;
using RxnsDemo.Micro.App.AggRoots;
using RxnsDemo.Micro.App.Api;
using RxnsDemo.Micro.App.Cmds;
using RxnsDemo.Micro.App.Events;
using RxnsDemo.Micro.App.Models;
using RxnsDemo.Micro.App.Qrys;

namespace RxnsDemo.Micro.App
{
    public class SurveyMicroService
    {

        [STAThread]
        static async Task Main(string[] args)
        {
            ReportStatus.StartupLogger = ReportStatus.Log.ReportToConsole();
            
            "Configuring App".LogDebug();
            
            await AspNetCoreWebApiAdapter.StartWebServices<MicroServiceBoostrapperAspNetCore>(MicroServiceBoostrapperAspNetCore.Cfg, args);
               
            Console.WriteLine("Press anykey to terminate");
            Console.ReadLine();
        }
    }

    public class MicroServiceBoostrapperAspNetCore : ConfigureAndStartAspnetCore
    {
        public MicroServiceBoostrapperAspNetCore()
        {
        }

        public override Func<string, Action<IRxnLifecycle>> App { get; } = url => HostSurveyDomainFeatureModule(url);
        public override IRxnAppInfo AppInfo { get; } = new AppVersionInfo("Survey Micro Service", "1.0", true);

        public override IWebApiCfg WebApiCfg => Cfg;

        public static IWebApiCfg Cfg { get; set; } = new WebApiCfg()
        {
            BindingUrl = "http://*:888",
            Html5IndexHtml = "index.html",
            Html5Root = @"C:\jan\Rxns\Rxns.AppSatus\Web\dist"// @"/Users/janison/rxns/Rxns.AppSatus/Web/dist/" //the rxns appstatus portal
        };

        //todo:
        //create supervir which uses the CPU stats to suggest increasing the process count (scale signals)
        //such as max file handlers per process is around ~16m

        //log sample 20 as an appcommand should return the last 20 log messages
        //we can enable and disable logs via a static global prop
        public static Func<string, Action<IRxnLifecycle>> HostSurveyDomainFeatureModule = appStatusUrl => SurveyRoom =>
        {
            SurveyRoom
                //the services to the api
                .CreatesOncePerApp<SurveyAnswersDomainService>()
                //.CreatesOncePerApp(() => new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>()))
                .CreatesOncePerApp<Func<ISurveyAnswer, string>>(_ => s => $"{s.userId}%{s.AttemptId}")
                .CreatesOncePerApp<TapeArrayTenantModelRepository<SurveyAnswers, ISurveyAnswer>>()
                //api
                .RespondsToCmd<BeginSurveyCmd>()
                .RespondsToCmd<RecordAnswerForSurveyCmd>()
                .RespondsToCmd<FinishSurveyCmd>()
                .RespondsToQry<LookupProgressInSurveyQry>()
                //events
                .Emits<UserAnsweredQuestionEvent>()
                .Emits<UserSurveyStartedEvent>()
                .Emits<UserSurveyEndedEvent>()
                //cfg specific
                .CreatesOncePerApp(() => new AggViewCfg()
                {
                    ReportDir = "reports"
                })
                .CreatesOncePerApp(() => new AppServiceRegistry()
                {
                    AppStatusUrl = "http://localhost:888"
                })
                .CreatesOncePerApp<RxnDebugLogger>()
                
                .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                //setup OS abstractions
                //test sim to exercise api
                //.CreatesOncePerApp<Basic30UserSurveySimulation>()
                //serilaisation of models
                .CreatesOncePerApp<UseDeserialiseCodec>()
                //.CreatesOncePerApp(_ => new AutoScaleoutReactorPlan(new ScaleoutToEverySpareReactor(), "ReplayMacOS", version: "Latest"))
                .CreatesOncePerApp(_ => new DynamicStartupTask((r, c) =>
                {
                    //any other app startup logic can go here
                    var evenBusToAzureFunc = new RxnManager<IRxn>(new AzureBackingChannel<IRxn>(new AzureCfg()
                    {
                        StorageConnectionString = ""
                    }, c.Resolve<IComponentContext>().Resolve<IResolveTypes>(), publishOnlyMode: true));

                    TimeSpan.FromSeconds(10).Then(true).Do(_ =>
                    {
                        "publishing event to azure".LogDebug();
                        evenBusToAzureFunc.Publish(new AppHeartbeat()).WaitR();

                    }).Until(ReportStatus.Log.OnError);
                }))
                ;

            //setup static object.Serialise() & string.Deserialise() methods
            RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
            RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);
        };
    }
}


