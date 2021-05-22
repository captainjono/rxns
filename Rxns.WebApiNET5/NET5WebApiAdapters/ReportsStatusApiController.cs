using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class ReportsStatusApiController : Controller, IReportStatus
    {
        private readonly ReportsStatus _rsImpl;

        public ReportsStatusApiController()
        {
            _rsImpl = new ReportsStatus(GetType().Name);
        }
        protected IActionResult BadRequest(Exception error)
        {
            var message = GetFriendlyError(error);

            var isMultiline = message.IndexOf("\r\n", StringComparison.OrdinalIgnoreCase);

            return StatusCode(StatusCodes.Status400BadRequest, message.Substring(0, isMultiline < 0 ? message.Length : isMultiline).Replace("-", "")); //dash in he reaonPhrase makes the htpClient the other side spew and translate to a 500 error);
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

        protected IActionResult BadRequest<T>(IDomainCommandResult<T> operation)
        {
            return BadRequest(operation.Error.Message ?? "unknown reason");
        }

        protected IActionResult Accepted<T>(IDomainCommandResult<T> operation, Uri location = null)
        {
            return new AcceptedResult(location, operation.Result);
        }

        protected IActionResult InternalServerError(Exception error)
        {
            var isMultiline = error.Message.IndexOf("\r\n", StringComparison.OrdinalIgnoreCase);
            return StatusCode(StatusCodes.Status500InternalServerError, error.Message.Substring(0, isMultiline < 0 ? 0 : isMultiline));
        }

        protected IActionResult Forbidden(string message)
        {
            return StatusCode(StatusCodes.Status403Forbidden, message);
        }

        protected bool UserCanAccessTenant(string tenant, string username)
        {
            return username.Equals(tenant, StringComparison.OrdinalIgnoreCase) || username.Equals("Admin");
        }

        protected string ClientIpAddress()
        {
            return GetRequestIP();
        }

        public IObservable<LogMessage<Exception>> Errors
        {
            get { return _rsImpl.Errors; }
        }

        public IObservable<LogMessage<string>> Information
        {
            get { return _rsImpl.Information; }
        }

        public string ReporterName
        {
            get { return GetType().Name; }
        }

        public void OnError(Exception exception)
        {
            _rsImpl.OnError(exception);
        }

        public void OnError(string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(exceptionMessage, args);
        }

        public void OnError(Exception innerException, string exceptionMessage, params object[] args)
        {
            _rsImpl.OnError(innerException, exceptionMessage, args);
        }

        public void OnInformation(string info, params object[] args)
        {
            _rsImpl.OnInformation(info, args);
        }

        public void OnWarning(string info, params object[] args)
        {
            _rsImpl.OnWarning(info, args);
        }

        public void OnVerbose(string info, params object[] args)
        {
            _rsImpl.OnVerbose(info, args);
        }

        public void OnDispose(IDisposable me)
        {
            _rsImpl.OnDispose(me);
        }

        private bool _isDisposed = false;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                _rsImpl.Dispose();
                base.Dispose();
            }
        }

        public string GetRequestIP(bool tryUseXForwardHeader = true)
        {
            string ip = null;

            // todo support new "Forwarded" header (2014) https://en.wikipedia.org/wiki/X-Forwarded-For

            // X-Forwarded-For (csv list):  Using the First entry in the list seems to work
            // for 99% of cases however it has been suggested that a better (although tedious)
            // approach might be to read each IP from right to left and use the first public IP.
            // http://stackoverflow.com/a/43554000/538763
            //
            if (tryUseXForwardHeader)
                ip = GetHeaderValueAs<string>("X-Forwarded-For").SplitCsv().FirstOrDefault();

            // RemoteIpAddress is always null in DNX RC1 Update1 (bug).
            if (ip.IsNullOrWhitespace() && HttpContext.Connection?.RemoteIpAddress != null)
                ip = HttpContext.Connection.RemoteIpAddress.ToString();

            if (ip.IsNullOrWhitespace())
                ip = GetHeaderValueAs<string>("REMOTE_ADDR");

            // _httpContextAccessor.HttpContext?.Request?.Host this is the local host.

            if (ip.IsNullOrWhitespace())
                throw new Exception("Unable to determine caller's IP.");

            return ip;
        }

        public T GetHeaderValueAs<T>(string headerName)
        {
            StringValues values;

            if (HttpContext?.Request?.Headers?.TryGetValue(headerName, out values) ?? false)
            {
                string rawValues = values.ToString();   // writes out as Csv when there are multiple.

                if (!rawValues.IsNullOrWhitespace())
                    return (T)Convert.ChangeType(values.ToString(), typeof(T));
            }
            return default(T);
        }

    }

    public static class IpAddressExtensions
    {

        public static List<string> SplitCsv(this string csvList, bool nullOrWhitespaceInputReturnsNull = false)
        {
            if (string.IsNullOrWhiteSpace(csvList))
                return nullOrWhitespaceInputReturnsNull ? null : new List<string>();

            return csvList
                .TrimEnd(',')
                .Split(',')
                .AsEnumerable<string>()
                .Select(s => s.Trim())
                .ToList();
        }
    }

}
