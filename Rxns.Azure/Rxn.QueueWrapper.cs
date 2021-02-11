using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Rxns.Collections;
using Rxns.NewtonsoftJson;
using RxnsDemo.Micro.App.Api;
using RxnsDemo.Micro.App.Events;
using RxnsDemo.Micro.App.Models;

namespace Rxns.Azure
{
    public static class RxnQueueWrapper
    {
        [FunctionName("Rxn_QueueWrapper")]
        public static async Task RunAsync([ServiceBusTrigger("myqueue", Connection = "")] string myQueueItem, ILogger log)
        {
            var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());

            studentProgress.Process(myQueueItem.FromJson<UserSurveyStartedEvent>());
            
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
        }
        
            [FunctionName("SurveyProgressView")]
            public static async Task RunAsync([QueueTrigger("eventManagerSubscribe", Connection = "")]string myQueueItem, [Queue("eventManagerPublish", Connection = "")]CloudQueue outputQueue, DiagnosticsTraceWriter log)
            {
                log.Trace(TraceLevel.Info, $"StudentProgressView SAW: {myQueueItem}", null);
                
                var studentProgress = new SurveyProgressView(new DictionaryKeyValueStore<string, SurveyProgressModel>());
                var result = await studentProgress.Process(myQueueItem.FromJson<UserSurveyStartedEvent>()).ToTask();
                
                //can turn this into a eventManager, and use a rxncreator to start it up
                await outputQueue.AddMessageAsync(new CloudQueueMessage(result.ToJson()));
            }
        }
    }
}