using System;
using MapLibre.Unity.Style;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Source
{
    /// <summary>
    /// Image source that places a single image between four geographic corners.
    /// Matches MapLibre Style Spec "image" source type.
    /// The texture can be loaded from a URL (via style JSON "url") or assigned
    /// directly from an in-Unity asset via <see cref="SetTexture"/>.
    /// </summary>
    public class ImageSource : ISource
    {
        public string Id { get; private set; }
        public SourceDefinition Definition { get; private set; }

        /// <summary>Loaded texture. Null until load completes or SetTexture is called.</summary>
        public Texture2D Texture { get; private set; }

        /// <summary>Corner coordinates as [top-left, top-right, bottom-right, bottom-left].</summary>
        public LngLat[] Corners { get; private set; }

        /// <summary>
        /// Fired whenever the texture or corners change so renderers can refresh.
        /// </summary>
        public event Action OnChanged;

        private MonoBehaviour _coroutineHost;
        private bool _ownsTexture;
        private RequestTransformFunction _transformRequest;

        public void Initialize(string id, SourceDefinition definition, MonoBehaviour coroutineHost,
            RequestTransformFunction transformRequest = null)
        {
            Id = id;
            Definition = definition;
            _coroutineHost = coroutineHost;
            _transformRequest = transformRequest;
            Corners = ParseCorners(definition.Coordinates);
        }

        /// <summary>
        /// Fetch the image from <see cref="SourceDefinition.Url"/>. The source owns the
        /// resulting texture and disposes it with the source.
        /// </summary>
        public async Awaitable LoadFromUrlAsync()
        {
            if (string.IsNullOrEmpty(Definition.Url))
                return;

            var transformed = RequestTransformer.Apply(_transformRequest, Definition.Url, ResourceKind.Image);
            if (transformed.Abort)
                return;

            using var request = UnityWebRequestTexture.GetTexture(transformed.Url, true);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            ApplyHeaders(request, transformed.Headers);
            await request.SendAsync();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ReplaceTexture(DownloadHandlerTexture.GetContent(request), ownsTexture: true);
            }
            else
            {
                Debug.LogError($"[ImageSource] '{Id}' load failed: {request.error}");
            }
        }

        private static void ApplyHeaders(UnityWebRequest request,
            System.Collections.Generic.Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }

        /// <summary>
        /// Assign a texture that the caller owns (e.g. a Unity asset). The source
        /// does not destroy externally owned textures on Dispose.
        /// </summary>
        public void SetTexture(Texture2D texture)
        {
            ReplaceTexture(texture, ownsTexture: false);
        }

        /// <summary>
        /// Update the 4 corner coordinates at runtime. Matches map.getSource(id).setCoordinates()
        /// in MapLibre GL JS.
        /// </summary>
        public void SetCoordinates(LngLat topLeft, LngLat topRight, LngLat bottomRight, LngLat bottomLeft)
        {
            Corners = new[] { topLeft, topRight, bottomRight, bottomLeft };
            OnChanged?.Invoke();
        }

        private void ReplaceTexture(Texture2D texture, bool ownsTexture)
        {
            if (Texture != null && _ownsTexture)
                UnityEngine.Object.Destroy(Texture);
            Texture = texture;
            _ownsTexture = ownsTexture;
            if (Texture != null)
            {
                Texture.wrapMode = TextureWrapMode.Clamp;
                Texture.filterMode = FilterMode.Bilinear;
            }
            OnChanged?.Invoke();
        }

        private static LngLat[] ParseCorners(double[][] coordinates)
        {
            if (coordinates == null || coordinates.Length < 4)
                return null;
            var corners = new LngLat[4];
            for (int i = 0; i < 4; i++)
            {
                var c = coordinates[i];
                if (c == null || c.Length < 2) return null;
                corners[i] = new LngLat(c[0], c[1]);
            }
            return corners;
        }

        public void Dispose()
        {
            if (Texture != null && _ownsTexture)
                UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }
}
