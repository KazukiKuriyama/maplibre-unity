using System;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Retry policy for transient HTTP failures (5xx, network errors, timeouts).
    /// Matches MapLibre GL JS' tile-load retry behavior: exponential backoff,
    /// permanent errors (4xx except 408/429) are not retried.
    /// </summary>
    public static class HttpRetryPolicy
    {
        /// <summary>Maximum number of attempts including the initial request. 1 = no retries.</summary>
        public const int DefaultMaxAttempts = 3;

        /// <summary>Base backoff in seconds. The Nth retry waits BaseBackoff * 2^(N-1).</summary>
        public const float DefaultBaseBackoffSeconds = 0.5f;

        /// <summary>
        /// Decide whether the given completed request is worth retrying. A null request
        /// (e.g. cancellation) is never retryable.
        /// </summary>
        public static bool ShouldRetry(UnityWebRequest request)
        {
            if (request == null) return false;
            if (request.result == UnityWebRequest.Result.Success) return false;

            // Network / connection errors are transient -- retry.
            if (request.result == UnityWebRequest.Result.ConnectionError) return true;

            // Protocol error: inspect HTTP status. 5xx, 408 (timeout), 429 (rate limit) are
            // transient; other 4xx codes (404, 401, 403) are permanent and should not retry.
            long status = request.responseCode;
            if (status >= 500) return true;
            if (status == 408 || status == 429) return true;
            return false;
        }

        /// <summary>
        /// Compute the backoff delay before the (attemptNumber)th retry. attemptNumber is
        /// 1-based (1 = first retry after initial attempt failed).
        /// </summary>
        public static float BackoffSeconds(int attemptNumber,
            float baseBackoffSeconds = DefaultBaseBackoffSeconds)
        {
            if (attemptNumber < 1) attemptNumber = 1;
            // 2^(n-1) keeps the first retry quick (0.5s) and doubles each time.
            return baseBackoffSeconds * (float)Math.Pow(2, attemptNumber - 1);
        }
    }
}
