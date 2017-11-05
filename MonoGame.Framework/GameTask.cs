
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Xna.Framework
{

#if USE_GAMETASK
    public class GameTask
    {
        internal GameTask(Task task)
        {
            this.task = task;
        }

        readonly internal protected Task task;

        public GameTask Then(Action<GameTask> then)
        {
            return new GameTask(task.ContinueWith(t => then(this), TaskContinuationOptions.ExecuteSynchronously));
        }

        public GameTask<R> Then<R>(Func<GameTask, R> then)
        {
            return new GameTask<R>(task.ContinueWith(t => then(this), TaskContinuationOptions.ExecuteSynchronously));
        }

        public AggregateException Exception => task.Exception;
        public bool IsFaulted => task.IsFaulted;


        public TaskAwaiter GetAwaiter()
        {
            return task.GetAwaiter();
        }

        public static GameTask<T> FromResult<T>(T result)
        {
            return new GameTask<T>(Task.FromResult(result));
        }
    }

    public class GameTask<T> : GameTask
    {
        internal GameTask(Task<T> task) : base(task) { }

        public GameTask Then(Action<GameTask<T>> toExecuteInGameThread)
        {
            return new GameTask(((Task<T>)task).ContinueWith(t => toExecuteInGameThread(this), TaskContinuationOptions.ExecuteSynchronously));
        }

        public GameTask<R> Then<R>(Func<GameTask<T>, R> toExecuteInGameThread)
        {
            return new GameTask<R>(((Task<T>)task).ContinueWith(t => toExecuteInGameThread(this), TaskContinuationOptions.ExecuteSynchronously));
        }

        public T Result => ((Task<T>)task).Result;

        public new TaskAwaiter<T> GetAwaiter()
        {
            return ((Task<T>)task).GetAwaiter();
        }
    }

    public interface IGameTaskConsumer
    {
        void EnqueueGameTaskContinuationAction(Action action);
    }

    public class BackgroundThread : IDisposable
    {
        private Task thread;
        private volatile bool exit = false;

        private System.Collections.Concurrent.ConcurrentQueue<Action> actionQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public BackgroundThread(int idleDelayMs = 100)
        {
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

        public GameTask Execute(Action backgroundAction, IGameTaskConsumer consumer)
        {
            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.None);

            actionQueue.Enqueue(() =>
            {
                try
                {
                    backgroundAction();
                    consumer.EnqueueGameTaskContinuationAction(() => taskCompletionSource.SetResult(true));
                }
                catch (Exception ex)
                {
                    consumer.EnqueueGameTaskContinuationAction(() => taskCompletionSource.SetException(ex));
                }
            });

            return new GameTask(taskCompletionSource.Task);
        }


        public GameTask<T> Execute<T>(Func<T> backgroundFunc, IGameTaskConsumer consumer)
        {
            TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.None);

            actionQueue.Enqueue(() =>
            {
                try
                {
                    T result = backgroundFunc();
                    consumer.EnqueueGameTaskContinuationAction(() => taskCompletionSource.SetResult(result));
                }
                catch (Exception ex)
                {
                    consumer.EnqueueGameTaskContinuationAction(() => taskCompletionSource.SetException(ex));
                }
            });

            return new GameTask<T>(taskCompletionSource.Task);
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
