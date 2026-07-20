using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Rxns.Commanding;
using Rxns.Health.AppStatus;
using Rxns.Hosting.Updates;
using Rxns.Interfaces;
using Rxns.Logging;

namespace Rxns.Hosting.Cluster
{
    public class AutoScalingAppManager : IRxnAppScalingManager
    {
        private readonly List<IRxnAppScalingPlan> _scalingPlans = new List<IRxnAppScalingPlan>();
        private readonly CompositeDisposable _resources = new CompositeDisposable();
        private readonly IDictionary<string, IDisposable> _apps = new Dictionary<string, IDisposable>();

        public void Dispose()
        {
            _resources.Dispose();
            _resources.Clear();
        }

        public string Name => "AutoScaler";
        public IRxnAppScalingManager ConfigureWith(IRxnAppScalingPlan scalingPlan)
        {
            _scalingPlans.Add(scalingPlan);

            return this;
        }
        
        public void Manage(IRxnAppContext app)
        {
            _scalingPlans.ForEach(p =>
            {
                var key = (app.args.ToStringEach() ?? Guid.NewGuid().ToString()).Replace("reactor,", "");

                $"Managing rxn {key}".LogDebug();
                p.Monitor(key, app).DisposedBy(_resources);
            });
        }

        public void UnManage(IRxnAppContext app)
        {
            _scalingPlans.ForEach(p =>
            {
                var key = app.args.ToStringEach().Replace("reactor,", "");
                $"Managing rxn {key}".LogDebug();
                _apps[key].Dispose();
                _apps.Remove(key);
            });
        }
    }
}
