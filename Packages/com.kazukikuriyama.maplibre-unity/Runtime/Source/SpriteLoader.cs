using System;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Loads a MapLibre sprite atlas (PNG texture + JSON metadata) from a URL.
    /// The sprite URL from the style JSON is used as a base; this loader fetches
    /// {base}.png and {base}.json (or @2x variants for high-DPI displays).
    /// </summary>
    public static class SpriteLoader
    {
        /// <summary>
        /// Load sprite atlas from the given base URL. Throws
        /// <see cref="SpriteLoadException"/> on fetch / parse failure.
        /// </summary>
        public static async Awaitable<SpriteAtlas> LoadAsync(string spriteBaseUrl,
            RequestTransformFunction transformRequest = null)
        {
            if (string.IsNullOrEmpty(spriteBaseUrl))
                throw new SpriteLoadException("Sprite URL is null or empty");

            // Determine suffix: use @2x for high-DPI displays
            string suffix = Screen.dpi > 200 ? "@2x" : "";
            string pngUrl = spriteBaseUrl + suffix + ".png";
            string jsonUrl = spriteBaseUrl + suffix + ".json";

            var (texture, pngError) = await FetchTextureAsync(pngUrl, transformRequest, ResourceKind.SpriteImage);
            var (jsonText, jsonError) = await FetchTextAsync(jsonUrl, transformRequest, ResourceKind.SpriteJson);

            // If @2x failed, retry without suffix
            if ((texture == null || jsonText == null) && !string.IsNullOrEmpty(suffix))
            {
                if (texture == null)
                {
                    pngUrl = spriteBaseUrl + ".png";
                    (texture, pngError) = await FetchTextureAsync(pngUrl, transformRequest, ResourceKind.SpriteImage);
                }
                if (jsonText == null)
                {
                    jsonUrl = spriteBaseUrl + ".json";
                    (jsonText, jsonError) = await FetchTextAsync(jsonUrl, transformRequest, ResourceKind.SpriteJson);
                }
            }

            if (texture == null)
                throw new SpriteLoadException($"Failed to load sprite PNG: {pngError}");
            if (string.IsNullOrEmpty(jsonText))
                throw new SpriteLoadException($"Failed to load sprite JSON: {jsonError}");

            Dictionary<string, SpriteEntry> entries;
            try
            {
                entries = SpriteAtlas.ParseJson(jsonText);
            }
            catch (Exception e)
            {
                throw new SpriteLoadException($"Failed to parse sprite JSON: {e.Message}", e);
            }

            return new SpriteAtlas(texture, entries);
        }

        private static async Awaitable<(Texture2D texture, string error)> FetchTextureAsync(
            string url, RequestTransformFunction transformRequest, ResourceKind kind)
        {
            var transformed = RequestTransformer.Apply(transformRequest, url, kind);
            if (transformed.Abort)
                return (null, "Sprite request aborted by transformRequest");

            using var request = UnityWebRequestTexture.GetTexture(transformed.Url);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            ApplyHeaders(request, transformed.Headers);
            await request.SendAsync();
            return request.result == UnityWebRequest.Result.Success
                ? (DownloadHandlerTexture.GetContent(request), null)
                : (null, request.error);
        }

        private static async Awaitable<(string text, string error)> FetchTextAsync(
            string url, RequestTransformFunction transformRequest, ResourceKind kind)
        {
            var transformed = RequestTransformer.Apply(transformRequest, url, kind);
            if (transformed.Abort)
                return (null, "Sprite request aborted by transformRequest");

            using var request = UnityWebRequest.Get(transformed.Url);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            ApplyHeaders(request, transformed.Headers);
            await request.SendAsync();
            return request.result == UnityWebRequest.Result.Success
                ? (request.downloadHandler.text, null)
                : (null, request.error);
        }

        private static void ApplyHeaders(UnityWebRequest request,
            Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Thrown by <see cref="SpriteLoader.LoadAsync"/> on fetch or parse failure.
    /// </summary>
    public class SpriteLoadException : Exception
    {
        public SpriteLoadException(string message) : base(message) { }
        public SpriteLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
