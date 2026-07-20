using System;
using Rxns.Hosting.Updates;
using Rxns.WebApi.Server.IO;

namespace Rxns.WebApi
{
    public abstract class ReportsStatusApiControllerWithUpload : ReportsStatusApiController
    {
        private readonly IFileUploadProvider _uploadProvider;

        protected ReportsStatusApiControllerWithUpload(IFileUploadProvider uploadProvider)
        {
            _uploadProvider = uploadProvider;
        }

        protected IObservable<UploadedFile> GetUploadedFiles()
        {
            return _uploadProvider.GetFiles(Request);
        }
    }
}
