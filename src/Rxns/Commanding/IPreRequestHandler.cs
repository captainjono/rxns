namespace Rxns.Commanding
{
    /// <summary>
    /// A handler which is executed before the target commandHandler is run
    /// </summary>
    /// <typeparam name="TRequest"></typeparam>
    public interface IPreRequestHandler<in TRequest>
    {
        void Handle(TRequest message);
    }
}
