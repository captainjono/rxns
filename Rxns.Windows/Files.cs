using System;
using System.IO;


namespace Rxns.Windows
{
    /// <summary>
    /// Extension methods of the Files class
    /// </summary>
    public static class Files
    {
        /// <summary>
        /// Compares two files for differences in their hash
        /// </summary>
        /// <param name="fileSource">The source file path</param>
        /// <param name="fileDestination">The destination file path</param>
        /// <returns>If the files are equal</returns>
        public static bool IsEqualsTo(this string fileSource, string fileDestination)
        {
            using (var f1 = new FileStream(fileSource, FileMode.Open))
            {
                using (var f2 = new FileStream(fileDestination, FileMode.Open))
                {
                    return f1.IsEquaTo(f2);
                }
            }
        }

        /// <summary>
        /// Using file-system paths, copies a file from one path to another using
        /// a specific buffer size to chunk data into during the copy process
        /// 
        /// Using WinIO for the the fastet copying under the hood
        /// </summary>
        /// <param name="sourceFile">The path to the file to copy</param>
        /// <param name="destinationFile">The path to the destination file</param>
        /// <param name="bufferSize">The size of the buffer used for the copy operation</param>
        public static void CopyTo(this string sourceFile, string destinationFile, int bufferSize = 65536)
        {
            var buffer = new byte[bufferSize];
            int bytesRead = 0;

            using (var source = new WinFileIO(buffer))
            {
                //create empty file to write too
                File.WriteAllText(destinationFile, String.Empty);

                //now do the copy using a shared buffer
                using (var destination = new WinFileIO(buffer))
                {
                    source.OpenForReading(sourceFile);
                    destination.OpenForWriting(destinationFile);

                    do
                    {
                        bytesRead = source.Read(bufferSize);
                        destination.WriteBlocks(bytesRead);
                    } while (bytesRead == bufferSize);
                }
            }
        }
    }
}
