using System.Net.Http;

namespace Rxns.Hosting.Compression
{
    public interface ICompressionHandler
    {
        bool Handles(string encodingType);
        HttpRequestMessage Decompress(HttpRequestMessage request);
        HttpResponseMessage Compress(HttpResponseMessage request);
    }
}
