namespace Rxns.Playback
{
    public class EventTape : ITapeStuff
    {
        public ITapeSource Source { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }

        public static ITapeStuff FromSource(string name, ITapeSource source)
        {
            return new EventTape() { Source = source, Name = name };
        }
    }
}
