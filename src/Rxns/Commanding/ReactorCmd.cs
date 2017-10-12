namespace Rxns.Commanding
{
    public abstract class ReactorCmd : ServiceCommand
    {
        /// <summary>
        /// The name of the reactor to target the command at
        /// </summary>
        public string Name { get; set; }

        protected ReactorCmd()
        {
        }

        protected ReactorCmd(string name)
        {
            Name = name;
        }
    }
}
