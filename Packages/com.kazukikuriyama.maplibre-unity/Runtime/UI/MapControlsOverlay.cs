using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using MapLibre.Unity.CameraControl;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.UI
{
    /// <summary>
    /// UI Toolkit overlay for map controls.
    /// Provides a compass button, debug info display, and attribution display.
    /// Requires a UIDocument component on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MapControlsOverlay : MonoBehaviour
    {
        [SerializeField] private MapLibreMap _map;

        [Header("Built-in controls (matching MapLibre GL JS)")]
        [Tooltip("Zoom + / - buttons (MapLibre GL JS NavigationControl).")]
        [SerializeField] private bool _showNavigationControls = true;
        [Tooltip("Toggle window fullscreen via Screen.fullScreen (MapLibre GL JS FullscreenControl).")]
        [SerializeField] private bool _showFullscreenControl = false;
        [Tooltip("Locate-me button using Unity LocationService (MapLibre GL JS GeolocateControl).")]
        [SerializeField] private bool _showGeolocateControl = false;
        [Tooltip("Zoom level the geolocate button flies to. 14 ≈ neighbourhood.")]
        [SerializeField] private float _geolocateTargetZoom = 14f;
        [Tooltip("Continuously recenter on user location (MapLibre GL JS trackUserLocation). When false, the button does a one-shot easeTo on click.")]
        [SerializeField] private bool _geolocateTrackUserLocation = false;
        [Tooltip("Bottom-left scale bar showing real-world distance at current zoom (MapLibre GL JS ScaleControl).")]
        [SerializeField] private bool _showScaleControl = true;
        [Tooltip("Maximum scale-bar length in screen pixels. The bar shrinks to a 'nice' round-number distance no longer than this.")]
        [SerializeField] private float _scaleMaxWidthPixels = 100f;
        [Tooltip("Use imperial units (ft / mi) instead of metric (m / km) for the scale bar.")]
        [SerializeField] private bool _scaleImperial = false;

        public void SetMap(MapLibreMap map) => _map = map;

        private Button _compassButton;
        private Button _zoomInButton;
        private Button _zoomOutButton;
        private Button _fullscreenButton;
        private Label _fullscreenGlyph;
        private Button _geolocateButton;
        private Label _geolocateGlyph;
        private VisualElement _compassNeedle;
        private Label _attributionLabel;
        private Label _debugInfoLabel;
        private VisualElement _boxZoomRect;
        private VisualElement _scaleBar;
        private Label _scaleLabel;
        private float _animatedBearing;
        private bool _isAnimating;
        private bool _geolocateRunning;
        private GeolocateState _geolocateState = GeolocateState.Idle;
        private bool _geolocateTrackingActive;

        // Ref-counted attribution store. Two sources frequently share the same
        // attribution string (e.g. multiple OSM-derived tilesets), so we keep
        // each unique string in the visible row exactly once but only drop it
        // from the row when the last referencing source goes away.
        private readonly Dictionary<string, int> _attributionCounts = new();

        private enum GeolocateState
        {
            /// <summary>Default -- no location request in flight.</summary>
            Idle,
            /// <summary>LocationService is initialising or waiting for a fix.</summary>
            Waiting,
            /// <summary>A fix has been obtained (or is being tracked).</summary>
            Active,
            /// <summary>Permission denied or LocationService failed.</summary>
            Error,
        }

        private const float AnimationSpeed = 8f;
        private const float AnimationThreshold = 0.1f;

        /// <summary>
        /// Clear all attributions. Called when the style is replaced.
        /// </summary>
        public void ClearAttributions()
        {
            _attributionCounts.Clear();
            UpdateAttributionText();
        }

        /// <summary>
        /// Add an attribution string. HTML tags are stripped for display.
        /// Identical strings from multiple sources are counted: the row shows
        /// each unique string once, and <see cref="RemoveAttribution"/> only
        /// removes the visible entry when the last reference is dropped.
        /// </summary>
        public void AddAttribution(string attribution)
        {
            if (string.IsNullOrWhiteSpace(attribution)) return;
            var plain = StripHtmlTags(attribution);
            if (_attributionCounts.TryGetValue(plain, out int n))
            {
                _attributionCounts[plain] = n + 1;
                return; // Already visible -- no row change.
            }
            _attributionCounts[plain] = 1;
            UpdateAttributionText();
        }

        /// <summary>
        /// Remove one reference to <paramref name="attribution"/>. The string
        /// only disappears from the visible row when its reference count
        /// reaches zero -- useful when multiple sources share an attribution
        /// (e.g. several OSM-derived tile sources).
        /// </summary>
        public void RemoveAttribution(string attribution)
        {
            if (string.IsNullOrWhiteSpace(attribution)) return;
            var plain = StripHtmlTags(attribution);
            if (!_attributionCounts.TryGetValue(plain, out int n)) return;
            if (n <= 1)
            {
                _attributionCounts.Remove(plain);
                UpdateAttributionText();
            }
            else
            {
                _attributionCounts[plain] = n - 1;
            }
        }

        private bool _uiBuilt;

        private void Update()
        {
            if (!_uiBuilt)
            {
                var uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null || uiDocument.rootVisualElement == null) return;
                BuildUI(uiDocument.rootVisualElement);
                _uiBuilt = true;
            }

            UpdateCompass();
            UpdateDebugInfo();
            UpdateScaleBar();
            UpdateFullscreenGlyph();
            UpdateGeolocateTracking();
        }

        private void BuildUI(VisualElement root)
        {
            // Pick-mode none on root so map input passes through
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            // Build debug info label (coordinates & zoom)
            BuildDebugInfoLabel(root);

            // Build attribution label
            BuildAttributionLabel(root);

            // Stack the right-side button column. Top → bottom: zoom-in, zoom-out,
            // compass, geolocate. The compass alone keeps its original spot when
            // navigation controls are off so layouts don't shift unexpectedly.
            float buttonTop = 10f;
            float buttonGap = 4f;
            float buttonSize = 28f;

            if (_showNavigationControls)
            {
                _zoomInButton = BuildNavigationButton("zoom-in", "Zoom in", "+", () => _map?.ZoomIn());
                _zoomInButton.style.top = buttonTop;
                _zoomInButton.style.right = 10;
                root.Add(_zoomInButton);
                buttonTop += buttonSize + buttonGap;

                // Use ASCII '-' (U+002D) rather than the typographically nicer
                // '−' (U+2212 MINUS SIGN) -- the latter is missing from Unity's
                // built-in LegacyRuntime.ttf, so WebGL Player builds (which
                // fall back to that font) would render the zoom-out label
                // blank.
                _zoomOutButton = BuildNavigationButton("zoom-out", "Zoom out", "-", () => _map?.ZoomOut());
                _zoomOutButton.style.top = buttonTop;
                _zoomOutButton.style.right = 10;
                root.Add(_zoomOutButton);
                buttonTop += buttonSize + buttonGap;
            }

            // Build compass button
            _compassButton = new Button { name = "compass-button", tooltip = "Reset bearing to north" };
            _compassButton.style.position = Position.Absolute;
            _compassButton.style.top = buttonTop;
            _compassButton.style.right = 10;
            _compassButton.style.width = 28;
            _compassButton.style.height = 28;
            _compassButton.style.borderTopLeftRadius = 14;
            _compassButton.style.borderTopRightRadius = 14;
            _compassButton.style.borderBottomLeftRadius = 14;
            _compassButton.style.borderBottomRightRadius = 14;
            _compassButton.style.backgroundColor = new Color(1f, 1f, 1f, 0.9f);
            _compassButton.style.borderTopWidth = 1;
            _compassButton.style.borderBottomWidth = 1;
            _compassButton.style.borderLeftWidth = 1;
            _compassButton.style.borderRightWidth = 1;
            _compassButton.style.borderTopColor = new Color(0, 0, 0, 0.15f);
            _compassButton.style.borderBottomColor = new Color(0, 0, 0, 0.15f);
            _compassButton.style.borderLeftColor = new Color(0, 0, 0, 0.15f);
            _compassButton.style.borderRightColor = new Color(0, 0, 0, 0.15f);
            _compassButton.style.paddingTop = 0;
            _compassButton.style.paddingBottom = 0;
            _compassButton.style.paddingLeft = 0;
            _compassButton.style.paddingRight = 0;
            _compassButton.clicked += OnCompassClicked;

            // Needle container
            _compassNeedle = new VisualElement { name = "compass-needle" };
            _compassNeedle.pickingMode = PickingMode.Ignore;
            _compassNeedle.style.position = Position.Absolute;
            _compassNeedle.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
            _compassNeedle.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            _compassNeedle.style.alignItems = Align.Center;
            _compassNeedle.style.justifyContent = Justify.Center;

            // North triangle (red) - CSS border trick
            var north = new VisualElement { name = "compass-north", pickingMode = PickingMode.Ignore };
            north.style.width = 0;
            north.style.height = 0;
            north.style.borderLeftWidth = 3.5f;
            north.style.borderRightWidth = 3.5f;
            north.style.borderBottomWidth = 10;
            north.style.borderLeftColor = Color.clear;
            north.style.borderRightColor = Color.clear;
            north.style.borderBottomColor = new Color(0.8f, 0.2f, 0.2f, 1f);
            north.style.marginBottom = 1;

            // South triangle (gray)
            var south = new VisualElement { name = "compass-south", pickingMode = PickingMode.Ignore };
            south.style.width = 0;
            south.style.height = 0;
            south.style.borderLeftWidth = 3.5f;
            south.style.borderRightWidth = 3.5f;
            south.style.borderTopWidth = 10;
            south.style.borderLeftColor = Color.clear;
            south.style.borderRightColor = Color.clear;
            south.style.borderTopColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            south.style.marginTop = 1;

            _compassNeedle.Add(north);
            _compassNeedle.Add(south);
            _compassButton.Add(_compassNeedle);
            root.Add(_compassButton);
            buttonTop += buttonSize + buttonGap;

            if (_showFullscreenControl)
            {
                _fullscreenButton = BuildNavigationButton("fullscreen", "Toggle fullscreen",
                    GetFullscreenGlyph(), OnFullscreenClicked);
                _fullscreenButton.style.top = buttonTop;
                _fullscreenButton.style.right = 10;
                _fullscreenGlyph = _fullscreenButton.Q<Label>();
                root.Add(_fullscreenButton);
                buttonTop += buttonSize + buttonGap;
            }

            if (_showGeolocateControl)
            {
                _geolocateButton = BuildNavigationButton("geolocate", "Show my location", "◎",
                    OnGeolocateClicked);
                _geolocateButton.style.top = buttonTop;
                _geolocateButton.style.right = 10;
                _geolocateGlyph = _geolocateButton.Q<Label>();
                root.Add(_geolocateButton);
                buttonTop += buttonSize + buttonGap;
                UpdateGeolocateGlyph();
            }

            if (_showScaleControl)
                BuildScaleBar(root);

            // Box-zoom selection rectangle. Hidden by default; positioned on demand
            // via UpdateBoxZoom() driven by MapInputHandler.OnBoxZoomDrag.
            _boxZoomRect = new VisualElement { name = "box-zoom-rect" };
            _boxZoomRect.pickingMode = PickingMode.Ignore;
            _boxZoomRect.style.position = Position.Absolute;
            _boxZoomRect.style.backgroundColor = new Color(0f, 0.55f, 1f, 0.18f);
            _boxZoomRect.style.borderTopWidth = 1;
            _boxZoomRect.style.borderBottomWidth = 1;
            _boxZoomRect.style.borderLeftWidth = 1;
            _boxZoomRect.style.borderRightWidth = 1;
            _boxZoomRect.style.borderTopColor = new Color(0f, 0.55f, 1f, 0.9f);
            _boxZoomRect.style.borderBottomColor = new Color(0f, 0.55f, 1f, 0.9f);
            _boxZoomRect.style.borderLeftColor = new Color(0f, 0.55f, 1f, 0.9f);
            _boxZoomRect.style.borderRightColor = new Color(0f, 0.55f, 1f, 0.9f);
            _boxZoomRect.style.display = DisplayStyle.None;
            root.Add(_boxZoomRect);
        }

        /// <summary>
        /// Show/update the box-zoom selection rectangle. Coordinates are in screen pixels
        /// (Y growing upward, as Unity reports them). Pass two diagonal corners.
        /// </summary>
        public void ShowBoxZoom(Vector2 startScreen, Vector2 currentScreen)
        {
            if (_boxZoomRect == null) return;
            float left = Mathf.Min(startScreen.x, currentScreen.x);
            float right = Mathf.Max(startScreen.x, currentScreen.x);
            // UI Toolkit y origin is the top of the panel, while Screen y is from the bottom.
            float topUi = Screen.height - Mathf.Max(startScreen.y, currentScreen.y);
            float bottomUi = Screen.height - Mathf.Min(startScreen.y, currentScreen.y);
            _boxZoomRect.style.left = left;
            _boxZoomRect.style.top = topUi;
            _boxZoomRect.style.width = right - left;
            _boxZoomRect.style.height = bottomUi - topUi;
            _boxZoomRect.style.display = DisplayStyle.Flex;
        }

        /// <summary>Hide the box-zoom selection rectangle.</summary>
        public void HideBoxZoom()
        {
            if (_boxZoomRect == null) return;
            _boxZoomRect.style.display = DisplayStyle.None;
        }

        // === Scale control (MapLibre GL JS ScaleControl) ===

        private void BuildScaleBar(VisualElement root)
        {
            // Bottom-left, vertically aligned with the attribution row on the
            // right (both sit at bottom:2). The container holds the label + bar
            // stacked, with the bar pinned to the bottom of the container so its
            // pixel row matches the attribution band.
            var container = new VisualElement { name = "scale-control" };
            container.pickingMode = PickingMode.Ignore;
            container.style.position = Position.Absolute;
            container.style.left = 8;
            container.style.bottom = 2;
            container.style.alignItems = Align.FlexStart;
            container.style.flexDirection = FlexDirection.Column;

            _scaleLabel = new Label("");
            _scaleLabel.pickingMode = PickingMode.Ignore;
            _scaleLabel.style.fontSize = 10;
            _scaleLabel.style.color = Color.white;
            _scaleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _scaleLabel.style.unityFontDefinition = StyleKeyword.None;
            var labelFont = SystemFontFallback.Resolve("Arial", 10);
            _scaleLabel.style.unityFont = new StyleFont(labelFont);
            // Cheap text outline by stacking a black drop shadow.
            _scaleLabel.style.textShadow = new StyleTextShadow(new TextShadow
            {
                color = Color.black,
                offset = new Vector2(1, 1),
                blurRadius = 0f,
            });
            _scaleLabel.style.marginBottom = 1;
            container.Add(_scaleLabel);

            _scaleBar = new VisualElement { name = "scale-bar" };
            _scaleBar.pickingMode = PickingMode.Ignore;
            _scaleBar.style.height = 6;
            _scaleBar.style.width = _scaleMaxWidthPixels;
            _scaleBar.style.borderBottomWidth = 2;
            _scaleBar.style.borderLeftWidth = 2;
            _scaleBar.style.borderRightWidth = 2;
            _scaleBar.style.borderBottomColor = Color.white;
            _scaleBar.style.borderLeftColor = Color.white;
            _scaleBar.style.borderRightColor = Color.white;
            // Faint background so the bar reads on light tile imagery too.
            _scaleBar.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            container.Add(_scaleBar);

            root.Add(container);
        }

        private void UpdateScaleBar()
        {
            if (_scaleBar == null || _map == null || _map.State == null || _map.MapCamera == null)
                return;

            float metersPerScreenPixel = ComputeMetersPerScreenPixel();
            if (metersPerScreenPixel <= 0f)
            {
                _scaleBar.style.display = DisplayStyle.None;
                return;
            }
            _scaleBar.style.display = DisplayStyle.Flex;

            float maxRealMeters = metersPerScreenPixel * _scaleMaxWidthPixels;
            (float chosenMeters, string text) = ChooseNiceScale(maxRealMeters, _scaleImperial);
            float chosenPixels = chosenMeters / metersPerScreenPixel;

            _scaleBar.style.width = Mathf.Clamp(chosenPixels, 4f, _scaleMaxWidthPixels);
            _scaleLabel.text = text;
        }

        private float ComputeMetersPerScreenPixel()
        {
            // Frustum height (world units) at the ground plane for our top-down camera.
            // 2 * cameraY * tan(fov/2) gives the visible vertical extent at Y=0 when
            // pitch is small. With pitch the scale varies across the screen; we use
            // the value at the camera's nadir, which is what MapLibre GL JS reports
            // for the centre-of-bottom anchor point too.
            var cam = _map.MapCamera;
            if (cam == null || Screen.height <= 0) return 0f;
            float cameraY = Mathf.Max(cam.transform.localPosition.y, 0.0001f);
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float frustumHeightWorld = 2f * cameraY * Mathf.Tan(fovRad * 0.5f);

            float zoom = _map.State.Zoom;
            float worldScale = CoordinateConversion.GetWorldScale(zoom); // Unity world units per mercator unit
            // mercator units per screen pixel
            float mercPerPixel = (frustumHeightWorld / Screen.height) / Mathf.Max(worldScale, 1e-6f);

            // Account for Web Mercator north-south distortion at the centre latitude.
            const double earthCircumference = 40075016.686;
            double latRad = _map.State.Center.Latitude * System.Math.PI / 180.0;
            double cosLat = System.Math.Cos(latRad);
            return (float)(mercPerPixel * earthCircumference * cosLat);
        }

        private static (float meters, string text) ChooseNiceScale(float maxMeters, bool imperial)
        {
            if (imperial)
            {
                const float metersPerFoot = 0.3048f;
                const float feetPerMile = 5280f;
                float maxFeet = maxMeters / metersPerFoot;
                if (maxFeet < feetPerMile)
                {
                    float niceFt = NiceNumber(maxFeet);
                    return (niceFt * metersPerFoot, $"{niceFt:0} ft");
                }
                float maxMiles = maxFeet / feetPerMile;
                float niceMi = NiceNumber(maxMiles);
                return (niceMi * feetPerMile * metersPerFoot, niceMi >= 1f ? $"{niceMi:0} mi" : $"{niceMi:0.#} mi");
            }
            if (maxMeters < 1000f)
            {
                float niceM = NiceNumber(maxMeters);
                return (niceM, $"{niceM:0} m");
            }
            float maxKm = maxMeters / 1000f;
            float niceKm = NiceNumber(maxKm);
            return (niceKm * 1000f, niceKm >= 1f ? $"{niceKm:0} km" : $"{niceKm:0.#} km");
        }

        /// <summary>
        /// Round <paramref name="value"/> down to the nearest 1/2/5 × 10^n. Standard
        /// "nice number" algorithm used by axis tick generators and MapLibre's own
        /// scale control.
        /// </summary>
        private static float NiceNumber(float value)
        {
            if (value <= 0f) return 0f;
            float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(value)));
            float fraction = value / power;
            float niceFraction;
            if (fraction >= 5f) niceFraction = 5f;
            else if (fraction >= 2f) niceFraction = 2f;
            else niceFraction = 1f;
            return niceFraction * power;
        }

        /// <summary>
        /// Helper to build a circular icon button with the same chrome as the compass.
        /// Uses a Label child so the glyph (text or unicode symbol) can be styled
        /// independently of the button's own padding.
        /// </summary>
        private static Button BuildNavigationButton(string name, string tooltip, string glyph,
            System.Action onClick)
        {
            var btn = new Button { name = name + "-button", tooltip = tooltip };
            btn.style.position = Position.Absolute;
            btn.style.width = 28;
            btn.style.height = 28;
            btn.style.borderTopLeftRadius = 14;
            btn.style.borderTopRightRadius = 14;
            btn.style.borderBottomLeftRadius = 14;
            btn.style.borderBottomRightRadius = 14;
            btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.9f);
            btn.style.borderTopWidth = 1;
            btn.style.borderBottomWidth = 1;
            btn.style.borderLeftWidth = 1;
            btn.style.borderRightWidth = 1;
            btn.style.borderTopColor = new Color(0, 0, 0, 0.15f);
            btn.style.borderBottomColor = new Color(0, 0, 0, 0.15f);
            btn.style.borderLeftColor = new Color(0, 0, 0, 0.15f);
            btn.style.borderRightColor = new Color(0, 0, 0, 0.15f);
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.alignItems = Align.Stretch;
            btn.style.justifyContent = Justify.Center;
            // Render the glyph via an explicit Label so we can pin a font even when
            // the Panel theme has no default font (TMP not used here).
            // The default theme applies asymmetric margins to .unity-label
            // (top:1, bottom:2) which would offset the glyph by ~0.5px from the
            // button centre. Reset margin / padding and stretch the label across
            // the full button so unityTextAlign centres the glyph cleanly.
            var label = new Label(glyph);
            label.pickingMode = PickingMode.Ignore;
            label.style.flexGrow = 1;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.fontSize = 18;
            label.style.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            label.style.unityFontDefinition = StyleKeyword.None;
            var font = SystemFontFallback.Resolve("Arial", 18);
            label.style.unityFont = new StyleFont(font);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.Add(label);

            btn.clicked += () => onClick?.Invoke();
            return btn;
        }

        // Fullscreen uses Unity's Screen.fullScreen toggle. On WebGL builds this
        // requests browser fullscreen for the canvas; on standalone it switches
        // between windowed and exclusive/borderless fullscreen depending on the
        // user's player settings. The button glyph swaps to reflect the new
        // state; we reread Screen.fullScreen each frame in case another system
        // (Alt+Enter, OS shortcut) changes it.
        private void OnFullscreenClicked()
        {
            Screen.fullScreen = !Screen.fullScreen;
            UpdateFullscreenGlyph();
        }

        private static string GetFullscreenGlyph()
        {
            // U+26F6 (square four corners) when windowed → enter fullscreen.
            // U+2922 (north-east/south-west arrow) when fullscreen → exit. Both
            // ship with the OS Arial font we already use for the other buttons.
            return Screen.fullScreen ? "⤢" : "⛶";
        }

        private void UpdateFullscreenGlyph()
        {
            if (_fullscreenGlyph == null) return;
            _fullscreenGlyph.text = GetFullscreenGlyph();
        }

        // Geolocate uses Unity's LocationService. The Input System package does not
        // provide GPS access -- LocationService remains the only path even on
        // projects that disable the legacy Input class. Permission prompts are
        // handled by Unity itself based on the platform manifest (Info.plist /
        // AndroidManifest UsesPermission).
        private void OnGeolocateClicked()
        {
            if (_map == null) return;
            if (_geolocateTrackUserLocation && _geolocateTrackingActive)
            {
                // Second click while tracking turns it off. Matches MapLibre GL JS:
                // active → background → off cycle is collapsed to a simple toggle
                // here because we have no separate "user moved the map" signal.
                StopGeolocateTracking();
                return;
            }
            if (_geolocateRunning) return;
            _ = GeolocateAsync();
        }

        private async Awaitable GeolocateAsync()
        {
            _geolocateRunning = true;
            SetGeolocateState(GeolocateState.Waiting);
            try
            {
#if UNITY_ANDROID
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                        UnityEngine.Android.Permission.FineLocation))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(
                        UnityEngine.Android.Permission.FineLocation);
                    // Best-effort wait for the permission dialog to resolve;
                    // Unity does not expose a callback for the dialog result.
                    float granted = 0f;
                    while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                               UnityEngine.Android.Permission.FineLocation) && granted < 10f)
                    {
                        granted += Time.unscaledDeltaTime;
                        await Awaitable.NextFrameAsync();
                    }
                    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                            UnityEngine.Android.Permission.FineLocation))
                    {
                        Debug.LogWarning("[MapControlsOverlay] Geolocate: permission denied");
                        SetGeolocateState(GeolocateState.Error);
                        return;
                    }
                }
