using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Rxns.DDD.CQRS;
using Rxns.Health;

namespace Rxns.Hosting.Compression
{

    public class TransferCompressionHandler : HttpClientHandler
    {
        public ICompressor[] Compressors;
        private readonly MonitorAction<HttpRequestMessage> _timeTransfer;
        private readonly MonitorAction<HttpRequestMessage> _endTransfer;

        public override bool SupportsAutomaticDecompression => false;

        public TransferCompressionHandler(IRxnHealthManager health, HttpClientCfg cfg, params ICompressor[] handlers)
        {
            var watcher = new ElapsedTimePulsar<HttpRequestMessage>("HttpReq", h => h.GetTimeout(cfg.TotalTransferTimeout), h => health.Publish(h));
            _timeTransfer = watcher.Before();
            _endTransfer = watcher.After();

            Compressors = handlers ?? new ICompressor[] { };
        }

        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            //request.Headers.TransferEncoding.Add(new TransferCodingHeaderValue("gzip"));
            //request.Content = new CompressedContent(request.Content, new GZipCompressor());
            //request.Content.Headers.ContentEncoding.Add("gzip");

            _timeTransfer.Do(request);

            var response = await base.SendAsync(request, cancellationToken);

            _endTransfer.Do(request);

            if (!response.Content.Headers.ContentEncoding.AnyItems()) return response;
            var encoding = response.Content.Headers.ContentEncoding.First();

            var compressor = Compressors.FirstOrDefault(c => c.EncodingType.Equals(encoding, StringComparison.OrdinalIgnoreCase));

            if (compressor != null)
            {
                response.Content = DecompressContent(response.Content, compressor);
            }

            //todo: publish a compression overhead event

            return response;
        }

        private static HttpContent DecompressContent(HttpContent compressedContent, ICompressor compressor)
        {
            using (compressedContent)
            {
                var decompressed = new MemoryStream();
                compressor.Decompress(compressedContent.ReadAsStreamAsync().Result, decompressed).Wait();
                var newContent = new StreamContent(decompressed);

                compressedContent.Headers.ForEach(h =>
                {
                    if (!h.Key.Equals("Content-Encoding"))
                        newContent.Headers.Add(h.Key, h.Value);
                });
                // copy content type so we know how to load correct formatter

                return newContent;
            }
        }
    }
}
