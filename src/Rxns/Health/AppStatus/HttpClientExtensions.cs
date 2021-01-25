//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Reactive.Linq;
//using System.Reactive.Threading.Tasks;
//using System.Text;
//using System.Threading.Tasks;
//using Microsoft.Win32;
//using Rxns;
//using Rxns.Interfaces;
//using Rxns.WebApi.Server.IO;

//namespace Janison.Micro
//{
//    public static class HttpCslientExtensions
//    {
//        public const string DEFAULT_CONTENTTYPE = "application/octet-stream";

//        //make cross-paltform, expose static GetContentTypeImpl
//        public static string GetContentType(string filename)
//        {
//            string result;
//            RegistryKey key;
//            object value;

//            var extension = Path.GetExtension(filename);

//            key = Registry.ClassesRoot.OpenSubKey("MIME\\Database\\Content Type", false);
//            try
//            {
//                var type = key.GetSubKeyNames().FirstOrDefault(contentType =>
//                {
//                    var possible = key.OpenSubKey(contentType).GetValue("Extension");
//                    return possible != null && possible.ToString().BasicallyEquals(extension);
//                });

//                return type != null ? type : DEFAULT_CONTENTTYPE;
//            }
//            catch (Exception e)
//            {
//                return DEFAULT_CONTENTTYPE;
//            }
//        }

//        public static string ContentType(this string filename)
//        {
//            return GetContentType(filename);
//        }

//        public static string AsNormalisedUrl(this string url)
//        {
//            return url.Replace(" ", "");
//        }
//        public static bool IsNullOrWhitespace(this string str)
//        {
//            return String.IsNullOrWhiteSpace(str);
//        }
//        public static string IsNullOrWhitespace(this string str, string returnThis)
//        {
//            return str.IsNullOrWhitespace() ? returnThis : str;
//        }

        

//        public static HttpContent AsStreamContent(this IFileMeta file)
//        {
//            var content = new StreamContent(file.Contents);
//            content.Headers.ContentType = new MediaTypeHeaderValue(!file.ContentType.IsNullOrWhitespace() ? file.ContentType : file.Name.ContentType());
//            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
//            content.Headers.ContentMD5 = file is FileMetaPart ? (file as FileMetaPart).Md5 : file.Hash == null ? new byte[0] : file.Hash.FromHash();
//            content.Headers.ContentDisposition.FileName = file.Name;
//            content.Headers.ContentDisposition.FileNameStar = file.Name;
//            content.Headers.ContentDisposition.Size = file.Length;
//            content.Headers.ContentDisposition.CreationDate = file.LastWriteTime.ToUniversalTime();

//            return content;
//        }

//        public static Task<HttpResponseMessage> PostAsJsonAsync(this HttpClient client, string route, object content, bool useIsoDates = true)
//        {
//            return client.PostAsync(route, content.ToJsonContent(useIsoDates));
//        }

//        public static Task<HttpResponseMessage> PostAsMultiPart(this HttpClient client, string route, params IFileMeta[] files)
//        {
//            var multiPart = new MultipartFormDataContent();

//            foreach (var file in files)
//                multiPart.Add(new StreamContent(file.Contents), file.Name, file.Name);

//            return client.PostAsync(route, multiPart);
//        }

//        public static HttpContent ToJsonContent(this object content, bool useIsoDates = true)
//        {
//            return new StringContent(content.Serialise(), Encoding.UTF8, "application/json");
//        }

//        //public static CompressedContent WithCompression(this HttpContent content)
//        //{
//        //    var compressed = new CompressedContent(content, new GZipCompressor());

//        //    return compressed;
//        //}

//        public static IObservable<T> ReadAsJson<T>(this IObservable<HttpResponseMessage> response)
//        {
//            return Observable.Create<T>(o =>
//            {
//                return response.Subscribe(msg =>
//                {
//                    msg.Content.ReadAsStringAsync().ToObservable().Subscribe(result =>
//                    {
//                        try
//                        {
//                            var content = JsonConvert.DeserializeObject<T>(result);
//                            o.OnNext(content);
//                        }
//                        catch (Exception e)
//                        {
//                            o.OnError(e);
//                        }
//                        finally
//                        {
//                            o.OnCompleted();
//                        }
//                    },
//                    error => o.OnError(error));

//                }, error => o.OnError(error));
//            });
//        }
//    }

//}
