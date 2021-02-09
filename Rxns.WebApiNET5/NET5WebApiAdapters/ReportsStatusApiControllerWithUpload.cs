using System;

namespace Rxns.WebApiNET5.NET5WebApiAdapters
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
