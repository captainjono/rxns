using System;
using System.Net.Http;
using Rxns.Hosting.Updates;

namespace Rxns.WebApi
{
    public interface IFileUploadProvider
    {
        IObservable<UploadedFile> GetFiles(HttpRequestMessage request);
    }
}
