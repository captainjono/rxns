using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive;
using System.Threading.Tasks;
using Rxns.WebApi.Compression;

namespace Rxns.Hosting.Compression
{
    public class CompressionHandler : ICompressionHandler
    {
        private readonly Compressor _handler;

        public CompressionHandler(Compressor handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage Decompress(HttpRequestMessage request)
        {
            // Read in the input stream, then decompress in to the outputstream.
            // Doing this asynronously, but not really required at this point
            // since we end up waiting on it right after this.
            var outputStream = new MemoryStream();
            request.Content.ReadAsStreamAsync().ContinueWith(t =>
            {
                var inputStream = t.Result;
                using (request.Content)
                using (var gzipStream = _handler.CreateDecompressionStream(inputStream))
                {
                    gzipStream.CopyTo(outputStream);
                    outputStream.Seek(0, SeekOrigin.Begin);
                }

            }).Wait();

            // Replace request content with the newly decompressed stream
            var newContent = new StreamContent(outputStream);

            // Copy all headers from original content in to new one
            foreach (var header in request.Content.Headers)
            {
                newContent.Headers.Add(header.Key, header.Value);
            }

            request.Content = newContent;

            return request;
        }

        public HttpResponseMessage Compress(HttpResponseMessage request)
        {
            request.Content = new CompressedContent(request.Content, _handler);

            return request;
        }

        public bool Handles(string encodingType)
        {
            return _handler.EncodingType.Equals(encodingType, StringComparison.OrdinalIgnoreCase);
        }
    }

    //todo: move into common web lib
    public class CompressedContent : HttpContent
    {
        private readonly HttpContent content;
        private readonly ICompressor compressor;

        public CompressedContent(HttpContent content, ICompressor compressor)
        {
            Ensure.Argument.NotNull(content, "content");
            Ensure.Argument.NotNull(compressor, "compressor");

            this.content = content;
            this.compressor = compressor;

            AddHeaders();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            Ensure.Argument.NotNull(stream, "stream");

            using (content)
            {
                var contentStream = content.ReadAsStreamAsync().WaitR();
                compressor.Compress(contentStream, stream).Wait();
            }

            return true.ToResult();
        }

        private void AddHeaders()
        {
            foreach (var header in content.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Headers.ContentEncoding.Add(compressor.EncodingType);
        }
    }
}
