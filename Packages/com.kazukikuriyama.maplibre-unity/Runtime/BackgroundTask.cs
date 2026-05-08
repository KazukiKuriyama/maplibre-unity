using System;
using System.Threading;
using System.Threading.Tasks;

namespace MapLibre.Unity
{
    /// <summary>
    /// Platform-aware <c>Task.Run</c> wrapper. Exists because Unity 6 WebGL
    /// Player builds run on a single-threaded WebAssembly runtime by default
    /// -- <c>Task.Run</c> there does not give us a worker thread, so the work
    /// either runs synchronously inline or sits on a queue that never drains
    /// while a coroutine is polling <c>Task.IsCompleted</c>. The renderer /
    /// parser code that previously hard-coded <c>Task.Run</c> now goes through
    /// here so the WebGL behaviour is explicit:
    ///
    ///   - <b>Editor / Standalone / mobile / etc.</b>: forwards to
    ///     <c>Task.Run</c> as before. Real worker thread, real concurrency.
    ///   - <b>WebGL Player</b>: invokes <c>work</c> inline on the calling
    ///     thread and returns a pre-completed (or faulted / cancelled) task.
    ///     Any caller that polls <c>while (!task.IsCompleted) yield return
    ///     null;</c> exits on the first iteration. Net behaviour is identical
    ///     to running the body directly, just compatible with the existing
    ///     async pattern.
    ///
    /// This is a stop-gap. The proper next step (Phase 3.5+) is making the
    /// heavy parsers / mesh builders yield-based so they spread across frames
    /// instead of blocking when run inline.
    /// </summary>
    internal static class BackgroundTask
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>True on WebGL Player builds -- callers can branch when
        /// they want to skip the Task wrapper entirely.</summary>
        public const bool RunsInline = true;
#else
        public const bool RunsInline = false;
#endif

        public static Task Run(Action work, CancellationToken token = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (token.IsCancellationRequested) return Task.FromCanceled(token);
            try
            {
                work();
                return Task.CompletedTask;
            }
            catch (OperationCanceledException oce)
            {
                return Task.FromCanceled(oce.CancellationToken.IsCancellationRequested
                    ? oce.CancellationToken : token);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
#else
            return Task.Run(work, token);
#endif
        }

        public static Task<T> Run<T>(Func<T> work, CancellationToken token = default)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            try
            {
                return Task.FromResult(work());
            }
            catch (OperationCanceledException oce)
            {
                return Task.FromCanceled<T>(oce.CancellationToken.IsCancellationRequested
                    ? oce.CancellationToken : token);
            }
            catch (Exception e)
            {
                return Task.FromException<T>(e);
            }
#else
            return Task.Run(work, token);
#endif
        }
    }
}
