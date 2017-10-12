using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Navigation.Pages
{
    public class HomeNavigationPage : NavigationPage
    {
        public HomeNavigationPage(Page page, bool hasInitalNavBar = false) : base(page)
        {
            BarBackgroundColor = Color.Gray;
            BarTextColor = Color.Black;
            Title = page.Title;
            SetHasNavigationBar(page, hasInitalNavBar);
        }
    }
}
