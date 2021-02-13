using System;

namespace Rxns.Azure
{
    /// <summary>
    /// Define table information
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class AzureTableAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AzureTableAttribute"/> class.
        /// </summary>
        /// <param name="name">The name of the table.</param>
        public AzureTableAttribute(string name)
        {
            this.Name = name;
        }

        /// <summary>
        /// Gets or sets the name of the table.
        /// </summary>
        /// <value>
        /// The name.
        /// </value>
        public string Name { get; set; }
    }
}
