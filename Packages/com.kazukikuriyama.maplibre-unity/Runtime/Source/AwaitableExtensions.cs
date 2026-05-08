using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Bridges Unity's coroutine-style async primitives onto Unity 6 Awaitable
    /// so library code can `await` them uniformly.
    /// </summary>
    public static class AwaitableExtensions
    {
        /// <summary>
        /// Send a UnityWebRequest and resolve when the operation completes.
        /// Caller is responsible for disposing <paramref name="req"/> and
        /// inspecting <c>req.result</c>.
        /// </summary>
        public static Awaitable SendAsync(this UnityWebRequest req)
        {
            var tcs = new AwaitableCompletionSource();
            var op = req.SendWebRequest();
            if (op.isDone)
            {
                tcs.SetResult();
            }
            else
            {
                op.completed += _ => tcs.TrySetResult();
            }
            return tcs.Awaitable;
        }

        /// <summary>
        /// Generic <see cref="AsyncOperation"/> awaiter -- completes on the
        /// next frame after the operation finishes.
        /// </summary>
        public static Awaitable AsAwaitable(this AsyncOperation op)
        {
            var tcs = new AwaitableCompletionSource();
            if (op.isDone) tcs.SetResult();
            else op.completed += _ => tcs.TrySetResult();
            return tcs.Awaitable;
        }
    }
}
