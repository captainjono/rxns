using System;
using System.Net.Http;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public static class HttpClientExtensions
    {

            private static string TimeoutPropertyKey = "RequestTimeout";

            public static void SetTimeout(
                this HttpRequestMessage request,
                TimeSpan? timeout)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                request.Properties[TimeoutPropertyKey] = timeout;
            }

            public static TimeSpan GetTimeout(this HttpRequestMessage request, TimeSpan ifNull)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                if (request.Properties.TryGetValue(
                        TimeoutPropertyKey,
                        out var value)
                    && value is TimeSpan timeout)
                    return timeout;
                return ifNull;
            }
        

        public static string AsNormalisedUrl(this string url)
        {
            return url.Replace(" ", "");
        }

        public static Task<HttpResponseMessage> PostAsJsonAsync(this HttpClient client, string route, object content, bool useIsoDates = true)
        {
            return client.PostAsync(route, content.ToJsonContent(useIsoDates));
        }

        public static Task<HttpResponseMessage> PostAsMultiPart(this HttpClient client, string route, params IFileMeta[] files)
        {
            var multiPart = new MultipartFormDataContent();

            foreach (var file in files)
                multiPart.Add(new StreamContent(file.Contents), file.Name, file.Name);

            return client.PostAsync(route, multiPart);
        }

        public static HttpContent ToJsonContent(this object content, bool useIsoDates = true)
        {
            var json = JsonConvert.SerializeObject(content, new JsonSerializerSettings()
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = useIsoDates ? DateFormatHandling.IsoDateFormat : DateFormatHandling.MicrosoftDateFormat
            });

            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        //public static CompressedContent WithCompression(this HttpContent content)
        //{
        //    var compressed = new CompressedContent(content, new GZipCompressor());

        //    return compressed;
        //}

        public static IObservable<T> ReadAsJson<T>(this IObservable<HttpResponseMessage> response)
        {
            return Rxn.Create<T>(o =>
            {
                return response.Subscribe(msg =>
                {
                    msg.Content.ReadAsStringAsync().ToObservable().Subscribe(result =>
                    {
                        try
                        {
                            var content = JsonConvert.DeserializeObject<T>(result);
                            o.OnNext(content);
                        }
                        catch (Exception e)
                        {
                            o.OnError(e);
                        }
                        finally
                        {
                            o.OnCompleted();
                        }
                    },
                    error => o.OnError(error));

                }, error => o.OnError(error));
            });
        }
    }
}
