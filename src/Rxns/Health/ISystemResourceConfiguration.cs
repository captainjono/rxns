namespace Rxns.Health
{
    public interface ISystemResourceConfiguration
    {
        /// <summary>
        /// The maximum thread pool size as used by the RxnSchedulers TaskPoool
        /// </summary>
        int ThreadPoolSize { get; set; }
    }
}
