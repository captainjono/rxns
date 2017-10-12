using System;
using System.Reactive.Linq;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation
{
    public static class PageHelpers
    {
        public static Page AsPageWith<TPage>(this RxnPageModel model, TPage page, string title, string icon = null, Action OnAppearing = null, Action OnDisapearing = null) where TPage : Page
        {
            page.Title = title;
            page.Icon = icon;

            Observable.FromEventPattern(a => page.Appearing += a, b => page.Appearing -= b)
                .Do(_ =>
                {
                    if (OnAppearing != null) OnAppearing();
                    page.BindingContext = model;
                })
                .Until(model.OnError)
                .DisposedBy(model);

            Observable.FromEventPattern(a => page.Disappearing += a, b => page.Disappearing -= b)
                .Do(_ =>
                {
                    if (OnDisapearing != null) OnDisapearing();
                    page.BindingContext = null;
                })
                .Until(model.OnError)
                .DisposedBy(model);

            return page;
        }
    }
}
