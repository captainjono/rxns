//using System.Net;
//using System.Net.Http;
//
//using System.Web.Http.Controllers;
//using System.Web.Http.Filters;
//using Microsoft.AspNetCore.Mvc.Filters;

//namespace Rxns.WebApi
//{
//    public class ValidateMimeMultipartContentFilter : ActionFilterAttribute
//    {
//        public override void OnActionExecuting(actioneq actionContext)
//        {
//            if (!actionContext.Request.Content.IsMimeMultipartContent())
//            {
//                throw new HttpResponseException(HttpStatusCode.UnsupportedMediaType);
//            }
//        }

//        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
//        {

//        }
//    }
//}
