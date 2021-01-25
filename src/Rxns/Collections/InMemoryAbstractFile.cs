using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Subjects;


namespace Rxns.Collections
{
    public class InMemoryAbstractFile : IAbstractFile
    {
        private readonly Dictionary<string, string> _configuration;
        private readonly BehaviorSubject<string> _configSubject;

        public InMemoryAbstractFile()
        {
            _configuration = new Dictionary<string, string>();
            _configSubject = new BehaviorSubject<string>("");
        }

        public string Get(string settingName)
        {
            return _configuration.ContainsKey(settingName) ? _configuration[settingName] : null;
        }

        public void Set(Dictionary<string, string> settings)
        {
            foreach(var setting in settings)
                Set(setting.Key, setting.Value);
        }

        public void Set(string key, string value)
        {
            _configuration.AddOrReplace(key, value);
        }

        public IObservable<string> Read()
        {
            return _configSubject;
        }

        public IObservable<Unit> Write(string contents)
        {
            CurrentThreadScheduler.Instance.Schedule(() => _configSubject.OnNext(contents));

            return new Unit().ToObservable();
        }

        public bool Exists
        {
            get { return true; }
        }
    }
}
