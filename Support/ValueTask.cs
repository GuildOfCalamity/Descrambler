using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Descrambler
{
    /// <summary>
    /// ValueTask for dotNET 4.x
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct ValueTask<T>
    {
        private readonly T _result;
        private readonly Task<T> _task;
        private readonly bool _isCompleted;

        /// <summary>
        /// Constructor for synchronous ValueTask
        /// </summary>
        /// <param name="result"></param>
        public ValueTask(T result)
        {
            _result = result;
            _task = null;
            _isCompleted = true;
        }

        /// <summary>
        /// Constructor for asynchronous ValueTask
        /// </summary>
        /// <param name="task"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public ValueTask(Task<T> task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            _result = default(T);
            _isCompleted = false;
        }

        /// <summary>
        /// Property to check if the result is completed synchronously
        /// </summary>
        public bool IsCompleted => _isCompleted || (_task?.IsCompleted ?? false);

        /// <summary>
        /// Awaiter for async support
        /// </summary>
        /// <returns><see cref="ValueTaskAwaiter"/></returns>
        public ValueTaskAwaiter GetAwaiter()
        {
            return new ValueTaskAwaiter(this);
        }

        /// <summary>
        /// Gets the result either synchronously or from the Task
        /// </summary>
        public T Result
        {
            get
            {
                if (_isCompleted)
                    return _result;
                else
                    return _task.Result;
            }
        }

        /// <summary>
        /// Static method to create a ValueTask from a result (synchronous)
        /// </summary>
        /// <param name="result"></param>
        /// <returns><see cref="ValueTask{T}"/></returns>
        public static ValueTask<T> FromResult(T result)
        {
            return new ValueTask<T>(result);
        }

        /// <summary>
        /// Static method to create a ValueTask from an exception (asynchronous)
        /// </summary>
        /// <param name="exception"></param>
        /// <returns><<see cref="ValueTask{T}"/>/returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ValueTask<T> FromException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            return new ValueTask<T>(Task.FromException<T>(exception));
        }

        /// <summary>
        /// Static method to create a ValueTask from a canceled task (asynchronous)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns><see cref="ValueTask{T}"/></returns>
        public static ValueTask<T> FromCanceled(CancellationToken cancellationToken)
        {
            return new ValueTask<T>(Task.FromCanceled<T>(cancellationToken));
        }

        /// <summary>
        /// ConfigureAwait method to control capturing of the synchronization context
        /// </summary>
        /// <param name="continueOnCapturedContext"></param>
        /// <returns><see cref="ConfiguredValueTaskAwaitable"/></returns>
        public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredValueTaskAwaitable(this, continueOnCapturedContext);
        }

        /// <summary>
        /// Custom Awaiter struct
        /// </summary>
        public struct ValueTaskAwaiter : System.Runtime.CompilerServices.INotifyCompletion
        {
            readonly ValueTask<T> _valueTask;

            public ValueTaskAwaiter(ValueTask<T> valueTask)
            {
                _valueTask = valueTask;
            }

            public bool IsCompleted => _valueTask.IsCompleted;

            public T GetResult() => _valueTask.Result;

            public void OnCompleted(Action continuation)
            {
                if (_valueTask._isCompleted)
                    continuation();
                else
                    _valueTask._task?.ContinueWith(t => continuation());
            }
        }

        /// <summary>
        /// Struct to handle ConfigureAwait functionality
        /// </summary>
        public struct ConfiguredValueTaskAwaitable
        {
            private readonly ValueTask<T> _valueTask;
            private readonly bool _continueOnCapturedContext;

            public ConfiguredValueTaskAwaitable(ValueTask<T> valueTask, bool continueOnCapturedContext)
            {
                _valueTask = valueTask;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public ConfiguredValueTaskAwaiter GetAwaiter()
            {
                return new ConfiguredValueTaskAwaiter(_valueTask, _continueOnCapturedContext);
            }
        }

        /// <summary>
        /// Awaiter for the ConfiguredValueTaskAwaitable
        /// </summary>
        public struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
        {
            private readonly ValueTask<T> _valueTask;
            private readonly bool _continueOnCapturedContext;

            public ConfiguredValueTaskAwaiter(ValueTask<T> valueTask, bool continueOnCapturedContext)
            {
                _valueTask = valueTask;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            public bool IsCompleted => _valueTask.IsCompleted;

            public T GetResult() => _valueTask.Result;

            public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);

            public void OnCompleted(Action continuation)
            {
                if (_valueTask._isCompleted)
                    continuation();
                else
                {
                    if (_continueOnCapturedContext)
                        _valueTask._task?.ContinueWith(t => continuation(), TaskScheduler.FromCurrentSynchronizationContext());
                    else
                        _valueTask._task?.ContinueWith(t => continuation(), TaskScheduler.Default);
                }
            }
        }
    }

    /// <summary>
    /// Static methods in a non-generic ValueTask struct.
    /// </summary>
    public static class ValueTask
    {
        public static ValueTask<T> FromResult<T>(T result)
        {
            return new ValueTask<T>(result);
        }
        
        public static ValueTask<T> FromException<T>(Exception exception)
        {
            if (exception == null) 
                throw new ArgumentNullException(nameof(exception));

            return new ValueTask<T>(Task.FromException<T>(exception));
        }

        public static ValueTask<T> FromCanceled<T>(CancellationToken cancellationToken)
        {
            return new ValueTask<T>(Task.FromCanceled<T>(cancellationToken));
        }
    }

    /// <summary>
    /// Example usage class
    /// </summary>
    class DriverExample
    {
        static void Main()
        {
            // FromResult usage
            var resultTask = ValueTask<int>.FromResult(42);
            Debug.WriteLine(resultTask.Result);

            // FromException usage
            var exceptionTask = ValueTask<int>.FromException(new InvalidOperationException("Testing FromException"));
            try
            {
                var result = exceptionTask.Result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message); // Outputs "Testing FromException"
            }

            // FromCanceled usage
            var cancellationToken = new CancellationToken(true);
            var canceledTask = ValueTask<int>.FromCanceled(cancellationToken);
            try
            {
                var result = canceledTask.Result;
            }
            catch (AggregateException ex)
            {
                Debug.WriteLine(ex.InnerException is TaskCanceledException); // Outputs "True"
            }

            // Async usage with GetAwaiter().OnCompleted()
            var valueTask = GetValueTaskAsync();
            valueTask.GetAwaiter().OnCompleted(() =>
            {
                Debug.WriteLine($"Async Result: {valueTask.Result}");
            });

        }

        public async Task<int> TestConfigureAwait()
        {
            // Using ConfigureAwait with ValueTask
            return await GetValueTaskAsync().ConfigureAwait(false);
        }

        public static ValueTask<int> GetValueTaskAsync()
        {
            return new ValueTask<int>(Task.Run(() => 42)); // Asynchronous result
        }
    }
}
