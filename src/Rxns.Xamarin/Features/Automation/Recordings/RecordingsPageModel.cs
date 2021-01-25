using System;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using Rxns.DDD.Commanding;
using Rxns.Xamarin.Features.Navigation;
using Rxns.Xamarin.Features.Navigation.Pages;
using Xamarin.Forms;

namespace Rxns.Xamarin.Features.Automation.Recordings
{
    public class RecordingsPageModel : OutputViewModel
    {
        private readonly UserAutomationService _automator;

        public class RecordingsInfo
        {
            /// <summary>
            /// The name of the menu item
            /// </summary>
            public string Name { get; set; }
            /// <summary>
            /// The icon that will be next to the name in the menu
            /// </summary>
            public ImageSource Icon { get; set; }
            /// <summary>
            /// THe command executed when the item is selected
            /// </summary>
            public ICommand LaunchCommand { get; set; }
            public ICommand DeleteCommand { get; set; }
            /// <summary>
            /// The hint for the menu item. ie. the current setting value
            /// </summary>
            public string Hint { get; set; }
            /// <summary>
            /// How heigh the menu item is, default is 40, seqerators should be smaller
            /// </summary>
            public int Thickness { get; set; }

            public RecordingsInfo()
            {
                Thickness = 40;
            }
        }

        public RecordingsInfo[] Recordings { get; private set; }

        public RecordingsPageModel(INavigationService<IRxnPageModel> nav, UserAutomationService automator)
        {
            _automator = automator;
            PageTitle = "All Recordings";
        }

        public override IDisposable Show()
        {
            return _automator
                        .GetAll()
                        .Select(fs => fs.Select(ff => ff)
                                        .Select(f => new RecordingsInfo()
                                        {
                                            Name = f.Name,
                                            LaunchCommand = _publish.OnExecute(new UserAutomationService.PlayRecording(f.Name)),
                                            DeleteCommand = _publish.OnExecute(new UserAutomationService.DeleteRecording(f.Name))
                                        }))
                        .UpdateUIWith(allrecordings => Recordings = allrecordings.ToArray())
                        .Until(OnError);
        }
    }

}

