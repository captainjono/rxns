using System;
using System.Collections.Generic;
using Autofac;
using Rxns.DDD.Commanding;
using Rxns.DDD.CQRS;
using Rxns.Interfaces;
using Rxns.Microservices;
using Rxns.Scheduling;

namespace Rxns.Hosting
{
    public interface IRxnDef
    {
        IAppContainer Container { get; }
        IRxnDef UpdateWith(Action<IRxnLifecycle> lifecycle);
        void Build(IAppContainer updateExisting = null);
    }
    public interface IRxnLifecycle : IModule
    {
        IRxnLifecycle CreatesOncePerRequestNamed<T, TName>(string name = null, bool preserveExisting = false);
        IRxnLifecycle CreatesOncePerAppNamed<T, TName>(string name, bool preserveExisting = false);
        IRxnLifecycle CreatesOncePerAppNamed<T, TName>(Func<T> factory, string name, bool preserveExisting = false);
        IRxnLifecycle CreatesOncePerApp<T>(bool preserveExisting = false);
        IRxnLifecycle CreatesOncePerAppAs<TService, TImplements>(bool preserveExisting = false);
        IRxnLifecycle CreatesOncePerRequest<T>();
        IRxnLifecycle CreatesOncePerRequest<T>(Func<T> factory);
        IRxnLifecycle CreatesOncePerRequestAs<T>(Func<IResolveTypes, IEnumerable<object>, T> factory);
        IRxnLifecycle CreatesOncePerRequestAs<T, T2>(Func<IResolveTypes, IEnumerable<object>, T> factory);
        IRxnLifecycle CreatesOncePerRequest<T>(Func<IResolveTypes, T> factory);
        IRxnLifecycle CreatesOncePerApp(Type type);
        IRxnLifecycle CreateGenericOncePerAppAs(Type type, Type asT);
        IRxnLifecycle CreatesOncePerApp<T>(Func<T> factory, bool preserveExisting = false, string named = null);
        IRxnLifecycle CreatesOncePerApp<T>(Func<IResolveTypes, T> factory, bool preserveExisting = false, params string[] named);

        IRxnLifecycle RespondsToCmd<T>() where T : IDomainCommand;
        IRxnLifecycle RespondsToQry<T>() where T : IDomainQuery;
        IRxnLifecycle RespondsToSvcCmds<T>() where T : IServiceCommand;
        IRxnLifecycle RunsTask<T>() where T : ITask;


        IRxnLifecycle Emits<T>() where T : IRxn;
        IRxnLifecycle EmitsAnyIn<T>() where T : IRxn;

        IRxnLifecycle Includes<T>() where T : IAppModule, new();
    }
}
