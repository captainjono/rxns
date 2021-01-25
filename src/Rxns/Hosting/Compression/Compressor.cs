using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Rxns.Hosting;

namespace Rxns.WebApi.Compression
{
    public abstract class Compressor : ICompressor
    {
        public abstract string EncodingType { get; }
        public abstract Stream CreateCompressionStream(Stream output);
        public abstract Stream CreateDecompressionStream(Stream input);

        public virtual Task Compress(Stream source, Stream destination)
        {
            var compressed = CreateCompressionStream(destination);

            using (compressed)
            {
                source.CopyTo(compressed);
            }
            
            if (destination.CanSeek)
                destination.Seek(0, SeekOrigin.Begin);

            return destination.ToResult();
        }

        public virtual Task Decompress(Stream source, Stream destination)
        {
            var decompressed = CreateDecompressionStream(source);

            using (decompressed)
            {
                decompressed.CopyTo(destination);
            }


            if (destination.CanSeek)
                destination.Seek(0, SeekOrigin.Begin);

            return destination.ToResult();
        }
    }
}
