using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(RxnsDemo.AzureB2C.RxnApps.RxnAppAzureFunc.Startup))]
namespace RxnsDemo.AzureB2C.RxnApps.RxnAppAzureFunc
{

    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddHttpClient();

            
        }
    }
}
