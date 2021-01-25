namespace Rxns.DDD.Commanding
{
    /// <summary>
    /// A handler which is executed after a commandhandler has run but before its result is returned to the user
    /// </summary>
    /// <typeparam name="TRequest"></typeparam>
    /// <typeparam name="TResponse"></typeparam>
    public interface IPostRequestHandler<in TRequest, in TResponse>
    {
        void Handle(TRequest request, TResponse response);
    }
}
