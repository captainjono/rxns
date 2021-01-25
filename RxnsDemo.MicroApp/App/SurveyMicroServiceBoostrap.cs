using System;
using System.Web.Http;
using Autofac;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Rxns;
using Rxns.Collections;
using Rxns.DDD.BoundedContext;
using Rxns.Hosting;
using Rxns.Hosting.Cluster;
using Rxns.Logging;
using Rxns.Metrics;
using Rxns.NewtonsoftJson;
using Rxns.WebApi;
using Rxns.WebApi.MsWebApiAdapters;
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
        static void Main(string[] args)
        {
            "Configuring App".LogDebug();

            //var functionApp = Rxn.Create(() => { "Anything can be in here".LogDebug(); }).ToRxnApp();
            //var functionRxnApp = functionApp.WithRxns(new ContainerBuilder().ToRxnDef());

            var apiCfg = new WebApiCfg()
            {
                BindingUrl = "http://+:888/",
                Html5IndexHtml = "index.html",
                Html5Root = @"..\..\..\Rxns.AppSatus\Web\dist\" //the rxns appstatus portal
            };

            var rxnApp = HostSurveyDomainFeatureModule(apiCfg.BindingUrl).ToRxns().UseWindowsAddons();           
            var appInfo = new AppVersionInfo("Survey Micro Service", "1.0", true);
            var webApiHost = new WebApiHost(apiCfg, new OwinWebApi2Adapter(cfg => cfg.MakeJsonOnly())); //todo: support AspNet .Net5
            //var consoleHost = new ConsoleHostedApp(); // for unit/testing
            //var reliableHost = new RxnSupervisorHost(...); //will automatically reboot your app on failure. "always on"

            "Launching App in WebApi host".LogDebug();

            rxnApp
                .Named(appInfo)
                .OnHost(webApiHost, new RxnAppCfg()) // the apps supervisor
                .Subscribe(rxnAppContext =>
                {

                    //send an error report to the appstatus portal
                    //Rxn.In(TimeSpan.FromSeconds(1)).Do(_ =>
                    //{
                    //    GeneralLogging.Log.OnError("Some error has happened");
                    //}).Subscribe();

                    //called each time the app starts

                    //can use this context to interact with the Micro.App
                    // rxnAppContext.Installer - Installs the app
                    // rxnAppContext.CmdService - CQRS integration service
                    // rxnAppContext.RxnManager - Event-driven abstraction

                    ////authenticate with local token server
                    //var authRequest = new HttpClient();
                    //var user = "cool";
                    //var pass = "dude";
                    //var clientId = "myApp";

                    //Rxn.In(TimeSpan.FromSeconds(2), true).Do(_ =>
                    //{
                    //    "Authenticating".LogDebug();

                    //    var result = authRequest.PostAsync(
                    //        "http://localhost:888/token", 
                    //        new StringContent($"grant_type=password&username={user}&password={pass}&client_id={clientId}")
                    //        ).Result;

                    //    var token = result.Content.ReadAsAsync<UserAccessToken>().WaitR();
                    //    $"Auth result {token.ToJson()}".LogDebug();

                    //}).Subscribe();


                });
                //later to end app
                //.Dispose();
                
            Console.WriteLine("Press anykey to terminate");
            Console.ReadLine();
        }

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
                .CreatesOncePerApp(() => new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>()))
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
                    AppStatusUrl = appStatusUrl
                })
                .CreatesOncePerApp<INSECURE_SERVICE_DEBUG_ONLY_MODE>()
                //setup OS abstractions
                //test sim to exercise api
                //.CreatesOncePerApp<Basic30UserSurveySimulation>()
                //serilaisation of models
                .CreatesOncePerApp<UseDeserialiseCodec>()
                .CreatesOncePerApp(_ => new AutoScaleoutReactorPlan(new ScaleoutToEverySpareReactor(), "ReplayMacOS", version: "Latest"))
                ;

            //setup static object.Serialise() & string.Deserialise() methods
            RxnExtensions.DeserialiseImpl = (t, json) => JsonExtensions.FromJson(json, t);
                RxnExtensions.SerialiseImpl = (json) => JsonExtensions.ToJson(json);
        };
    }

    public static class WebApiExtensions
    {
        /// <summary>
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static HttpConfiguration MakeJsonOnly(this HttpConfiguration config)
        {
            //remove XML WS 
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            //add json
            var json = config.Formatters.JsonFormatter;
            json.SerializerSettings.Formatting = Formatting.Indented;
            json.SerializerSettings.TypeNameHandling = TypeNameHandling.Auto;
            json.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
            json.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            config.Formatters.Remove(config.Formatters.JsonFormatter);
            config.Formatters.Add(json);

            return config;
        }
    }
}


