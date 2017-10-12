namespace Rxns.Xamarin.Features.Composition
{
    /// <summary>
    /// A bridge so apps can access OS specific functions who's implementation
    /// is different depending on the platform its being run on.  The design of the bridge is
    /// that the app should implement this class and the wrapper for the OS should depend on
    /// this interface and call these functions when the these things occour.
    /// 
    /// When each function should be called is outlined in the comments of each signature
    /// </summary>
    public interface IAppToOsBridge
    {
        /// <summary>
        /// When app is brought out of the background, into the foreground
        /// ready for user input
        /// </summary>
        void OnResume();
        /// <summary>
        /// When an app is sent into the background by the OS. The app may or may not be
        /// still active, but it definately has no way to receive user input. 
        /// </summary>
        void OnBackground();
    }
}
