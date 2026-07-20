    using System.Collections.Generic;
    using System.Reactive.Disposables;
    using Rxns;
    using Rxns.Interfaces;
    

namespace System
    {
        public static class IDisposableExtensions
        {
            public static void DisposeAll(this IEnumerable<IDisposable> disposables)
            {
                foreach (var obj in disposables)
                    if (obj != null)
                        obj.Dispose();
            }

            public static T DisposedBy<T>(this T obj, IManageResources reporter) where T : IDisposable
            {
                reporter.OnDispose(obj);

                return obj;
            }

            public static T Disposes<T>(this T parent, IDisposable resource) where T : IManageResources
            {
                parent.OnDispose(resource);

                return parent;
            }

            public static T Disposeds<T>(this T obj, IEnumerable<IDisposable> disposables) where T : IManageResources
            {
                disposables.ForEach(d => obj.OnDispose(d));

                return obj;
            }

            public static T[] DisposedBy<T>(this T[] objs, IReportStatus reporter) where T : IDisposable
            {
                foreach (var obj in objs)
                    reporter.OnDispose(obj);

                return objs;
            }

            public static T DisposedBy<T>(this T obj, List<IDisposable> disposables) where T : IDisposable
            {
                disposables.Add(obj);

                return obj;
            }

            public static T DisposedWith<T>(this IEnumerable<IDisposable> disposables, T obj) where T : IManageResources
            {
                disposables.ForEach(d => obj.OnDispose(d));

                return obj;
            }


            public static T DisposedBy<T>(this T obj, CompositeDisposable disposables) where T : IDisposable
            {
                disposables.Add(obj);

                return obj;
            }


            public static List<IDisposable> DisposedWith(this IDisposable obj, Action action)
            {
                List<IDisposable> disposables = new List<IDisposable>();

                disposables.Add(new DisposableAction(action));
                disposables.Add(obj);

                return disposables;
            }

            public static IEnumerable<IDisposable> DisposedBy(this IEnumerable<IDisposable> obj, List<IDisposable> disposables)
            {
                disposables.AddRange(obj);

                return obj;
            }

            public static IEnumerable<T> DisposedBy<T>(this IEnumerable<T> objs, IReportStatus reporter) where T : IDisposable
            {
                foreach (var o in objs)
                    reporter.OnDispose(o);

                return objs;
            }
        }
    }

