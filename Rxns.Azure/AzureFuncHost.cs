using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Rxns.Collections;
using Rxns.Interfaces;
using Rxns.Logging;
using Rxns.NewtonsoftJson;

namespace Rxns.Azure
{
    /// <summary>
    /// this is a WIP spike into creating a generic adapter for rxns to scale out components or reactors onto an fully managed azure function
    /// </summary>
    public static class AzureFunctionHost
    {
        [FunctionName("SurveyProgressViewFnc")]
        public static async Task RunAsync([QueueTrigger("eventsink", Connection = "rxns")] string rxn, [Queue("eventsinkPub", Connection = "rxns")] CloudQueue outputQueue, DiagnosticsTraceWriter log)
        {
            //ReportStatus.Log.ReportToConsole();
            //log.Trace(TraceLevel.Info, $"StudentProgressView SAW: {rxn}", null);

            //var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());
            //var result = await studentProgress.Process(rxn.FromJson<UserSurveyStartedEvent>()).ToTask();

            ////can turn this into a eventManager, and use a rxncreator to start it up
            //await outputQueue.AddMessageAsync(new CloudQueueMessage(result.ToJson()));
            $"SurveyProgressViewFnc processed message: {rxn}".LogDebug();

        }

        [FunctionName("SurveyProgressViewFncRxnIntegration")]
        public static async Task RunAsync([QueueTrigger("eventsink", Connection = "rxns")] string rxn, ILogger log)
        {


            //need to work out a convention to dynmically generate this class ^^ and queue worker for a given reactor
            //var microservice = new MicroServiceBoostrapperAspNetCore();

            ////boostrap
            ////todo: fix this code
            //var cb = new ContainerBuilder();
            //cb.Register(c => new AzureBackingChannel<IRxn>(new AzureCfg()
            //{
            //    StorageConnectionString = "DefaultEndpointsProtocol=https;AccountName=rxns;AccountKey=MlxQL7N/9eMvm2vdAwiKmzPTda5GycIDE+WyCKxmkb+83OQztFf03o057yq8G1cb5AcfRHaQTBzdBnBS7/Temg==;EndpointSuffix=core.windows.net"
            //}, c.Resolve<IComponentContext>().Resolve<IResolveTypes>()))
            //.AsImplementedInterfaces()
            //.SingleInstance();


            ////run app
            //microservice
            //    .CreateApp(new ContainerBuilder(), args: "reactor SurveyProgressView".Split(' ')).SelectMany(a =>
            //    {                                          //// ^^^^^^^^^^^ this is my bright idea to reuse the args to create a targette reactor. need to implement "component" isolation, so we only startup a particular class! would also work for the "queueWorker" in process scaleout
            //        return a.Run();
            //    })
            //    .Do(context =>
            //    {
            //        "StudentProgress function ready to accept events".LogDebug();

            //        var msg = rxn.GetTypeFromJson(context.Resolver);
            //        $"Saw: {msg.Serialise()}".LogDebug();

            //        context.RxnManager
            //            .Publish //hmmm cant publish here? or do i need to setup a routing backing channel?
            //            ((IRxn)rxn.Deserialise(msg));
            //    })
            //    .Until(ReportStatus.Log.OnError);

            //var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());
            //studentProgress.Process();

            $"StudentProgressView processed message: {rxn}".LogDebug();
        }


    }
}
