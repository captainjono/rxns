using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Rxns.Hosting.Compression;

namespace Rxns.WebApi.Compression
{
    public class CompressedRequestOrResponseHandler : DelegatingHandler
    {
        private readonly ICompressionHandler[] _handlers;

        public CompressedRequestOrResponseHandler(ICompressionHandler[] handlers)
        {
            _handlers = handlers;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content.Headers.ContentEncoding.AnyItems())
                foreach (var compressionHandler in _handlers)
                {
                    if (compressionHandler.Handles(request.Content.Headers.ContentEncoding.FirstOrDefault()))
                    {
                        request = compressionHandler.Decompress(request);
                        break;
                    }
                }

            return base.SendAsync(request, cancellationToken).ContinueWith(response =>
            {
                var compressedResponse = response.Result;

                if (compressedResponse.Content != null)
                    foreach (var compressionHandler in _handlers)
                    {
                        foreach (var encoding in request.Headers.AcceptEncoding)
                            if (compressionHandler.Handles(encoding.Value))
                            {
                                return compressionHandler.Compress(response.Result);
                            }
                    }

                return compressedResponse;
            }, cancellationToken);
        }

    }
}
