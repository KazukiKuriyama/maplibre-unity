using System;
using System.Threading;
using MapLibre.Unity.CameraControl;
using MapLibre.Unity.Source;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity
{
    /// <summary>
    /// Awaitable-returning counterparts to the fire-and-forget public API.
    /// Each *Async method resolves when the underlying MapLibre operation
    /// finishes; supplying a <see cref="CancellationToken"/> aborts the wait
    /// with <see cref="OperationCanceledException"/>.
    ///
    /// Naming follows MapLibre GL JS conventions (the JS map exposes the
    /// callback / event surface, callers wrap it in a Promise themselves).
    /// Here the wrapping is built in so callers can <c>await</c> directly.
    /// </summary>
    public partial class MapLibreMap
    {
        // === Lifecycle / events ===

        /// <summary>
        /// Resolves when the map raises <see cref="MapEventType.Load"/>.
        /// If the map is already loaded the returned <see cref="Awaitable"/>
        /// completes on the next frame.
        /// </summary>
        public Awaitable LoadAsync(CancellationToken cancellationToken = default)
            => WaitForEventInternal(MapEventType.Load, cancellationToken);

        /// <summary>
        /// Resolves the next time <paramref name="type"/> is fired and yields
        /// the <see cref="MapEvent"/>. Equivalent to wrapping
        /// <c>map.once('type', handler)</c> in a Promise.
        /// </summary>
        public Awaitable<MapEvent> OnceAsync(MapEventType type, CancellationToken cancellationToken = default)
        {
            var tcs = new AwaitableCompletionSource<MapEvent>();
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Awaitable;
            }

            Action<MapEvent> handler = null;
            CancellationTokenRegistration ctr = default;

            handler = evt =>
            {
                Off(type, handler);
                ctr.Dispose();
                tcs.TrySetResult(evt);
            };

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(() =>
                {
                    Off(type, handler);
                    tcs.TrySetCanceled();
                });
            }

            On(type, handler);
            return tcs.Awaitable;
        }

        // === Camera animations ===

        /// <summary>
        /// Awaitable counterpart of <see cref="EaseTo(EaseToOptions)"/>.
        /// Resolves when the animation ends -- naturally or via
        /// <see cref="MapAnimator.CancelAnimation"/> / a competing animation.
        /// Throws <see cref="OperationCanceledException"/> when the supplied
        /// <paramref name="cancellationToken"/> is signalled.
        /// </summary>
        public Awaitable EaseToAsync(EaseToOptions options, CancellationToken cancellationToken = default)
        {
            EaseTo(options);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="FlyTo(FlyToOptions)"/>.
        /// </summary>
        public Awaitable FlyToAsync(FlyToOptions options, CancellationToken cancellationToken = default)
        {
            FlyTo(options);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="ZoomTo(float, float)"/>.
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public Awaitable ZoomToAsync(float zoom, float durationMs = 300f, CancellationToken cancellationToken = default)
        {
            ZoomTo(zoom, durationMs);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="PanTo"/>.
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public Awaitable PanToAsync(LngLat center, float durationMs = 300f, CancellationToken cancellationToken = default)
        {
            PanTo(center, durationMs);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="RotateTo"/>.
        /// </summary>
        /// <param name="durationMs">Animation duration in milliseconds.</param>
        public Awaitable RotateToAsync(float bearing, float durationMs = 300f, CancellationToken cancellationToken = default)
        {
            RotateTo(bearing, durationMs);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="FitBounds(LngLatBounds, FitBoundsOptions)"/>.
        /// </summary>
        public Awaitable FitBoundsAsync(LngLatBounds bounds, FitBoundsOptions options = default, CancellationToken cancellationToken = default)
        {
            FitBounds(bounds, options);
            return WaitForAnimationEndInternal(cancellationToken);
        }

        // === Style ===

        /// <summary>
        /// Awaitable counterpart of <see cref="SetStyle"/>. Fetches the style
        /// JSON from <paramref name="styleUrl"/> and resolves once sources /
        /// sprites / glyphs have finished initialising.
        /// </summary>
        public async Awaitable SetStyleAsync(string styleUrl, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MapLibreMap is not initialized yet -- await LoadAsync() first.");
            cancellationToken.ThrowIfCancellationRequested();

            string json;
            try { json = await FetchTextAsync(styleUrl); }
            catch (Exception e)
            {
                FireError($"SetStyle fetch failed: {e.Message}");
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(json))
            {
                FireError("SetStyle: failed to load style from URL");
                return;
            }
            await ApplyStyleJsonInternalAsync(json);
        }

        /// <summary>
        /// Awaitable counterpart of <see cref="SetStyleJson"/>.
        /// </summary>
        public async Awaitable SetStyleJsonAsync(string json, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("MapLibreMap is not initialized yet -- await LoadAsync() first.");
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyStyleJsonInternalAsync(json);
        }

        // === Image loading ===

        /// <summary>
        /// Awaitable counterpart of <see cref="LoadImage"/>. Resolves with
        /// the decoded <see cref="Texture2D"/> or throws on HTTP / decode
        /// failure.
        /// </summary>
        public async Awaitable<Texture2D> LoadImageAsync(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("LoadImage: url is null or empty", nameof(url));

            var transformed = RequestTransformer.Apply(TransformRequest, url, ResourceKind.Image);
            if (transformed.Abort)
                throw new Exception("LoadImage: aborted by transformRequest");

            using var request = UnityWebRequestTexture.GetTexture(transformed.Url, true);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            if (transformed.Headers != null)
            {
                foreach (var kvp in transformed.Headers)
                {
                    if (!string.IsNullOrEmpty(kvp.Key))
                        request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
                }
            }
            using var ctr = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(request.Abort)
                : default;
            await request.SendAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"{url}: {request.error}");

            var texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // === Internal helpers ===

        private Awaitable WaitForEventInternal(MapEventType type, CancellationToken cancellationToken)
        {
            var tcs = new AwaitableCompletionSource();
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Awaitable;
            }

            Action<MapEvent> handler = null;
            CancellationTokenRegistration ctr = default;

            handler = _ =>
            {
                Off(type, handler);
                ctr.Dispose();
                tcs.TrySetResult();
            };

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(() =>
                {
                    Off(type, handler);
                    tcs.TrySetCanceled();
                });
            }

            On(type, handler);
            return tcs.Awaitable;
        }

        private Awaitable WaitForAnimationEndInternal(CancellationToken cancellationToken)
        {
            var tcs = new AwaitableCompletionSource();

            // EaseTo / FlyTo etc. invoke Animator.EaseTo synchronously and
            // either start animating (IsAnimating=true) or short-circuit
            // (no MapState yet). If the animator is not running by the time
            // we get here, there is nothing to wait for.
            if (Animator == null || !Animator.IsAnimating)
            {
                tcs.TrySetResult();
                return tcs.Awaitable;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return tcs.Awaitable;
            }

            Action onEnd = null;
            CancellationTokenRegistration ctr = default;

            onEnd = () =>
            {
                Animator.OnAnimationEnd -= onEnd;
                ctr.Dispose();
                tcs.TrySetResult();
            };

            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(() =>
                {
                    if (Animator != null) Animator.OnAnimationEnd -= onEnd;
                    tcs.TrySetCanceled();
                });
            }

            Animator.OnAnimationEnd += onEnd;
            return tcs.Awaitable;
        }

    }
}
