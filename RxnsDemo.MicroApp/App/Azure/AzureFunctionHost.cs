using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Rxns.Collections;
using Rxns.Hosting;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using RxnsDemo.Micro.App;
using RxnsDemo.Micro.App.Api;
using RxnsDemo.Micro.App.Events;
using RxnsDemo.Micro.App.Models;

namespace Rxns.Azure
{
    /// <summary>
    /// this is a WIP spike into creating a generic adapter for rxns to scale out components or reactors onto an fully managed azure function
    /// </summary>
    public static class AzureFunctionHost
    {
        [FunctionName("SurveyProgressViewFnc")]
        public static async Task RunAsync([QueueTrigger("eventManagerSubscribe", Connection = "")] string rxn, [Queue("eventManagerPublish", Connection = "")] CloudQueue outputQueue, DiagnosticsTraceWriter log)
        {
            ReportStatus.Log.ReportToConsole();
            log.Trace(TraceLevel.Info, $"StudentProgressView SAW: {rxn}", null);

            var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());
            var result = await studentProgress.Process(rxn.FromJson<UserSurveyStartedEvent>()).ToTask();

            //can turn this into a eventManager, and use a rxncreator to start it up
            await outputQueue.AddMessageAsync(new CloudQueueMessage(result.ToJson()));
        }

        [FunctionName("SurveyProgressViewFncRxnIntegration")]
        public static async Task RunAsync([QueueTrigger("eventsink", Connection = "")] string rxn, ILogger log)
        {
            //need to work out a convention to dynmically generate this class ^^ and queue worker for a given reactor
            var microservice = new MicroServiceBoostrapperAspNetCore();

            //boostrap
            //todo: fix this code
            var cb = new ContainerBuilder();
            cb.Register(c => new AzureBackingChannel<IRxn>(new AzureCfg()
            {
                StorageConnectionString = ""
            }, c.Resolve<IComponentContext>().Resolve<IResolveTypes>()))
            .AsImplementedInterfaces()
            .SingleInstance();

            var root = new ContainerBuilder();
            //run app
            MicroServiceBoostrapperAspNetCore
                .CreateApp(root, microservice).SelectMany(a =>
                {                                          //// ^^^^^^^^^^^ this is my bright idea to reuse the args to create a targette reactor. need to implement "component" isolation, so we only startup a particular class! would also work for the "queueWorker" in process scaleout
                    return a.Run();
                })
                .Do(context =>
                {
                    "StudentProgress function ready to accept events".LogDebug();

                    var msg = rxn.GetTypeFromJson(context.Resolver);
                    $"Saw: {msg.Serialise()}".LogDebug();

                    context.RxnManager
                        .Publish //hmmm cant publish here? or do i need to setup a routing backing channel?
                        ((IRxn)rxn.Deserialise(msg));
                })
                .Until(ReportStatus.Log.OnError);

            //var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());
            //studentProgress.Process();

            log.LogInformation($"StudentProgressView processed message: {rxn}");
        }

        
    }
}
