using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns
{
    /// <summary>
    /// Provides a task scheduler that ensures a maximum concurrency level while  
    /// running on top of the thread pool. 
    /// 
    /// credit: goes to someone one stackoverflow for most of the code, cant find the post now :|
    /// </summary>
    public class TaskPoolSchedulerWithLimiter : TaskScheduler
    {
        // Indicates whether the current thread is processing work items.
        [ThreadStatic]
        private static bool _currentThreadIsProcessingItems;

        // The list of tasks to be executed  
        private readonly LinkedList<Task> _tasks = new LinkedList<Task>(); // protected by lock(_tasks) 

        // The maximum concurrency level allowed by this scheduler.  
        private readonly int _maxDegreeOfParallelism;

        // Indicates whether the scheduler is currently processing work items.  
        private int _delegatesQueuedOrRunning = 0;
        [ThreadStatic]
        private static Action _onCompleted;

        private int _taskQueue;

        // Gets the maximum concurrency level supported by this scheduler.  
        public sealed override int MaximumConcurrencyLevel { get { return _maxDegreeOfParallelism; } }

        public int PoolSize { get { return _maxDegreeOfParallelism; } }
        public int PoolCurrent { get { return _delegatesQueuedOrRunning; } }

        // Creates a new instance with the specified degree of parallelism.  
        public TaskPoolSchedulerWithLimiter(int maxDegreeOfParallelism, Action onCompleted = null)
        {
            if (maxDegreeOfParallelism < 1) throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
            _onCompleted = onCompleted;
        }

        // Queues a task to the scheduler.  
        protected sealed override void QueueTask(Task task)
        {
            // Add the task to the list of tasks to be processed.  If there aren't enough  
            // delegates currently queued or running to process tasks, schedule another.  
            lock (_tasks)
            {
                _tasks.AddLast(task);

                if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism)
                {
                    _taskQueue = 0;
                    ++_delegatesQueuedOrRunning;
                    NotifyThreadPoolOfPendingWork();
                }
                else
                {
                    _taskQueue++;
                }
                
            }
        }

        // Inform the ThreadPool that there's work to be executed for this scheduler.  
        private void NotifyThreadPoolOfPendingWork()
        {
            Observable.Start(() =>
            {
                // Note that the current thread is now processing work items. 
                // This is necessary to enable inlining of tasks into this thread.
                _currentThreadIsProcessingItems = true;
                try
                {
                    // Process all available items in the queue. 
                    while (true)
                    { 
                        Task item;
                        lock (_tasks)
                        {
                            // When there are no more items to be processed, 
                            // note that we're done processing, and get out. 
                            if (_tasks.Count == 0)
                            {
                                --_delegatesQueuedOrRunning;
                                break;
                            }

                            // Get the next item from the queue
                            item = _tasks.First.Value;
                            _tasks.RemoveFirst();   
                        }

                        // Execute the task we pulled out of the queue 
                        base.TryExecuteTask(item);


                        if (_onCompleted != null)
                            try
                            {
                                _onCompleted();
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine("TaskPoolSchedulerWithLimiter Swalled exception {0}", e);
                            }
                    }
                }
                // We're done processing items on the current thread 
                finally { _currentThreadIsProcessingItems = false; }
            }, TaskPoolScheduler.Default);
        }

        // Attempts to execute the specified task on the current thread.  
        protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // If this thread isn't already processing a task, we don't support inlining 
            if (!_currentThreadIsProcessingItems) return false;

            // If the task was previously queued, remove it from the queue 
            if (taskWasPreviouslyQueued)
                // Try to run the task.  
                if (TryDequeue(task))
                    return base.TryExecuteTask(task);
                else
                    return false;
            else
                return base.TryExecuteTask(task);
        }

        // Attempt to remove a previously scheduled task from the scheduler.  
        protected sealed override bool TryDequeue(Task task)
        {
            lock (_tasks) return _tasks.Remove(task);
        }

        // Gets an enumerable of the tasks currently scheduled on this scheduler.  
        protected sealed override IEnumerable<Task> GetScheduledTasks()
        {
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_tasks, ref lockTaken);
                if (lockTaken) return _tasks;
                else throw new NotSupportedException();
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_tasks);
            }
        }

        public static IScheduler ToScheduler(int threadCount, Action onThreadCompleted = null)
        {
            return new TaskPoolScheduler(new TaskFactory(new TaskPoolSchedulerWithLimiter(threadCount)));
        }

        public static IScheduler ToScheduler(TaskScheduler scheduler, Action onThreadCompleted = null)
        {
            return new TaskPoolScheduler(new TaskFactory(scheduler ?? new TaskPoolSchedulerWithLimiter(8)));
        }
    }
}
