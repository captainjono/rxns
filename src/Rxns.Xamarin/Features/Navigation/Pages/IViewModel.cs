using System;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    /// <summary>
    /// A view is a peice of UI content that has a simple life-cycle
    /// Show - when the UI should be displayed
    /// Hide - when the UI is hidden
    /// </summary>
    public interface IViewModel//<TCfg>
    {
        /// <summary>
        /// If the viewmodel is actively performing an operation or not
        /// </summary>
        bool IsLoading { get; }
        /// <summary>
        /// Sets up the view ready to be bound to a viewModel
        /// </summary>
        /// <returns>The resources that the view uses while is shown</returns>
        IDisposable Show();

        ///// <summary>
        ///// The viewmodel is doing work
        ///// </summary>
        //IObservable<bool> IsLoading { get; }

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <returns></returns>
        //IObservable<Unit> ShowBackground(); 
        /// <summary>
        /// Hides the view, cleaning up any resources
        /// </summary>
        void Hide();
    }
}
