using System;

namespace Rxns.WebApi.Server.IO
{
    public class FileUploadException : Exception
    {
        public FileUploadException(string message)
            : base(message)
        {

        }
    }
}
