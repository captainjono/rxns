using System;
using Microsoft.AspNetCore.Http;
using Rxns.WebApiNET5.NET5WebApiAdapters;

namespace Rxns.WebApiNET5
{
    public interface IFileUploadProvider
    {
        IObservable<UploadedFile> GetFiles(HttpRequest request);
    }
}
