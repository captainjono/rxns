namespace Rxns.Scheduling
{
    public interface IRxnScheduler
    {
        void Start();
        void Pause();
        void Resume();
        void Stop();
        void Clear();

        void Schedule(ISchedulableTaskGroup group);
        void Unschedule(ISchedulableTaskGroup group);
    }
}
