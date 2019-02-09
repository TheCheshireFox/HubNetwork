using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork
{
    internal static class TaskExtentions
    {
        public static async Task<T> Timeout<T>(this Task<T> task, TimeSpan timeout, CancellationToken? cts = null)
        {
            CancellationTokenRegistration? ctr = null;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            if (cts.HasValue)
            {
                ctr = cts.Value.Register(() =>
                {
                    tcs.TrySetCanceled();
                    ctr?.Dispose();
                });
            }

            if (timeout == TimeSpan.MaxValue)
            {
                return await task;
            }

            var delay = Task.Delay(timeout).ContinueWith<T>(d => throw new TimeoutException());
            return await await Task.WhenAny(task, delay);
        }

        public static async Task Timeout(this Task task, TimeSpan timeout, CancellationToken? cts = null)
        {
            CancellationTokenRegistration? ctr = null;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            if (cts.HasValue)
            {
                ctr = cts.Value.Register(() =>
                {
                    tcs.TrySetCanceled();
                    ctr?.Dispose();
                });
            }

            if (timeout == TimeSpan.MaxValue)
            {
                await task;
                return;
            }

            var delay =  Task.Delay(timeout).ContinueWith(d => throw new TimeoutException());
            await await Task.WhenAny(task, delay);
        }

        public static async Task Cancellable(this Task task, CancellationToken ct)
        {
            CancellationTokenRegistration ctr = default;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            ctr = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                ctr.Dispose();
            });

            await await Task.WhenAny(task, tcs.Task);
        }

        public static async Task<T> Cancellable<T>(this Task<T> task, CancellationToken ct)
        {
            CancellationTokenRegistration ctr = default;
            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

            ctr = ct.Register(() =>
            {
                tcs.TrySetCanceled();
                ctr.Dispose();
            });

            return await await Task.WhenAny(task, tcs.Task);
        }
    }
}
