using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace RxnsDemo.AzureB2C.Rxns
{
    public interface IAppControllerToUrlLinks
    {
        Uri CreateLinkFor(Controller parent, string actionName, object parameters);
    }

    public class AspnetCoreControllerLinkProvider : IAppControllerToUrlLinks
    {
        public Uri CreateLinkFor(Controller parent, string actionName, object parameters)
        {
            return new Uri(parent.Url.Link(actionName, parameters));
        }
    }
}
