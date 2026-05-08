using System.Collections.Generic;
using MapLibre.Unity.Source.PMTiles;
using MapLibre.Unity.Style;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Loads a public PMTiles archive (Protomaps' Florence sample) over
    /// HTTP Range requests, wires it up as a vector source, and adds a
    /// minimal water + roads + buildings style on top. Demonstrates that
    /// PMTiles works on any Unity build target including WebGL -- the
    /// archive header + directory tree are streamed as the user pans, no
    /// SQLite or native plugin required.
    /// </summary>
    public class PMTilesDemo : MonoBehaviour
    {
        [SerializeField] private PanelSettings _panelSettings;

        // Public Protomaps demo archive -- ~6.5 MB and Range-enabled.
        // Hosted on github.io which supports HTTP Range out of the box, so
        // we never download the whole file just to read the header. Replace
        // with your own .pmtiles URL or a StreamingAssets path to ship
        // offline maps with the build.
        private const string ArchiveUrl =
            "https://pmtiles.io/protomaps(vector)ODbL_firenze.pmtiles";

        // Florence centre -- matches the bundled archive's footprint.
        private static readonly LngLat FlorenceCenter = new(11.255, 43.770);

        private MapLibreMap _map;
        private Font _font;
        private Label _statusLabel;

        private async void Start()
        {
            _map = GetComponent<MapLibreMap>();
            if (_map == null)
            {
                Debug.LogError("[PMTilesDemo] MapLibreMap not found");
                return;
            }
            await _map.LoadAsync();

            _font = SystemFontFallback.Resolve("Arial", 16);
            BuildUi();

            _map.JumpTo(new MapLibre.Unity.CameraControl.JumpToOptions
            {
                Center = FlorenceCenter, Zoom = 9f,
            });

            SetStatus("Opening PMTiles archive…");
            PMTilesArchive archive;
            try
            {
                archive = new PMTilesArchive(ArchiveUrl);
            }
            catch (System.Exception e)
            {
                // PMTilesArchive ctor throws on empty / malformed URLs.
                SetStatus($"PMTiles URL rejected: {e.Message}", error: true);
                Debug.LogError($"[PMTilesDemo] ctor failed: {e}");
                return;
            }

            // OpenAsync throws PMTilesException on failure. The 30 s HTTP
            // timeout inside PMTilesArchive guards against unreachable hosts
            // so this await can't sit forever even on a dead URL.
            try
            {
                await archive.OpenAsync();
                SetStatus($"Header read -- type={archive.Header.TileType}, " +
                          $"zoom {archive.Header.MinZoom}-{archive.Header.MaxZoom}");
                AttachArchive(archive);
            }
            catch (System.Exception e)
            {
                SetStatus($"PMTiles open failed: {e.Message}", error: true);
                Debug.LogError($"[PMTilesDemo] {e}");
            }
        }

        private void AttachArchive(PMTilesArchive archive)
        {
            // Vector PMTiles → wire up via PMTilesVectorSource so the
            // existing PBF parser / expression / paint pipeline picks it up.
            // Raster archives use PMTilesRasterSource -- same lifecycle.
            if (archive.Header.TileType != PMTilesType.Mvt)
            {
                SetStatus($"Archive is {archive.Header.TileType}, not MVT -- " +
                          "swap PMTilesVectorSource for PMTilesRasterSource", error: true);
                return;
            }

            _map.AddSource("firenze", new PMTilesVectorSource(archive));

            // Layers expressed against the Protomaps schema. Florence has
            // "water", "roads", and "buildings" source layers; we render
            // those on top of the OSM raster basemap from the bootstrap
            // style. (No extra background layer here -- the style already
            // ships one.)
            _map.AddLayer(new LayerDefinition
            {
                Id = "pmtiles-water",
                Type = LayerType.Fill,
                Source = "firenze",
                SourceLayer = "water",
                Paint = new Dictionary<string, object> { { "fill-color", "#9bc4e6" } },
            });
            _map.AddLayer(new LayerDefinition
            {
                Id = "pmtiles-roads",
                Type = LayerType.Line,
                Source = "firenze",
                SourceLayer = "roads",
                Paint = new Dictionary<string, object>
                {
                    { "line-color", "#444" },
                    { "line-width", 1.0f },
                },
            });
            _map.AddLayer(new LayerDefinition
            {
                Id = "pmtiles-buildings",
                Type = LayerType.Fill,
                Source = "firenze",
                SourceLayer = "buildings",
                Paint = new Dictionary<string, object>
                {
                    { "fill-color", "#d8c5a6" },
                    { "fill-outline-color", "#a89579" },
                    { "fill-opacity", 0.85f },
                },
            });
            SetStatus("Tiles streaming over HTTP Range -- pan to load more.");
        }

        // ── UI ──

        private void BuildUi()
        {
            var go = new GameObject("PMTilesDemoUI");
            go.transform.SetParent(transform);
            var uiDoc = go.AddComponent<UIDocument>();
            if (_panelSettings != null) uiDoc.panelSettings = _panelSettings;
            uiDoc.sortingOrder = 200;
            _ = BuildAfterFrameAsync(uiDoc);
        }

        private async Awaitable BuildAfterFrameAsync(UIDocument uiDoc)
        {
            await Awaitable.NextFrameAsync();
            var root = uiDoc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.right = 0; root.style.bottom = 0;

            var panel = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = new Length(50, LengthUnit.Percent),
                    translate = new Translate(0, new Length(-50, LengthUnit.Percent)),
                    left = 14,
                    width = new Length(40, LengthUnit.Percent),
                    maxWidth = 420, minWidth = 280,
                    backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f),
                    borderTopLeftRadius = 10, borderTopRightRadius = 10,
                    borderBottomLeftRadius = 10, borderBottomRightRadius = 10,
                    paddingTop = 14, paddingBottom = 14, paddingLeft = 14, paddingRight = 14,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.12f),
                    borderBottomColor = new Color(1, 1, 1, 0.12f),
                    borderLeftColor = new Color(1, 1, 1, 0.12f),
                    borderRightColor = new Color(1, 1, 1, 0.12f),
                }
            };
            root.Add(panel);

            panel.Add(MakeLabel("PMTiles (HTTP Range)", 14, Color.white, FontStyle.Bold));
            panel.Add(MakeLabel(ArchiveUrl, 10, new Color(1, 1, 1, 0.55f)));

            _statusLabel = MakeLabel("…", 11, new Color(0.7f, 1f, 0.7f, 0.9f));
            _statusLabel.style.marginTop = 6;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(_statusLabel);
        }

        private Label MakeLabel(string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            l.style.unityFontStyleAndWeight = style;
            l.style.marginBottom = 4;
            l.style.unityFontDefinition = StyleKeyword.None;
            l.style.unityFont = new StyleFont(_font);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        private void SetStatus(string text, bool error = false)
        {
            Debug.Log($"[PMTilesDemo] {text}");
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.style.color = error
                ? new Color(1f, 0.5f, 0.5f, 0.95f)
                : new Color(0.7f, 1f, 0.7f, 0.9f);
        }
    }
}
