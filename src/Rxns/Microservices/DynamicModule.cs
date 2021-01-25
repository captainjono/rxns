using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rxns.Microservices
{
        /// <summary>
        /// A great way to add/override depndencies on demand. Often used for unit tests or slightly
        /// modifying the container for a specific application. For static/long lived sets of dependencies,
        /// dont be lazy, create a proper Module
        /// </summary>
        public class DynamicModule : IModule
        {
            //private readonly Func<ContainerBuilder, ContainerBuilder> _definition;

            ///// <summary>
            ///// Accepts a function to to create your module from
            ///// </summary>
            ///// <param name="definition">The function that will be used to create your module. Return the same container that was given to you, please!</param>
            //public DynamicModule(Func<ContainerBuilder, ContainerBuilder> definition)
            //{
            //    _definition = definition;
            //}

            //protected override void Load(ContainerBuilder builder)
            //{
            //    builder = _definition(builder);

            //    base.Load(builder);
            //}
        }
}
