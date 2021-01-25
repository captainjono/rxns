using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Rxns.Windows
{
    public static class HttpClientExtensions
    {
        public const string DEFAULT_CONTENTTYPE = "application/octet-stream";

        //make cross-paltform, expose static GetContentTypeImpl
        public static string GetContentType(string filename)
        {
            string result;
            RegistryKey key;
            object value;

            var extension = Path.GetExtension(filename);

            key = Registry.ClassesRoot.OpenSubKey("MIME\\Database\\Content Type", false);
            try
            {
                var type = key.GetSubKeyNames().FirstOrDefault(contentType =>
                {
                    var possible = key.OpenSubKey(contentType).GetValue("Extension");
                    return possible != null && possible.ToString().BasicallyEquals(extension);
                });

                return type != null ? type : DEFAULT_CONTENTTYPE;
            }
            catch (Exception e)
            {
                return DEFAULT_CONTENTTYPE;
            }
        }
    }
}
