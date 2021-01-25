using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Rxns.DDD.Commanding;
using Rxns.Interfaces;

namespace Rxns.Collections
{
    public interface ILookupStuff<TStuff>
    {
        IObservable<TStuff[]> View { get; }
        IObservable<bool> HasMoreStuff { get; }
    }

    /// <summary>
    /// todo: improve, test
    /// I removed the input/output streams and as the IProcessInput 
    /// 
    /// Example usage of my train of thought;
    ///             var summaryVm = new LookupAutomator<RvMatterSummary>(_app.Model.DetailsStream, 4);
    //_eventManager.CreateSubscription<MoreStuff>()
    //    .Where(w => w.Target == summaryVm.TargetId)
    //    .SelectMany(summaryVm.Process).Subscribe(_eventManager.Publish).DisposedBy(_resources);

    //SummaryId = summaryVm.TargetId;
    //summaryVm.Query.ObserveOn(RxApp.UIScheduler).Subscribe(m => Model = m);
    /// </summary>
    /// <typeparam name="TStuff"></typeparam>
    public class LookupAutomator<TStuff, TFilter> : ILookupStuff<TStuff>, IDisposable
    {
        public IObservable<TStuff[]> View { get; private set; }
        public IObservable<bool> HasMoreStuff { get { return canContinue; } }

        private readonly FutureSubject<ContinuationToken> continuation;
        private readonly BehaviorSubject<bool> canContinue;
        private ContinuationToken _continuationDefault;

        public LookupAutomator(IObservableQuery<Continuation<TStuff[]>, TFilter> queryStore)
        {
            _continuationDefault = queryStore.Continuation.Value();
            continuation = new FutureSubject<ContinuationToken>(_continuationDefault);
            canContinue = new BehaviorSubject<bool>(true);
            View = queryStore.FilterWith(queryStore.Filter)
                             .LimitWith(continuation).Do(c =>
                             {
                                 Debug.WriteLine("Got new continuation: {0}/{1} {2}({3})", c.Token.Size, c.Token.Total, c.Token.CanContinue, c.Token);
                                 continuation.Future(c.Token);
                                 canContinue.OnNext(c.Token.CanContinue);
                             })
                             .Select(q => q.Records);
        }

        public void LoadMore()
        {
            continuation.Next();
        }

        public void Refresh()
        {
            continuation.Future(_continuationDefault);
            continuation.Next();
        }

        public void Dispose()
        {
            canContinue.Dispose();
            continuation.Dispose();
        }
    }
}
