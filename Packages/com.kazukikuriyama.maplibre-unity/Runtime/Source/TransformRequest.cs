using System.Collections.Generic;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Resource kinds passed to a <see cref="RequestTransformFunction"/>.
    /// Mirrors the ResourceType enum in MapLibre GL JS so user code can switch
    /// behavior per resource (e.g. inject a token only for tile requests).
    /// </summary>
    public enum ResourceKind
    {
        Unknown,
        Style,
        Source,
        Tile,
        Glyphs,
        SpriteImage,
        SpriteJson,
        Image,
        Geojson
    }

    /// <summary>
    /// Result of transforming a request. Use <see cref="Abort"/> to signal that
    /// the request should not be sent (e.g. because credentials are missing).
    /// Headers, when non-null, are added to the outgoing request.
    /// </summary>
    public struct TransformedRequest
    {
        /// <summary>Final URL to send. May differ from the input URL.</summary>
        public string Url;

        /// <summary>Optional headers to add. Null skips header injection.</summary>
        public Dictionary<string, string> Headers;

        /// <summary>If true, the request is cancelled and no error is fired.</summary>
        public bool Abort;

        public static TransformedRequest Aborted => new() { Abort = true };

        public static TransformedRequest UrlOnly(string url) => new() { Url = url };

        public static TransformedRequest WithHeaders(string url, Dictionary<string, string> headers)
            => new() { Url = url, Headers = headers };
    }

    /// <summary>
    /// Hook called for every HTTP request issued by the map. Matches
    /// transformRequest in MapLibre GL JS. Use it to inject auth tokens,
    /// rewrite URLs, or proxy requests through a CORS gateway.
    /// </summary>
    /// <param name="url">Original request URL.</param>
    /// <param name="kind">What kind of resource is being fetched.</param>
    /// <returns>The transformed request descriptor.</returns>
    public delegate TransformedRequest RequestTransformFunction(string url, ResourceKind kind);

    /// <summary>
    /// Helpers for applying a <see cref="RequestTransformFunction"/> to a URL.
    /// </summary>
    public static class RequestTransformer
    {
        /// <summary>
        /// Apply the transform if non-null; return <see cref="TransformedRequest.UrlOnly"/>
        /// otherwise. Centralized so source implementations all behave identically.
        /// </summary>
        public static TransformedRequest Apply(RequestTransformFunction transform,
            string url, ResourceKind kind)
        {
            if (transform == null) return TransformedRequest.UrlOnly(url);
            var result = transform(url, kind);
            // Treat an empty URL as an explicit abort so callers don't have to
            // distinguish two failure modes.
            if (!result.Abort && string.IsNullOrEmpty(result.Url)) result.Abort = true;
            return result;
        }
    }
}
