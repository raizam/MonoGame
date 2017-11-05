
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework
{

#if USE_GAMETASK
    public class GameTask
    {
        internal GameTask(Task task, Func<TimeSpan> durationFunc)
        {
            this.innerTask = task;
            this.durationFunc = durationFunc;
        }

        internal protected readonly Task innerTask;
        protected readonly Func<TimeSpan> durationFunc;
        private TimeSpan finalDuration = TimeSpan.Zero;

        protected void StopWatch()
        {
            //if (finalDuration.Equals(TimeSpan.Zero))
            //    finalDuration = durationFunc();
        }

        public GameTask Then(Action<GameTask> then)
        {
            return new GameTask(innerTask.ContinueWith(t => { StopWatch(); then(this); }, TaskContinuationOptions.ExecuteSynchronously), durationFunc);
        }

        public GameTask<R> Then<R>(Func<GameTask, R> then)
        {
            return new GameTask<R>(innerTask.ContinueWith(t => { StopWatch(); return then(this); }, TaskContinuationOptions.ExecuteSynchronously), durationFunc);
        }

        public AggregateException Exception => innerTask.Exception;
        public bool IsFaulted => innerTask.IsFaulted;

        public TimeSpan TotalDuration => durationFunc();

        public TaskAwaiter GetAwaiter()
        {
            return innerTask.GetAwaiter();
        }

        public static GameTask<T> FromResult<T>(T result)
        {
            return new GameTask<T>(Task.FromResult(result), () => TimeSpan.Zero);
        }
    }

    public class GameTask<T> : GameTask
    {
        internal GameTask(Task<T> task, Func<TimeSpan> durationFunc) : base(task, durationFunc) { }

        public GameTask Then(Action<GameTask<T>> then)
        {
            return new GameTask(((Task<T>)innerTask).ContinueWith(t => { StopWatch(); then(this); }, TaskContinuationOptions.ExecuteSynchronously), durationFunc);
        }

        public GameTask<R> Then<R>(Func<GameTask<T>, R> then)
        {
            return new GameTask<R>(((Task<T>)innerTask).ContinueWith(t => { StopWatch(); return then(this); }, TaskContinuationOptions.ExecuteSynchronously), durationFunc);
        }

        public T Result => ((Task<T>)innerTask).Result;

        public new TaskAwaiter<T> GetAwaiter()
        {
            return ((Task<T>)innerTask).GetAwaiter();
        }
    }

    public interface IGameTaskConsumer
    {
        Func<TimeSpan> GetDurationFunc();
        void EnqueueGameTaskContinuations(Action<long> action);
    }

    public class BackgroundThread : IDisposable
    {
        private readonly IGameTaskConsumer consumer;
        private Task thread;
        private volatile bool exit = false;

        private System.Collections.Concurrent.ConcurrentQueue<Action> actionQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public BackgroundThread(IServiceProvider serviceProvider, int idleDelayMs = 100)
        {
            this.consumer = (IGameTaskConsumer)serviceProvider.GetService(typeof(IGameTaskConsumer));
            this.thread = Task.Factory.StartNew(() =>
            {
                Thread.CurrentThread.Name = this.GetType().Name;
                while (!exit)
                {
                    Action action;
                    while (actionQueue.TryDequeue(out action))
                        action();

                    Thread.Sleep(idleDelayMs);
                }
            }, TaskCreationOptions.LongRunning);
        }

        public GameTask Execute(Action backgroundAction)
        {
            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.None);
            var tsk = new GameTask(taskCompletionSource.Task, consumer.GetDurationFunc());
            actionQueue.Enqueue(() =>
            {
                try
                {
                    backgroundAction();
                    consumer.EnqueueGameTaskContinuations((currentTime) => taskCompletionSource.SetResult(true));
                }
                catch (Exception ex)
                {
                    consumer.EnqueueGameTaskContinuations((currentTime) => taskCompletionSource.SetException(ex));
                }
            });

            return tsk;
        }


        public GameTask<T> Execute<T>(Func<T> backgroundFunc)
        {
            TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.None);
            var tsk = new GameTask<T>(taskCompletionSource.Task, consumer.GetDurationFunc());
            actionQueue.Enqueue(() =>
            {
                try
                {
                    T result = backgroundFunc();
                    consumer.EnqueueGameTaskContinuations((currentTime) => taskCompletionSource.SetResult(result));
                }
                catch (Exception ex)
                {
                    consumer.EnqueueGameTaskContinuations((currentTime) => taskCompletionSource.SetException(ex));
                }
            });

            return tsk;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            exit = true;

            if (disposing)
                thread.Wait();

            thread = null;
            actionQueue = null;
        }
    }
#endif //USE_GAMETASK
}
