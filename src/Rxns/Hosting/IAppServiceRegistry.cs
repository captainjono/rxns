namespace Rxns.Hosting
{
    public interface IAppServiceRegistry
    {
        string AppStatusUrl { get; set;}
    }

    public class AppServiceRegistry : IAppServiceRegistry
    {
        public string AppStatusUrl { get; set; }
    }
}
