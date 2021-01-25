using System.IO;
using Rxns.Interfaces;

namespace Rxns.Collections
{
    public interface IAbstractFileMeta
    {
        /// <summary>
        /// The name of the file
        /// </summary>
        IFileMeta Meta { get; }

        /// <summary>
        /// Deletes the file. This operation cannot be undone.
        /// </summary>
        Stream Open();
        void Delete();
    }
}
