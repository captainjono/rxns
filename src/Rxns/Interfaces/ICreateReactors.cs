namespace Rxns.Interfaces
{
    /// <summary>
    /// This interface should be used whenever you want to create a new reactor in the system
    /// </summary>
    public interface ICreateReactors
    {
        /// <summary>
        /// Gets a reactor if it hasnts already been created. Otherwise
        /// this will create the reactor and leave it ready for a connection
        /// to be made
        /// </summary>
        /// <param name="reactor"></param>
        /// <returns></returns>
        ReactorConnection GetOrCreate(string reactor);
    }
}
