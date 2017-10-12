using System;

namespace Rxns.Xamarin.Features.Navigation
{
    public class AppPageInfo<TBasePage, TBasePageModel>
    {
        public TBasePage Page { get; set; }
        public TBasePageModel Model { get; set; }

        public AppPageInfo(TBasePage page, TBasePageModel model)
        {
            Ensure.NotNull(page, "page");
            Ensure.NotNull(model, "model");

            Page = page;
            Model = model;
        }

        public override string ToString()
        {
            return "{0}({1})".FormatWith(Page.GetType().Name, Model.GetType().Name);
        }
    }
}
