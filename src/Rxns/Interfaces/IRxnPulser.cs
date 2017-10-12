namespace Rxns.Interfaces
{
    /// <summary>
    /// A service which polled at a set interval for new events
    /// 
    /// "i pulse events at at a certain rate"
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRxnPulsar<T> : IRxnPulseService
    {
    }
}
