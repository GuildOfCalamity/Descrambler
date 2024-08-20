using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Descrambler
{
    /// <summary>
    /// <para>A home-brew version of <see cref="System.Threading.Tasks.Task"/>.</para>
    /// <para>At its core, <see cref="BasicTask"/> uses <see cref="BasicThreadPool"/> to execute the requested action/delegate.</para>
    /// </summary>
    /// <remarks>
    /// This class is based on: Writing async/await from scratch in C# with Stephen [https://www.youtube.com/watch?v=R-z2Hv-7nxk]
    /// </remarks>
    public class BasicTask
    {
        private bool _completed;
        private Exception _exception;
        private Action _continuation;
        private ExecutionContext _context;

        /// <summary>
        /// Support for await keyword
        /// </summary>
        public struct Awaiter : INotifyCompletion // ICriticalNotifyCompletion is for UnsafeOnCompleted(Action) support.
        {
            private BasicTask _bt;
            public Awaiter(BasicTask bt) { _bt = bt; }
            public Awaiter GetAwaiter() => this;
            public bool IsCompleted => _bt.IsCompleted;
            public void OnCompleted(Action continuation) => _bt?.ContinueWith(continuation);
            public void GetResult() => _bt?.Wait();
        }
        public Awaiter GetAwaiter() => new Awaiter(this);
        public bool IsCompleted
        {
            get
            {
                lock (this) // private state on a public object
                {
                    return _completed;
                }
            }
        }
        public void SetResult() => Complete(null);
        public void SetException(Exception exception) => Complete(exception);
        public void Wait()
        {
            ManualResetEventSlim mres = null;

            lock (this) // private state on a public object
            {
                if (!_completed)
                {
                    // We only create a ManualResetEvent if the task has not completed.
                    mres = new ManualResetEventSlim();
                    ContinueWith(mres.Set);
                }
            }
            mres?.Wait();

            /** we'll only get here if it's done **/

            if (_exception != null)
            {
                // We don't want to overwrite the current Stack Trace,
                // so we'll propagate the exception correctly using
                // the ExceptionDispatchInfo's throw method.
                ExceptionDispatchInfo.Capture(_exception).Throw();

                // You could also wrap and propagate the exception
                // using a new AggregateException.
                //throw new AggregateException(_exception);
            }
        }

        public BasicTask ContinueWith(Action action)
        {
            BasicTask t = new BasicTask();

            Action callback = () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"BasicTask.ContinueWith: {ex.Message}");
                    //Debug.WriteLine($"StackTrace: {Environment.StackTrace}");
                    t.SetException(ex);
                    return;
                }
                t.SetResult();
            };

            lock (this) // private state on a public object
            {
                if (_completed)
                {
                    BasicThreadPool.QueueUserWorktem(callback);
                }
                else
                {
                    _continuation = callback;
                    _context = ExecutionContext.Capture();
                }
            }

            return t;
        }

        public BasicTask ContinueWith(Func<BasicTask> action)
        {
            BasicTask t = new BasicTask();

            Action callback = () =>
            {
                try
                {
                    BasicTask next = action();
                    next.ContinueWith(delegate
                    {
                        if (next._exception != null)
                        {
                            t.SetException(next._exception);
                        }
                        else
                        {
                            t.SetResult();
                        }
                    });
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"BasicTask.ContinueWith: {ex.Message}");
                    //Debug.WriteLine($"StackTrace: {Environment.StackTrace}");
                    t.SetException(ex);
                    return;
                }
            };

            lock (this) // private state on a public object
            {
                if (_completed)
                {
                    BasicThreadPool.QueueUserWorktem(callback);
                }
                else
                {
                    _continuation = callback;
                    _context = ExecutionContext.Capture();
                }
            }

            return t;
        }

        public static BasicTask Run(Action action)
        {
            BasicTask t = new BasicTask();

            BasicThreadPool.QueueUserWorktem(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    //Debug.WriteLine($"BasicTask.Run: {ex.Message}");
                    //Debug.WriteLine($"StackTrace: {Environment.StackTrace}");
                    t.SetException(ex);
                    return;
                }
                t.SetResult();
            });

            return t;
        }

        public static BasicTask WhenAll(List<BasicTask> tasks)
        {
            BasicTask t = new BasicTask();

            if (tasks.Count == 0)
            {
                t.SetResult();
            }
            else
            {
                int remaining = tasks.Count;

                Action continuation = () =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        t.SetResult();
                    }
                };

                try
                {
                    foreach (var task in tasks)
                    {
                        task.ContinueWith(continuation);
                    }
                }
                catch (Exception ex)
                {
                    t.SetException(ex);
                }
            }
            return t;
        }

        public static BasicTask Delay(TimeSpan timespan) => Delay(timespan.Milliseconds);
        public static BasicTask Delay(int timeout)
        {
            BasicTask t = new BasicTask();
            if (timeout > 0)
            {   // Do not use Thread.Sleep() here, instead create a timer to fire SetResult() upon timeout.
                _ = new Timer(_ => t.SetResult()).Change(timeout, Timeout.Infinite);
            }
            return t;
        }

        public static BasicTask Iterate(IEnumerable<BasicTask> tasks)
        {
            BasicTask t = new BasicTask();
            IEnumerator<BasicTask> e = tasks.GetEnumerator();
            void MoveNext()
            {
                try
                {
                    if (e.MoveNext())
                    {
                        BasicTask next = e.Current;
                        next.ContinueWith(MoveNext);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    t.SetException(ex);
                    return;
                }

                t.SetResult();
            }
            MoveNext();
            return t;
        }

        void Complete(Exception exception)
        {
            lock (this) // private state on a public object
            {
                if (_completed)
                    throw new InvalidOperationException("Multiple tasks think they're the last one.");

                _completed = true;
                _exception = exception;

                // If we have a continuation, then run it now.
                if (_continuation != null)
                {
                    BasicThreadPool.QueueUserWorktem(delegate
                    {
                        if (_context is null)
                            _continuation();
                        else
                            ExecutionContext.Run(_context, (object state) => ((Action)state)?.Invoke(), _continuation);
                    });
                }
            }
        }
    }

    /// <summary>
    /// <para>A home-brew version of <see cref="ThreadPool.QueueUserWorkItem(WaitCallback)"/></para>
    /// <para>which supports the local variable capture via <see cref="ExecutionContext"/>.</para>
    /// </summary>
    /// <remarks>
    /// This class is based on: Writing async/await from scratch in C# with Stephen [https://www.youtube.com/watch?v=R-z2Hv-7nxk]
    /// </remarks>
    public static class BasicThreadPool
    {
        static ulong _lifeCount = 0;
        static int _maxConcurrencyLevel = Math.Max(1, Environment.ProcessorCount);
        public static ThreadPriority GlobalPriority = ThreadPriority.Normal;

        /// <summary>
        /// We're adding <see cref="ExecutionContext"/> to the <see cref="BlockingCollection{T}"/>
        /// to make this <see cref="AsyncLocal{T}"/>-friendly to support things like variable capture.
        /// </summary>
        static readonly BlockingCollection<(Action, ExecutionContext)> _workItems = new BlockingCollection<(Action, ExecutionContext)>();

        /// <summary>
        /// This is our only public facing method for the user.
        /// </summary>
        /// <param name="action"><see cref="Action"/></param>
        public static void QueueUserWorktem(Action action) => _workItems.Add((action, ExecutionContext.Capture()));

        static BasicThreadPool()
        {
            Debug.WriteLine($"[INFO] Maximum concurrent threads: {_maxConcurrencyLevel}");
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                new Thread(() =>
                {
                    while (true)
                    {
                        (Action workItem, ExecutionContext context) = _workItems.Take();
                        if (context is null)
                            workItem.Invoke();
                        else
                        {
                            ExecutionContext.Run(context, (object state) => ((Action)state)?.Invoke(), workItem);
                            // Less efficient due to the closure, but may be required on older versions:
                            //ExecutionContext.Run(context, delegate { wi.Invoke(); }, null);
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = $"MyThread_{_lifeCount}",
                    Priority = GlobalPriority
                }.Start();
            }
        }
    }
}
