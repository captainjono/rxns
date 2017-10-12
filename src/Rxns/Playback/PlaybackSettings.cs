namespace Rxns.Playback
{
    public class PlaybackSettings
    {
        /// <summary>
        /// Controls how quickly events are played in relation
        /// to the elpsed time of the tape.
        /// 
        /// 1   = Real time
        /// < 1 = Slower then real time (SLOW MO)
        /// > 1 = Faster then real time (FFWD)
        /// </summary>
        public double Speed { get; set; }

        /// <summary>
        /// How often frames are drawn. default is 1fps
        /// </summary>
        public double TickSpeed { get; set; }

        /// <summary>
        /// The number of actions that are read in advance of the current
        /// playback position
        /// </summary>
        public int ActionBuffer { get; set; }

        public PlaybackSettings()
        {
            Speed = 1;
            ActionBuffer = 1;
            TickSpeed = 1;
        }
    }
}
