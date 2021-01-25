using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;


namespace Rxns.Collections
{
    /// <summary>
    /// Represents an FIFO list of objects that have failed to process for some reason, and 
    /// you would like to at a later time re-process them. Useful is a cloud environment where errors are transiant
    /// </summary>
    /// <typeparam name="TItem"></typeparam>
    public class RetryBucket<TItem> : IDisposable
    {
        private readonly TimeSpan _retryIn;
        private readonly Action<TItem> _retryAction;
        private readonly Action<TItem, Exception> _errorAction;
        private readonly Action<TItem, Exception> _discardAction;
        private readonly IScheduler _retryScheduler;
        private readonly Dictionary<TItem, int> _discardedItems = new Dictionary<TItem, int>();
        private IDisposable _discardTimer;
        /// <summary>
        /// The name of the bucket, for status messages
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The number of time an item is retried before considered poison
        /// </summary>
        public int MaxReties { get; set; }

        public int Count { get { return _discardedItems.Count; } }

        //was just elaving and thought that i should make the list is discards an abstract concept, so we can add persistence seemlessly later on
        //just implement an inmemory provider atm aka List<T>
        //look for the current IBackingStore implementations and see if there is a cohension

        /// <summary>
        /// todo: make backingstore a depdency so a different one can be swapped in
        /// todo: make retryTrigger a depdency so different ones can be swapped in
        /// </summary>
        /// <param name="errorAction"></param>
        /// <param name="discardAction">When an item has been retried more then the max retries, this action is called as the item is removed from the bucket and discarded</param>
        /// <param name="name">The name of the bucket as re turned from the status fuction</param>
        /// <param name="retryIn">The ttl of the retry action being called from the first item being being in the bucket</param>
        /// <param name="retryAction">The action to execute for each item in the bucket</param>
        /// <param name="maxReties"></param>
        /// <param name="retryScheduler">The scheduler for the retryIn timer</param>
        public RetryBucket(Action<TItem> retryAction, Action<TItem, Exception> errorAction, Action<TItem, Exception> discardAction = null, string name = "defaultBucket", TimeSpan? retryIn = null, int maxReties = 3, IScheduler retryScheduler = null)
        {
            Ensure.NotNull(retryAction, "retryAction");

            Name = name;
            MaxReties = maxReties;
            _retryIn = retryIn ?? TimeSpan.FromMinutes(10);
            _retryAction = retryAction;
            _errorAction = errorAction;
            _discardAction = discardAction;
            _retryScheduler = retryScheduler;
            _errorAction = _errorAction ?? new Action<TItem, Exception>((_, e) => { Debug.WriteLine("Will re try again, swalled: {0}", e);});
        }

        public void Add(TItem itemToTryLater)
        {
            _discardedItems.Add(itemToTryLater, 0);
            SetupRequeueOfDiscardedFiles();
        }

        private void SetupRequeueOfDiscardedFiles()
        {
            if (_discardTimer != null) return;

            _discardTimer = Observable. Timer(_retryIn, _retryScheduler)
                                        .Subscribe(_ => RetryNow());
        }

        public void RetryNow()
        {
            if (_discardTimer != null)
            {
                _discardTimer.Dispose();
                _discardTimer = null;
            }

            _discardedItems.ToArray().ForEach(i =>
            {
                try
                {
                    _retryAction(i.Key);
                    _discardedItems.Remove(i.Key);
                }
                catch (Exception e)
                {
                    _discardedItems[i.Key]++;

                    if (_discardedItems[i.Key] >= MaxReties)
                    {
                        Debug.WriteLine("Discarding item '{0}'", i.ToString());

                        try
                        {
                            if (_discardAction != null)
                                _discardAction(i.Key, e);
                        }
                        catch (Exception ee)
                        {
                            Debug.WriteLine("Could not poison message, discarding anyway: {0}", ee);
                        }
                        finally
                        {
                            _discardedItems.Remove(i.Key);
                        }
                    }

                    if (_discardedItems.Any())
                    {
                        _errorAction(i.Key, e);
                        SetupRequeueOfDiscardedFiles();
                    }
                }
            });
        }

        /// <summary>
        /// Returns the current state of the bucket as systemStatusMeta
        /// </summary>
        /// <returns></returns>
        public object Status()
        {
            return new
            {
                Bucket = Name,
                ToProcess = _discardedItems.Count
            };
        }

        public void Dispose()
        {
            if (_discardTimer == null) return;

            _discardTimer.Dispose();
            _discardTimer = null;
        }
    }
}
