namespace Rxns.Playback
{
    public class RxnTape : ITapeStuff
    {
        public ITapeSource Source { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }

        public static ITapeStuff FromSource(string name, ITapeSource source)
        {
            return new RxnTape() { Source = source, Name = name };
        }
    }
}
