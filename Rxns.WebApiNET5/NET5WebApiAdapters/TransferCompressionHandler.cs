using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Rxns.Hosting;
using Rxns.WebApi.Compression;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
{
    public class TransferCompressionHandler : HttpClientHandler
    {
        public Collection<ICompressor> Compressors;

        public override bool SupportsAutomaticDecompression
        {
            get { return false; }
        }

        public TransferCompressionHandler()
        {
            Compressors = new Collection<ICompressor>();
            Compressors.Add(new GZipCompressor());
        }

        protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            //request.Headers.TransferEncoding.Add(new TransferCodingHeaderValue("gzip"));
            //request.Content = new CompressedContent(request.Content, new GZipCompressor());
            //request.Content.Headers.ContentEncoding.Add("gzip");

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Content.Headers.ContentEncoding.AnyItems())
            {
                var encoding = response.Content.Headers.ContentEncoding.First();

                var compressor = Compressors.FirstOrDefault(c => c.EncodingType.Equals(encoding, StringComparison.OrdinalIgnoreCase));

                if (compressor != null)
                {
                    response.Content = DecompressContent(response.Content, compressor);
                }
            }

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
