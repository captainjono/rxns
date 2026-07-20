using System;
using Rxns.DDD.Commanding;

namespace Rxns.DDD.CQRS
{
    /// <summary>
    /// A convience class/interface so i can work with the IoC container in setting up and referncing the command
    /// mediator pipeline. Without this class, decorating and specifying the pipeline requires combersum registrations with Named() tags
    /// </summary>
    public class DomainCommandMediator : Mediator, IDomainCommandMediator
    {
        public DomainCommandMediator(SingleInstanceFactory singleInstanceFactory) : base(singleInstanceFactory)
        {
        }

        public override Type Handles()
        {
            return typeof(IDomainCommandHandler<,>);
        }
    }

    /// <summary>
    /// A convience interface for IoC
    /// </summary>
    public interface IDomainCommandMediator : IMediator
    {
    }
}
