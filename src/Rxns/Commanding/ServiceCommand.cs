using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Collections.Generic;


namespace Rxns.DDD.Commanding
{
    public abstract class ServiceCommand : IServiceCommand
    {
        //[IgnoreDataMember]
        public string Id { get; set; }


        public ServiceCommand()
        {
            Id = Guid.NewGuid().ToString();
        }

        /// <summary>
        /// A string representation of the this service command, that can later
        /// be parsed my ServiceCommand.Parse()
        /// should be of the form: ClassName prop1 prop2 prop3
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "{0} {1}".FormatWith(GetType().Name, GetType().GetProperties().Where(p => p.Name != "Id").Select(p => this.GetProperty(p.Name)).ToStringEach(" "));
        }

        /// <summary>
        /// Parses a service command from a string with format "serviceCommandClassName property1 property2 ... propertyN"
        /// </summary>
        /// <param name="command"></param>
        /// <param name="resolver"></param>
        /// <returns></returns>
        public static IServiceCommand Parse(string command, IServiceCommandFactory resolver)
        {
            var cmdTokens = command.Split(new[]{" "}, StringSplitOptions.RemoveEmptyEntries);
            var cmdType = cmdTokens[0].Trim();

            try
            {
                return resolver.Get(cmdType, cmdTokens.Skip(1).Select(v => v.Trim()).ToArray());
            }
            catch (Exception e)
            {
                throw new ServiceCommandNotFound("Could not resolve cmd '{0}' with options '{1}' because {2}", cmdType, cmdTokens.Skip(1).ToStringEach(), e.Message);
            }
        }
    }

    //public static class ServiceCommandExtensions
    //{
    //    /// <summary>
    //    /// Wraps a service command so it can be sent via a remoting mechamism to other 
    //    /// applications that understand them
    //    /// </summary>
    //    /// <param name="cmd"></param>
    //    /// <returns></returns>
    //    public static RxnQuestion AsQuestion(this IServiceCommand cmd)
    //    {
    //        return new RxnQuestion
    //        {
    //            Action = SystemCommand.ServiceCommand,
    //            Options = cmd.ToString(),
    //            RequestId = cmd.Id
    //        };
    //    }
    //}
}
