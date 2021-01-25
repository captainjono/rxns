using Rxns.Logging;

namespace Rxns.Scheduling
{
    public abstract class StatefulTask<Ti, To> : ReportsStatus, ITask<Ti, To>
    {
        public abstract Ti Execute(To state);
    }
}
