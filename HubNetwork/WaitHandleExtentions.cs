using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork
{
    internal static class WaitHandleExtentions
    {
        public static async Task<bool> WaitOneAsync(this WaitHandle wh, int millisecondsTimeout = -1, CancellationToken? ct = null)
        {
            RegisteredWaitHandle rwh = null;
            CancellationTokenRegistration? ctr = null;

            try
            {
                var tcs = new TaskCompletionSource<bool>();

                rwh = ThreadPool.RegisterWaitForSingleObject(wh,
                    (s, t) => ((TaskCompletionSource<bool>)s).TrySetResult(!t),
                    tcs,
                    millisecondsTimeout,
                    true);

                ctr = ct?.Register(s => ((TaskCompletionSource<bool>)s).TrySetCanceled(), tcs);

                return await tcs.Task;
            }
            finally
            {
                rwh?.Unregister(null);
                ctr?.Dispose();
            }
        }
    }
}
