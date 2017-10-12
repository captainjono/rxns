namespace Rxns.Commanding
{    
    /// <summary>
    /// A command which interacts with an application service outside
    /// the context of any buisness domain. The main idea behind a this style of 
    /// command is that it should be constructable easily via a text expression
    /// </summary>
    public interface IServiceCommand : IUniqueRxn
    {
        /// <summary>
        /// A string parsable representation of this command
        /// </summary>
        /// <returns></returns>
        string ToString();
    }
}
