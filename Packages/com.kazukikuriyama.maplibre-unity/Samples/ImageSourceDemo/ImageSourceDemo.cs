using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using UnityEngine;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Demonstrates the "image" source type: drape a Unity texture onto the map
    /// between four geographic corners.
    ///
    /// If no texture is assigned in the Inspector the sample falls back to a
    /// built-in Unity default texture (UnityWhite.png tinted in-shader via the
    /// raster-opacity property demonstration). Assign a Texture2D to override.
    /// </summary>
    [RequireComponent(typeof(MapLibreMap))]
    public class ImageSourceDemo : MonoBehaviour
    {
        [Header("Overlay image (assign a Unity built-in or project texture)")]
        [SerializeField] private Texture2D _overlayTexture;

        [Header("Overlay corners (lng, lat) -- default covers central Tokyo")]
        [SerializeField] private Vector2 _topLeft = new(139.70f, 35.71f);
        [SerializeField] private Vector2 _topRight = new(139.82f, 35.71f);
        [SerializeField] private Vector2 _bottomRight = new(139.82f, 35.65f);
        [SerializeField] private Vector2 _bottomLeft = new(139.70f, 35.65f);

        [Header("Layer")]
        [SerializeField] private string _sourceId = "overlay";
        [SerializeField] private string _layerId = "overlay-layer";
        [SerializeField, Range(0f, 1f)] private float _opacity = 0.7f;

        private MapLibreMap _map;

        private IEnumerator Start()
        {
            _map = GetComponent<MapLibreMap>();

            while (!_map.IsInitialized)
                yield return null;

            var texture = _overlayTexture != null ? _overlayTexture : CreateFallbackTexture();

            _map.AddImageSource(_sourceId, texture,
                new LngLat(_topLeft.x, _topLeft.y),
                new LngLat(_topRight.x, _topRight.y),
                new LngLat(_bottomRight.x, _bottomRight.y),
                new LngLat(_bottomLeft.x, _bottomLeft.y));

            _map.AddLayer(new LayerDefinition
            {
                Id = _layerId,
                Type = LayerType.Raster,
                Source = _sourceId,
                Paint = new Dictionary<string, object>
                {
                    { "raster-opacity", _opacity }
                }
            });
        }

        // Generate a visible checkerboard pattern when no texture is assigned,
        // so the sample works out-of-the-box with zero configuration.
        private static Texture2D CreateFallbackTexture()
        {
            const int size = 128;
            const int checkSize = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "ImageSourceDemo_Fallback",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            var a = new Color32(0x2E, 0x7D, 0x32, 0xFF);
            var b = new Color32(0xFF, 0xEB, 0x3B, 0xFF);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool even = ((x / checkSize) + (y / checkSize)) % 2 == 0;
                    pixels[y * size + x] = even ? a : b;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
