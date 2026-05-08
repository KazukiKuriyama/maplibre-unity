using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Style
{
    /// <summary>
    /// Metadata for a single sprite entry in the atlas.
    /// Matches the MapLibre sprite JSON format.
    /// </summary>
    public class SpriteEntry
    {
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        [JsonProperty("width")] public int Width;
        [JsonProperty("height")] public int Height;
        [JsonProperty("pixelRatio")] public float PixelRatio = 1f;
        [JsonProperty("sdf")] public bool Sdf;
    }

    /// <summary>
    /// Options for <see cref="SpriteAtlas.AddImage"/>. Matches MapLibre GL JS
    /// map.addImage() options object.
    /// </summary>
    public struct ImageOptions
    {
        /// <summary>Ratio of pixels in the image to CSS pixels (default 1).</summary>
        public float PixelRatio;
        /// <summary>Whether the image is an SDF (signed distance field) sprite.</summary>
        public bool Sdf;

        public static ImageOptions Default => new() { PixelRatio = 1f, Sdf = false };
    }

    /// <summary>
    /// Manages sprite textures and provides UV lookup by icon name.
    /// Primary texture is loaded from the style's sprite URL on startup.
    /// Individual textures can also be added at runtime via AddImage().
    /// </summary>
    public class SpriteAtlas
    {
        /// <summary>Texture of the atlas loaded from the style sprite URL (may be null).</summary>
        public Texture2D Texture { get; private set; }

        private Dictionary<string, SpriteEntry> _entries;

        private struct RuntimeImage
        {
            public Texture2D Texture;
            public SpriteEntry Entry;
        }

        private readonly Dictionary<string, RuntimeImage> _runtimeImages = new();

        public SpriteAtlas(Texture2D texture, Dictionary<string, SpriteEntry> entries)
        {
            Texture = texture;
            _entries = entries ?? new Dictionary<string, SpriteEntry>();
        }

        /// <summary>
        /// Bundled entries dictionary (excludes runtime images). Exposed so that
        /// <see cref="Replace"/> callers can pass it through.
        /// </summary>
        public Dictionary<string, SpriteEntry> RawEntries => _entries;

        /// <summary>
        /// Look up a sprite by name and return its entry, UV rect, and the texture
        /// that contains it. Runtime images take precedence over the atlas so
        /// applications can override a bundled icon.
        /// UV rect is in Unity texture space (Y=0 at bottom).
        /// </summary>
        public bool TryGetEntry(string iconName, out SpriteEntry entry, out Rect uvRect, out Texture2D texture)
        {
            entry = null;
            uvRect = default;
            texture = null;
            if (string.IsNullOrEmpty(iconName)) return false;

            if (_runtimeImages.TryGetValue(iconName, out var runtime))
            {
                entry = runtime.Entry;
                texture = runtime.Texture;
                // Runtime images fill their own texture -- UV covers the full [0,1].
                uvRect = new Rect(0f, 0f, 1f, 1f);
                return true;
            }

            if (_entries.TryGetValue(iconName, out entry) && Texture != null)
            {
                float texW = Texture.width;
                float texH = Texture.height;
                // MapLibre sprite JSON uses Y-down (top-left origin).
                // Unity textures use Y-up (bottom-left origin).
                uvRect = new Rect(
                    entry.X / texW,
                    1f - (entry.Y + entry.Height) / texH,
                    entry.Width / texW,
                    entry.Height / texH
                );
                texture = Texture;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Backwards-compatible overload. Returns the atlas texture implicitly so
        /// older callers work unchanged -- but they will see a null texture when the
        /// entry is a runtime image. Prefer the 4-out overload for new code.
        /// </summary>
        public bool TryGetEntry(string iconName, out SpriteEntry entry, out Rect uvRect)
        {
            return TryGetEntry(iconName, out entry, out uvRect, out _);
        }

        public bool HasIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return false;
            return _runtimeImages.ContainsKey(iconName) || _entries.ContainsKey(iconName);
        }

        /// <summary>
        /// Register a runtime image under <paramref name="iconName"/>. Later
        /// <see cref="TryGetEntry"/> calls for that name return this texture.
        /// The caller retains ownership of the texture.
        /// Matches map.addImage(id, image, options) in MapLibre GL JS.
        /// </summary>
        public void AddImage(string iconName, Texture2D texture, ImageOptions options = default)
        {
            if (string.IsNullOrEmpty(iconName) || texture == null) return;
            if (options.PixelRatio <= 0f) options.PixelRatio = 1f;

            _runtimeImages[iconName] = new RuntimeImage
            {
                Texture = texture,
                Entry = new SpriteEntry
                {
                    X = 0,
                    Y = 0,
                    Width = texture.width,
                    Height = texture.height,
                    PixelRatio = options.PixelRatio,
                    Sdf = options.Sdf,
                },
            };
        }

        /// <summary>
        /// Replace an existing runtime image. Same as AddImage but fails silently
        /// if the image doesn't exist. Matches map.updateImage() in MapLibre GL JS.
        /// </summary>
        public bool UpdateImage(string iconName, Texture2D texture)
        {
            if (string.IsNullOrEmpty(iconName) || !_runtimeImages.ContainsKey(iconName)) return false;
            var existing = _runtimeImages[iconName];
            existing.Texture = texture;
            existing.Entry.Width = texture != null ? texture.width : 0;
            existing.Entry.Height = texture != null ? texture.height : 0;
            _runtimeImages[iconName] = existing;
            return true;
        }

        /// <summary>
        /// Remove a runtime image. Atlas-bundled images cannot be removed.
        /// Matches map.removeImage() in MapLibre GL JS.
        /// </summary>
        public bool RemoveImage(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return false;
            return _runtimeImages.Remove(iconName);
        }

        /// <summary>
        /// Return all known icon names (atlas + runtime). Matches map.listImages().
        /// </summary>
        public List<string> ListImages()
        {
            var list = new List<string>(_entries.Count + _runtimeImages.Count);
            foreach (var key in _entries.Keys) list.Add(key);
            foreach (var key in _runtimeImages.Keys)
            {
                // A runtime image may shadow an atlas entry; avoid duplicates.
                if (!_entries.ContainsKey(key)) list.Add(key);
            }
            return list;
        }

        /// <summary>
        /// Parse sprite JSON string into entry dictionary.
        /// </summary>
        public static Dictionary<string, SpriteEntry> ParseJson(string json)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, SpriteEntry>>(json);
        }

        /// <summary>
        /// Replace the atlas texture and entries in place. Existing renderers retain
        /// their reference to this <see cref="SpriteAtlas"/> instance, so the swap
        /// becomes visible without recreating renderers. Runtime images added via
        /// <see cref="AddImage"/> are preserved across the swap.
        /// </summary>
        public void Replace(Texture2D texture, Dictionary<string, SpriteEntry> entries)
        {
            Texture = texture;
            _entries = entries ?? new Dictionary<string, SpriteEntry>();
        }
    }
}
