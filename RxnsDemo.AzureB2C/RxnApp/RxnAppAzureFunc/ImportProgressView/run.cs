using System;
using Microsoft.Azure.WebJobs;
using Rxns;
using Rxns.Logging;
using Rxns.NewtonsoftJson;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace RxnsDemo.AzureB2C.RxnApps.RxnAppAzureFunc.ImportProgress
{
    public class ImportProgressViewFunc : ReportsStatusApiController
    {
        private readonly ImportProgressView _view;

        public ImportProgressViewFunc(ImportProgressView view)
        {
            _view = view;
        }

        [FunctionName("ImportProgressView")]
        public void Run(string importEvent)
        {
            dynamic @event = importEvent.FromJson<dynamic>();
            $"running {importEvent}".LogDebug();

            ((IObservable<object>) _view.Process(@event)).Until();
        }
    }

}