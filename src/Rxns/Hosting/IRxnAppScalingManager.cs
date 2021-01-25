using System;
using Rxns.Interfaces;

namespace Rxns.Hosting
{
    public interface IRxnAppScalingManager : IDisposable
    {
        string Name { get; }

        IRxnAppScalingManager ConfigureWith(IRxnAppScalingPlan scalingPlan);
        void Manage(IRxnAppContext app);
        void UnManage(IRxnAppContext context);
    }

    public interface IRxnAppScalingPlan
    {
        IDisposable Monitor(string name, IRxnAppContext app);
    }


    public class ReactorCrashed : IRxn
    {
        public string Name { get; set; }
    }
}