#endif
                if (!UnityEngine.Input.location.isEnabledByUser)
                {
                    Debug.LogWarning("[MapControlsOverlay] Geolocate: location disabled by user");
                    SetGeolocateState(GeolocateState.Error);
                    return;
                }

                UnityEngine.Input.location.Start(10f, 10f);
                int waited = 0;
                while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && waited < 100)
                {
                    await Awaitable.WaitForSecondsAsync(0.1f);
                    waited++;
                }
                if (UnityEngine.Input.location.status != LocationServiceStatus.Running)
                {
                    Debug.LogWarning("[MapControlsOverlay] Geolocate: failed to start LocationService");
                    SetGeolocateState(GeolocateState.Error);
                    return;
                }

                var data = UnityEngine.Input.location.lastData;
                _map.EaseTo(new EaseToOptions
                {
                    Center = new LngLat(data.longitude, data.latitude),
                    Zoom = _geolocateTargetZoom,
                    Duration = 800f,
                });
                SetGeolocateState(GeolocateState.Active);

                if (_geolocateTrackUserLocation)
                {
                    _geolocateTrackingActive = true;
                    // The tracking routine runs forever until StopGeolocateTracking()
                    // clears the flag; LocationService keeps streaming updates.
                }
                else
                {
                    UnityEngine.Input.location.Stop();
                }
            }
            finally
            {
                _geolocateRunning = false;
            }
        }

        private void StopGeolocateTracking()
        {
            _geolocateTrackingActive = false;
            UnityEngine.Input.location.Stop();
            SetGeolocateState(GeolocateState.Idle);
        }

        private void UpdateGeolocateTracking()
        {
            if (!_geolocateTrackingActive) return;
            if (UnityEngine.Input.location.status != LocationServiceStatus.Running)
            {
                // Service died (signal lost, user revoked permission, etc.).
                _geolocateTrackingActive = false;
                SetGeolocateState(GeolocateState.Error);
                return;
            }
            var data = UnityEngine.Input.location.lastData;
            // Skip the recenter while an animation is mid-flight; otherwise
            // tracking would fight any pan/zoom in progress.
            if (_map == null || _map.Animator == null) return;
            if (!_map.Animator.IsAnimating)
            {
                _map.JumpTo(new JumpToOptions
                {
                    Center = new LngLat(data.longitude, data.latitude),
                });
            }
        }

        private void SetGeolocateState(GeolocateState next)
        {
            if (_geolocateState == next) return;
            _geolocateState = next;
            UpdateGeolocateGlyph();
        }

        private void UpdateGeolocateGlyph()
        {
            if (_geolocateGlyph == null) return;
            _geolocateGlyph.style.color = _geolocateState switch
            {
                GeolocateState.Active => new Color(0.13f, 0.45f, 0.86f, 1f),   // MapLibre GL JS active blue
                GeolocateState.Waiting => new Color(0.45f, 0.45f, 0.45f, 1f),  // muted while waiting for fix
                GeolocateState.Error => new Color(0.83f, 0.18f, 0.18f, 1f),    // red on permission/service failure
                _ => new Color(0.15f, 0.15f, 0.15f, 1f),                       // idle dark grey
            };
        }

        private void BuildDebugInfoLabel(VisualElement root)
        {
            _debugInfoLabel = new Label { name = "debug-info-label" };
            _debugInfoLabel.style.position = Position.Absolute;
            _debugInfoLabel.style.top = 10;
            _debugInfoLabel.style.left = 10;
            _debugInfoLabel.style.fontSize = 10;
            _debugInfoLabel.style.color = Color.white;
            _debugInfoLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            _debugInfoLabel.style.paddingTop = 4;
            _debugInfoLabel.style.paddingBottom = 4;
            _debugInfoLabel.style.paddingLeft = 8;
            _debugInfoLabel.style.paddingRight = 8;
            _debugInfoLabel.style.borderTopLeftRadius = 4;
            _debugInfoLabel.style.borderTopRightRadius = 4;
            _debugInfoLabel.style.borderBottomLeftRadius = 4;
            _debugInfoLabel.style.borderBottomRightRadius = 4;
            _debugInfoLabel.style.unityFontDefinition = StyleKeyword.None;
            var font = SystemFontFallback.Resolve("Arial", 10);
            _debugInfoLabel.style.unityFont = new StyleFont(font);
            _debugInfoLabel.pickingMode = PickingMode.Ignore;
            _debugInfoLabel.text = "";
            root.Add(_debugInfoLabel);
        }

        private void BuildAttributionLabel(VisualElement root)
        {
            _attributionLabel = new Label { name = "attribution-label" };
            _attributionLabel.style.position = Position.Absolute;
            _attributionLabel.style.bottom = 2;
            _attributionLabel.style.right = 4;
            _attributionLabel.style.fontSize = 10;
            _attributionLabel.style.color = Color.white;
            _attributionLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.8f);
            _attributionLabel.style.paddingTop = 2;
            _attributionLabel.style.paddingBottom = 2;
            _attributionLabel.style.paddingLeft = 4;
            _attributionLabel.style.paddingRight = 4;
            _attributionLabel.style.borderTopLeftRadius = 2;
            _attributionLabel.style.borderTopRightRadius = 2;
            _attributionLabel.style.borderBottomLeftRadius = 2;
            _attributionLabel.style.borderBottomRightRadius = 2;
            _attributionLabel.style.unityTextAlign = TextAnchor.LowerRight;
            // Explicitly set a font so text renders even without a PanelSettings theme
            _attributionLabel.style.unityFontDefinition = StyleKeyword.None;
            var font = SystemFontFallback.Resolve("Arial", 10);
            _attributionLabel.style.unityFont = new StyleFont(font);
            _attributionLabel.pickingMode = PickingMode.Ignore;
            _attributionLabel.text = "";
            root.Add(_attributionLabel);

            // Apply any attributions added before UI was built
            UpdateAttributionText();
        }

        private void UpdateAttributionText()
        {
            if (_attributionLabel == null) return;
            _attributionLabel.text = string.Join(" | ", _attributionCounts.Keys);
        }

        private static string StripHtmlTags(string html)
        {
            var stripped = Regex.Replace(html, "<.*?>", string.Empty);
            return WebUtility.HtmlDecode(stripped).Trim();
        }

        private void OnDisable()
        {
            if (_compassButton != null)
                _compassButton.clicked -= OnCompassClicked;
        }

        private void UpdateDebugInfo()
        {
            if (_map == null || _map.State == null || _debugInfoLabel == null) return;

            var center = _map.State.Center;
            var zoom = _map.State.Zoom;
            _debugInfoLabel.text = $"Lng: {center.Longitude:F4}  Lat: {center.Latitude:F4}  Zoom: {zoom:F2}";
        }

        private void UpdateCompass()
        {
            if (_map == null || _map.State == null || _compassNeedle == null) return;

            float targetBearing = _map.State.Bearing;

            if (_isAnimating)
            {
                _animatedBearing = Mathf.Lerp(_animatedBearing, targetBearing, Time.deltaTime * AnimationSpeed);
                if (Mathf.Abs(_animatedBearing - targetBearing) < AnimationThreshold)
                {
                    _animatedBearing = targetBearing;
                    _isAnimating = false;
                }
            }
            else
            {
                _animatedBearing = targetBearing;
            }

            _compassNeedle.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(-_animatedBearing)));
        }

        private void OnCompassClicked()
        {
            if (_map == null || _map.State == null) return;

            _isAnimating = true;
            _animatedBearing = _map.State.Bearing;
            _map.State.Bearing = 0f;
            _map.State.Pitch = 0f;
        }
    }
}
