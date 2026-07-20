using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Web.Http;
using System.Web.Http.Results;
using Rxns.CQRS;
using Rxns.DDD.CQRS;

namespace Rxns.WebApi
{
    public class DomainCommandApiController : ReportsStatusApiController
    {
        protected IHttpActionResult BadRequest<T>(IDomainCommandResult<T> operation)
        {
            var result = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = Request,
                ReasonPhrase = operation.Error.Message ?? "unknown reason"
            };

            return new ResponseMessageResult(result);
        }

        protected IHttpActionResult Accepted<T>(IDomainCommandResult<T> operation, Uri location = null)
        {
            var result = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                RequestMessage = Request,
                Content = new ObjectContent(operation.Result.GetType(), operation.Result, new JsonMediaTypeFormatter())
            };

            result.Headers.Add("Access-Control-Expose-Headers", "Location");

            if (location != null) result.Headers.Location = location;

            return new ResponseMessageResult(result);
        }

        protected IHttpActionResult BadRequest(Exception error)
        {
            var message = GetFriendlyError(error);

            var isMultiline = message.IndexOf("\r\n", StringComparison.OrdinalIgnoreCase);
            var result = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                RequestMessage = Request,
                ReasonPhrase = message.Substring(0, isMultiline < 0 ? message.Length : isMultiline).Replace("-", "") //dash in he reaonPhrase makes the htpClient the other side spew and translate to a 500 error
            };
            
            return new ResponseMessageResult(result);
        }

        private string GetFriendlyError(Exception error)
        {
            var domainError = error as DomainCommandException;
            if (domainError != null)
            {
                return domainError.DomainMessage ?? domainError.Message;
            }

            return error.Message;
        }

        protected new IHttpActionResult InternalServerError(Exception error)
        {
            var isMultiline = error.Message.IndexOf("\r\n", StringComparison.OrdinalIgnoreCase);
            var result = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                RequestMessage = Request,
                ReasonPhrase = error.Message.Substring(0, isMultiline < 0 ? 0 : isMultiline)
            };

            return new ResponseMessageResult(result);
        }

        protected IHttpActionResult Forbidden(string message)
        {
            return new ResponseMessageResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = message });
        }

        protected bool UserCanAccessTenant(string tenant, string username)
        {
            return username.Equals(tenant, StringComparison.OrdinalIgnoreCase) || username.Equals("Admin");
        }
    }
}
