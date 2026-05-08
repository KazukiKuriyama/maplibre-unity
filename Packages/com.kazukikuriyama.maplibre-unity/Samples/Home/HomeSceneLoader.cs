using System;
using System.Collections;
using System.Collections.Generic;
using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Home scene loader. Lists all sample scenes in a UI Toolkit panel and
    /// loads the selected one via SceneManager.
    /// </summary>
    public class HomeSceneLoader : MonoBehaviour
    {
        [Serializable]
        public class SampleEntry
        {
            public string Title;
            public string Description;
            public string SceneName;
            public string Category;
        }

        [SerializeField] private PanelSettings _panelSettings;

        // Ordering note: entries are listed in learning-curve order within each
        // category, and categories themselves are arranged so a new user
        // working top-down moves from the simplest scene (Basic Raster) toward
        // the most advanced (Custom integrations). HomeSceneLoader groups by
        // Category at runtime, so the order here is what determines how
        // entries appear inside each section of the menu.
        [SerializeField]
        private List<SampleEntry> _samples = new()
        {
            // === Basic -- start here ===
            new SampleEntry { Title = "Basic Raster Map",      Description = "Raster tiles, no styling",                                       SceneName = "BasicRasterMapScene",         Category = "Basic" },
            new SampleEntry { Title = "Vector Demo",           Description = "Basic vector tile rendering",                                    SceneName = "VectorDemoScene",             Category = "Basic" },
            new SampleEntry { Title = "Vector With Labels",    Description = "Vector tiles + text labels",                                     SceneName = "SampleScene",                 Category = "Basic" },

            // === Source -- adding data dynamically ===
            new SampleEntry { Title = "GeoJSON",               Description = "Render a GeoJSON source",                                        SceneName = "GeoJsonDemoScene",            Category = "Source" },
            new SampleEntry { Title = "Runtime GeoJSON",       Description = "Update GeoJSON data at runtime",                                 SceneName = "RuntimeGeoJsonDemoScene",     Category = "Source" },
            new SampleEntry { Title = "Dynamic Source/Layer",  Description = "Add / remove sources and layers at runtime",                     SceneName = "DynamicSourceLayerDemoScene", Category = "Source" },
            new SampleEntry { Title = "PMTiles (HTTP Range)",  Description = "Stream a single-file PMTiles archive over HTTP Range -- works on WebGL", SceneName = "PMTilesDemoScene",      Category = "Source" },
            new SampleEntry { Title = "Image Source",          Description = "Drape a single image between four geographic corners",            SceneName = "ImageSourceDemoScene",        Category = "Source" },
            new SampleEntry { Title = "Prefab Source",         Description = "Anchor 3D primitive prefabs to LngLat coordinates",              SceneName = "PrefabSourceDemoScene",       Category = "Source" },
            new SampleEntry { Title = "Particle Source",       Description = "Anchor ParticleSystem effects to LngLat (smoke / sparkle / burst)", SceneName = "ParticleSourceDemoScene",  Category = "Source" },

            // === Layer -- every built-in layer type ===
            new SampleEntry { Title = "Circle Layer",          Description = "Circle layer rendering",                                         SceneName = "CircleDemoScene",             Category = "Layer" },
            new SampleEntry { Title = "Dash Line",             Description = "Dashed line via line-dasharray",                                 SceneName = "DashLineTestScene",           Category = "Layer" },
            new SampleEntry { Title = "Line Labels",           Description = "Text placement along curved lines",                              SceneName = "LineLabelsDemoScene",         Category = "Layer" },
            new SampleEntry { Title = "Fill Extrusion",        Description = "Extruded 3D buildings",                                          SceneName = "FillExtrusionDemoScene",      Category = "Layer" },
            new SampleEntry { Title = "Heatmap",               Description = "Heatmap layer",                                                  SceneName = "HeatmapDemoScene",            Category = "Layer" },
            new SampleEntry { Title = "Hillshade",             Description = "DEM-based hillshade relief",                                     SceneName = "HillshadeDemoScene",          Category = "Layer" },
            new SampleEntry { Title = "Terrain",               Description = "DEM-based 3D terrain",                                           SceneName = "TerrainDemoScene",            Category = "Layer" },
            new SampleEntry { Title = "Per-feature Pattern",   Description = "Different fill-pattern sprites per feature via [\"match\", ...]", SceneName = "PerFeaturePatternDemoScene",  Category = "Layer" },
            new SampleEntry { Title = "Color Interpolation",   Description = "interpolate vs interpolate-hcl vs interpolate-lab side-by-side", SceneName = "ColorInterpolationDemoScene", Category = "Layer" },
            new SampleEntry { Title = "Line Gradient",         Description = "line-gradient driven by line-progress (lineMetrics: true)",      SceneName = "LineGradientDemoScene",       Category = "Layer" },
            new SampleEntry { Title = "Symbol Translate",      Description = "text-translate / icon-translate offset (map vs viewport anchor)", SceneName = "SymbolTranslateDemoScene",   Category = "Layer" },
            new SampleEntry { Title = "Circle Pitch",          Description = "circle-pitch-alignment × circle-pitch-scale combinations side-by-side", SceneName = "CirclePitchDemoScene",  Category = "Layer" },
            new SampleEntry { Title = "Background Pattern",    Description = "background-pattern with a runtime-registered sprite",             SceneName = "BackgroundPatternDemoScene",  Category = "Layer" },

            // === Text / Icon -- symbol layer paths ===
            new SampleEntry { Title = "Font Test",             Description = "Multi-language font rendering",                                  SceneName = "FontTestScene",               Category = "Text/Icon" },
            new SampleEntry { Title = "Sprite Icon",           Description = "Sprite icon rendering",                                          SceneName = "SpriteIconTestScene",         Category = "Text/Icon" },
            new SampleEntry { Title = "Glyph URL",             Description = "SDF text via MapLibre glyph PBFs (no TMP font asset needed)",    SceneName = "GlyphUrlDemoScene",           Category = "Text/Icon" },

            // === UI ===
            new SampleEntry { Title = "Marker & Popup",        Description = "Marker / Popup API",                                             SceneName = "MarkerPopupDemoScene",        Category = "UI" },

            // === API -- runtime style + camera mutation ===
            new SampleEntry { Title = "Style Switch",          Description = "Swap style.json at runtime",                                     SceneName = "StyleSwitchDemoScene",        Category = "API" },
            new SampleEntry { Title = "Fit Bounds",            Description = "fitBounds() camera helper",                                      SceneName = "FitBoundsDemoScene",          Category = "API" },
            new SampleEntry { Title = "Camera Animation",      Description = "easeTo / flyTo / zoomTo / rotateTo / panBy / jumpTo side-by-side", SceneName = "CameraAnimationDemoScene",   Category = "API" },
            new SampleEntry { Title = "Sky & Light",           Description = "setSky / setLight driven by a time-of-day slider",               SceneName = "SkyAndLightDemoScene",        Category = "API" },
            new SampleEntry { Title = "Free Camera",           Description = "Drive the map camera externally (Cinemachine / orbit rig)",     SceneName = "FreeCameraDemoScene",         Category = "API" },
            new SampleEntry { Title = "Paint Property",        Description = "Update paint properties at runtime",                             SceneName = "PropertyDemoScene",           Category = "API" },
            new SampleEntry { Title = "Query Features",        Description = "queryRenderedFeatures",                                          SceneName = "QueryFeaturesDemoScene",      Category = "API" },
            new SampleEntry { Title = "Feature State Hover",   Description = "Hover / click polygons via setFeatureState",                     SceneName = "FeatureStateHoverDemoScene",  Category = "API" },

            // === Custom -- extension points for user code ===
            new SampleEntry { Title = "Custom Layer",          Description = "Render user-supplied geometry via ICustomLayer",                 SceneName = "CustomLayerDemoScene",        Category = "Custom" },
            new SampleEntry { Title = "Custom Source",         Description = "Serve tiles from a user backend via ICustomRasterSource",       SceneName = "CustomSourceDemoScene",       Category = "Custom" },

            // === Event ===
            new SampleEntry { Title = "Pointer Event",         Description = "Pointer event handling",                                         SceneName = "PointerEventDemoScene",       Category = "Event" },
            new SampleEntry { Title = "Event Demo",            Description = "All map events",                                                 SceneName = "EventDemoScene",              Category = "Event" },
        };

        public const string HomeSceneName = "HomeScene";

        private Font _font;
        private VisualElement _licenseModal;
        private ScrollView _scroll;

        // Remember the scroll position across HomeScene → demo → HomeScene
        // round-trips within a single Play session. Reset on Play start (the
        // RuntimeInitializeOnLoadMethod below) so the position does not bleed
        // between sessions when "Reload Domain" is disabled in Enter Play Mode
        // settings -- with the default (domain reload on) the static would
        // already reset itself, but keeping the explicit reset makes the
        // behaviour identical in both modes.
        private static float s_savedScrollY;

        // Scene name of the most recently opened sample, used to highlight its
        // Open button so the user can spot where they were. Same Play-session
        // lifetime as s_savedScrollY.
        private static string s_lastOpenedSceneName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetSavedScrollOnPlayStart()
        {
            s_savedScrollY = 0f;
            s_lastOpenedSceneName = null;
        }

        private IEnumerator Start()
        {
            _font = SystemFontFallback.Resolve("Arial", 16);
            yield return null;
            BuildUI();
        }

        private void BuildUI()
        {
            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc == null)
                uiDoc = gameObject.AddComponent<UIDocument>();

            if (_panelSettings != null)
                uiDoc.panelSettings = _panelSettings;
            else if (uiDoc.panelSettings == null)
            {
                var fallback = Resources.FindObjectsOfTypeAll<PanelSettings>();
                if (fallback.Length > 0) uiDoc.panelSettings = fallback[0];
            }

            uiDoc.sortingOrder = 100;

            var root = uiDoc.rootVisualElement;
            if (root == null) return;
            root.Clear();
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;
            root.style.backgroundColor = new Color(0.06f, 0.07f, 0.1f, 1f);

            var container = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    alignItems = Align.Center,
                    paddingTop = 24,
                    paddingBottom = 24,
                    paddingLeft = 24,
                    paddingRight = 24,
                }
            };
            root.Add(container);

            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    width = Length.Percent(100),
                    maxWidth = 1200,
                }
            };
            _scroll = scroll;
            container.Add(scroll);
            SampleScrollViewStyle.Apply(scroll);
            // Inset the scrollable content so cards and the License button
            // don't touch the scrollbar or the panel edge. Top / left small,
            // right larger to clear the slim scrollbar (6px bar + 6px margin).
            scroll.contentContainer.style.paddingTop = 4;
            scroll.contentContainer.style.paddingBottom = 4;
            scroll.contentContainer.style.paddingLeft = 8;
            scroll.contentContainer.style.paddingRight = 16;

            // ScrollView's default wheel handler calls ReadSingleLineHeight, which NREs when the
            // PanelSettings has no PanelTextSettings. Intercept in the capture phase and drive
            // the scroller manually.
            scroll.RegisterCallback<WheelEvent>(evt =>
            {
                scroll.verticalScroller.value += evt.delta.y * WheelScrollStep;
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            scroll.Add(MakeLicenseButton(root));

            var title = new Label("MapLibre Unity Samples")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 22,
                    minHeight = TitleBlockHeight - TitleBlockMargin,
                    flexShrink = 0,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = TitleBlockMargin,
                    whiteSpace = WhiteSpace.Normal,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            scroll.Add(title);

            var subtitle = new Label("Select a sample to open the scene")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 12,
                    minHeight = SubtitleBlockHeight - SubtitleBlockMargin,
                    flexShrink = 0,
                    color = new Color(1, 1, 1, 0.6f),
                    unityTextAlign = TextAnchor.MiddleCenter,
                    marginBottom = SubtitleBlockMargin,
                    whiteSpace = WhiteSpace.Normal,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            scroll.Add(subtitle);

            var grouped = new Dictionary<string, List<SampleEntry>>();
            foreach (var entry in _samples)
            {
                var cat = string.IsNullOrEmpty(entry.Category) ? "Other" : entry.Category;
                if (!grouped.TryGetValue(cat, out var list))
                {
                    list = new List<SampleEntry>();
                    grouped[cat] = list;
                }
                list.Add(entry);
            }

            // contentContainer has paddingTop+Bottom=8 (set above) inside the
            // border box, so include it here or the bottom of the last card
            // gets clipped against the explicit height.
            float contentHeight = LicenseButtonBlockHeight + TitleBlockHeight + SubtitleBlockHeight + 8f;
            foreach (var kv in grouped)
            {
                var sectionLabel = MakeSectionLabel(kv.Key);
                sectionLabel.style.flexShrink = 0;
                scroll.Add(sectionLabel);
                contentHeight += SectionLabelBlockHeight;

                int cardCount = kv.Value.Count;
                float listHeight = cardCount * CardHeight + (cardCount - 1) * CardSpacing;
                var list = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Column,
                        marginBottom = ListBottomMargin,
                        flexShrink = 0,
                        height = listHeight,
                    }
                };
                scroll.Add(list);
                contentHeight += listHeight + ListBottomMargin;

                foreach (var entry in kv.Value)
                    list.Add(MakeCard(entry));
            }

            // contentContainer's flex layout caps at the viewport height in this panel setup,
            // so the vertical scroller ends up with highValue=0. Force the content height to
            // the known total so the scroller has a usable range.
            scroll.contentContainer.style.flexGrow = 0;
            scroll.contentContainer.style.flexShrink = 0;
            scroll.contentContainer.style.height = contentHeight;

            // Restore the previous scroll position once the ScrollView has
            // laid out -- verticalScroller.highValue is 0 until the first
            // GeometryChangedEvent, so applying any non-zero saved value here
            // would silently clamp to 0. Self-unregister to keep the listener
            // single-shot.
            if (s_savedScrollY > 0f)
            {
                void RestoreOnce(GeometryChangedEvent _)
                {
                    scroll.UnregisterCallback<GeometryChangedEvent>(RestoreOnce);
                    scroll.verticalScroller.value =
                        Mathf.Min(s_savedScrollY, scroll.verticalScroller.highValue);
                }
                scroll.RegisterCallback<GeometryChangedEvent>(RestoreOnce);
            }
        }

        private void OnDisable()
        {
            // Capture before SceneManager tears down the UIDocument -- fires on
            // both demo-scene transitions and Play-stop. The static field is
            // reset at Play start so a stale value from a prior session never
            // leaks back in.
            if (_scroll != null)
                s_savedScrollY = _scroll.verticalScroller.value;
        }

        private const float CardHeight = 52f;
        private const float CardSpacing = 4f;
        private const float WheelScrollStep = 20f;
        // Section label = marginTop 8 + minHeight 22 + paddingBottom 2 + border 1 + marginBottom 4
        private const float SectionLabelBlockHeight = 37f;
        private const float ListBottomMargin = 10f;
        private const float TitleBlockMargin = 2f;
        private const float TitleBlockHeight = 32f;      // minHeight 30 + marginBottom 2
        private const float SubtitleBlockMargin = 12f;
        private const float SubtitleBlockHeight = 28f;   // minHeight 16 + marginBottom 12

        private Label MakeSectionLabel(string text)
        {
            return new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 13,
                    minHeight = 22,
                    width = Length.Percent(100),
                    color = new Color(0.55f, 0.78f, 1f, 1f),
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 8,
                    marginBottom = 4,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(1, 1, 1, 0.06f),
                    paddingBottom = 2,
                    whiteSpace = WhiteSpace.Normal,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
        }

        private VisualElement MakeCard(SampleEntry entry)
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = CardHeight,
                    marginBottom = CardSpacing,
                    backgroundColor = new Color(0.13f, 0.15f, 0.2f, 1f),
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    paddingTop = 8,
                    paddingBottom = 8,
                    paddingLeft = 12,
                    paddingRight = 10,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.06f),
                    borderBottomColor = new Color(1, 1, 1, 0.06f),
                    borderLeftColor = new Color(1, 1, 1, 0.06f),
                    borderRightColor = new Color(1, 1, 1, 0.06f),
                }
            };

            var textColumn = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1,
                    flexDirection = FlexDirection.Column,
                    justifyContent = Justify.Center,
                    height = CardHeight - 20f,
                    marginRight = 10,
                }
            };
            card.Add(textColumn);

            var titleLabel = new Label(entry.Title)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 13,
                    minHeight = 18,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    whiteSpace = WhiteSpace.Normal,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            textColumn.Add(titleLabel);

            var descLabel = new Label(entry.Description)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 11,
                    minHeight = 14,
                    color = new Color(1, 1, 1, 0.65f),
                    whiteSpace = WhiteSpace.Normal,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            textColumn.Add(descLabel);

            var openBtn = new Button(() => LoadScene(entry))
            {
                text = "Open",
                style =
                {
                    minHeight = 26,
                    height = 26,
                    width = 70,
                    marginTop = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginLeft = 0,
                    paddingTop = 0,
                    paddingBottom = 0,
                    paddingLeft = 0,
                    paddingRight = 0,
                    flexShrink = 0,
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0.22f, 0.5f, 0.85f, 1f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            // Highlight the most recently opened sample's button in amber so
            // it stands out against the blue palette of the other cards.
            bool isLastOpened = !string.IsNullOrEmpty(s_lastOpenedSceneName)
                && entry.SceneName == s_lastOpenedSceneName;
            if (isLastOpened)
            {
                ApplyButtonHoverStates(openBtn,
                    normal: new Color(0.90f, 0.55f, 0.18f, 1f),
                    hover:  new Color(0.96f, 0.63f, 0.26f, 1f),
                    active: new Color(0.72f, 0.42f, 0.12f, 1f));
            }
            else
            {
                ApplyButtonHoverStates(openBtn,
                    normal: new Color(0.22f, 0.50f, 0.85f, 1f),
                    hover:  new Color(0.30f, 0.58f, 0.92f, 1f),
                    active: new Color(0.16f, 0.40f, 0.72f, 1f));
            }
            card.Add(openBtn);

            return card;
        }

        // UI Toolkit Buttons set inline via C# don't pick up the default
        // :hover / :active USS rules -- drive the color manually from pointer
        // events. Tracks hover + press independently so leaving while pressed
        // reverts to normal and re-entering returns to active until released.
        private static void ApplyButtonHoverStates(VisualElement btn,
            Color normal, Color hover, Color active)
        {
            btn.style.backgroundColor = normal;
            bool hovering = false;
            bool pressed = false;

            void Apply()
            {
                btn.style.backgroundColor = pressed && hovering
                    ? active
                    : (hovering ? hover : normal);
            }

            btn.RegisterCallback<PointerEnterEvent>(_ => { hovering = true;  Apply(); });
            btn.RegisterCallback<PointerLeaveEvent>(_ => { hovering = false; Apply(); });
            btn.RegisterCallback<PointerDownEvent>(_ => { pressed  = true;  Apply(); });
            btn.RegisterCallback<PointerUpEvent>(_ =>   { pressed  = false; Apply(); });
            btn.RegisterCallback<PointerCancelEvent>(_ => { pressed = false; Apply(); });
        }

        private const float LicensePanelWidth = 560f;
        private const float LicensePanelHeight = 360f;
        private const float LicenseHeaderHeight = 44f;
        private const float LicenseBodyLineHeight = 16f;
        private const float LicenseButtonHeight = 24f;
        private const float LicenseButtonMarginBottom = 8f;
        private const float LicenseButtonBlockHeight = LicenseButtonHeight + LicenseButtonMarginBottom;

        // Embedded license sections shown in the License modal. Mirrors the
        // package's LICENSE (full MIT text) and the public sections of
        // THIRD_PARTY_NOTICES.md so the in-app display works without a
        // TextAsset assignment and survives stripping of unreferenced assets
        // in Player builds. Keep this list in sync with both files when
        // dependencies or attributions change.
        private static readonly (string Title, string Body)[] LicenseSections =
        {
            ("MapLibre Unity", @"License: MIT License

Copyright (c) 2026 Kazuki Kuriyama

A Unity map rendering library based on the MapLibre Style Spec.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."),

            ("MapLibre GL JS", @"MapLibre Unity is described as a C# port of
[MapLibre GL JS](https://github.com/maplibre/maplibre-gl-js). While the
runtime is an independent Unity / C# implementation, the public API
surface, Style Spec semantics, and several algorithms (raster / vector
tile pipeline, expression evaluation, camera animations) were modelled
after MapLibre GL JS. The upstream is distributed under the
3-Clause BSD License:

```
Copyright (c) 2020, MapLibre contributors
Copyright (c) 2014-2020, Mapbox

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name ""MapLibre"" nor the names of its contributors may be used
   to endorse or promote products derived from this software without specific
   prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS ""AS IS"" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```"),

            ("Newtonsoft.Json (Json.NET)", @"- **Package**: `com.unity.nuget.newtonsoft-json` (3.2.1)
- **License**: MIT License
- **Copyright**: Copyright (c) 2007 James Newton-King
- **URL**: https://www.newtonsoft.com/json"),

            ("Unity Input System", @"- **Package**: `com.unity.inputsystem` (1.18.0)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.inputsystem@1.18/"),

            ("TextMeshPro", @"- **Package**: `com.unity.textmeshpro` (Unity 6 built-in)
- **License**: Unity Companion License
- **URL**: https://docs.unity3d.com/Packages/com.unity.textmeshpro@latest"),

            ("Universal Render Pipeline (URP)", @"- **Package**: `com.unity.render-pipelines.universal` (17.3.0)
- **License**: Unity Companion License
- **Notes**: MapLibre Unity ships URP-tagged shaders; the URP package must
  be installed in the consuming project for tile rendering.
- **URL**: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.3/"),

            ("Unity UI (uGUI)", @"- **Package**: `com.unity.ugui` (2.0.0)
- **License**: Unity Companion License
- **Notes**: Used by sample overlays and the controls UI.
- **URL**: https://docs.unity3d.com/Packages/com.unity.ugui@latest"),

            ("mapbox/earcut", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/VectorTile/EarClipTriangulator.cs`
is a C# port of the [mapbox/earcut](https://github.com/mapbox/earcut)
JavaScript polygon triangulation library. It is redistributed under the
upstream ISC License:

```
ISC License

Copyright (c) 2016, Mapbox

Permission to use, copy, modify, and/or distribute this software for any purpose
with or without fee is hereby granted, provided that the above copyright notice
and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED ""AS IS"" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS
OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
THIS SOFTWARE.
```"),

            ("mapbox/supercluster (API surface only)", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/SuperclusterLite.cs`
exposes a small clustering API (`GetCluster`, `GetClusterChildren`,
`GetClusterLeaves`, `GetClusterExpansionZoom`) modelled after
[mapbox/supercluster](https://github.com/mapbox/supercluster) so that
GeoJSONSource cluster queries match the MapLibre GL JS surface. The
implementation is independent -- an O(N²) per-zoom merge rather than
the upstream KD-tree -- and no upstream source is incorporated. The
API attribution is provided here for nominative reference only."),

            ("mapbox/geojson-vt (clean-room reference)", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/GeoJsonToVectorTile.cs`
converts GeoJSON features into MVT-compatible tile data following the
shape of [mapbox/geojson-vt](https://github.com/mapbox/geojson-vt). The
projection / clipping logic is written from scratch against the public
GeoJSON (RFC 7946) and MVT specifications; no upstream source is
incorporated."),

            ("Mapbox Vector Tile (MVT) Specification", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/VectorTile/`
(`PbfReader.cs`, `VectorTileParser.cs`, `GeometryDecoder.cs`,
`VectorTileData.cs`) decodes vector tiles per the
[Mapbox Vector Tile Specification](https://github.com/mapbox/vector-tile-spec).
The specification document is © Mapbox and is published under
[CC-BY 3.0 US](https://creativecommons.org/licenses/by/3.0/us/). MVT is
also adopted as an OGC standard."),

            ("Mapbox Glyph PBF Format", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/GlyphPbfParser.cs`
implements the wire format defined by
[mapbox/glyph-pbf-composite](https://github.com/mapbox/glyph-pbf-composite)
(`proto/glyphs.proto`, BSD-3-Clause). Only the wire-compatible reader is
written from the schema; the upstream `.proto` file itself is not
redistributed."),

            ("Mapbox Terrain-RGB DEM Encoding", @"`Shaders/MapLibreHillshade.shader` and `Runtime/Terrain/TerrainManager.cs`
decode elevation tiles using the publicly documented
[Terrain-RGB encoding](https://docs.mapbox.com/data/tilesets/reference/mapbox-terrain-rgb-v1/)
formula `height = -10000 + ((R*65536 + G*256 + B) * 0.1)`. Mathematical
formulas are not subject to copyright; this notice is informational."),

            ("PMTiles v3 Specification", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/PMTiles/`
implements the
[PMTiles v3 specification](https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md)
maintained by Protomaps LLC (BSD-3-Clause). Implementation is
clean-room from the spec."),

            ("MBTiles Specification", @"`Packages/com.kazukikuriyama.maplibre-unity/Runtime/Source/MBTiles/`
implements the
[MBTiles specification](https://github.com/mapbox/mbtiles-spec) (BSD-3,
Mapbox). The library does **not** bundle a SQLite binding; users supply
their own `IMBTilesBackend` (e.g. Mono.Data.Sqlite, sqlite-net) so the
package stays binding-agnostic."),

            ("Trademark Notice", @"""Mapbox"" is a registered trademark of Mapbox, Inc.; ""MapLibre"" is a
trademark of the MapLibre community. References to these names in
this file and in source comments are nominative -- used only to
identify the public specifications and upstream projects this library
is interoperable with -- and do not imply any affiliation with,
endorsement by, or sponsorship from either organization."),

            ("Tile Providers", @"The bundled sample scenes reference public tile servers for illustration
purposes. The library itself fetches tiles via HTTP only -- no tile data is
redistributed. When forking a sample for shipping, confirm your use case
fits each provider's terms; for heavy / commercial traffic, switch to a
provider you have a commercial agreement with.

| Provider | URL template | License / Terms | Used by |
|---|---|---|---|
| OpenStreetMap Standard | `tile.openstreetmap.org/{z}/{x}/{y}.png` | [Tile Usage Policy](https://operations.osmfoundation.org/policies/tiles/) -- heavy / commercial use prohibited; ODbL on data | BasicRasterMap, MarkerPopupDemo, EventDemo, PointerEventDemo, TerrainDemo, GeoJsonDemo, CustomLayerDemo, FeatureStateHoverDemo, ColorInterpolationDemo, CustomSourceDemo, PerFeaturePatternDemo, ImageSourceDemo, DynamicSourceLayerDemo, PMTilesDemo, BackgroundPatternDemo, PrefabSourceDemo, ParticleSourceDemo, StyleSwitchDemo |
| CARTO Voyager | `basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png` | CC BY 3.0 (basemap design) + ODbL (data); see [carto.com/attributions](https://carto.com/attributions) | StyleSwitchDemo |
| OpenTopoMap | `a.tile.opentopomap.org/{z}/{x}/{y}.png` | CC-BY-SA 3.0 (style) + ODbL (data) + SRTM; non-commercial / educational use, see [opentopomap.org/about](https://opentopomap.org/about) | StyleSwitchDemo |
| OpenFreeMap (planet) | `tiles.openfreemap.org/planet/{z}/{x}/{y}.pbf` | OpenMapTiles schema + OSM (ODbL); free public service, see [openfreemap.org](https://openfreemap.org) | VectorDemo, CircleDemo, FillExtrusionDemo, HeatmapDemo, LineGradientDemo, DashLineTest, SkyAndLightDemo, SymbolTranslateDemo, etc. |
| MapLibre demotiles | `demotiles.maplibre.org/style.json` (vector) | OpenMapTiles schema + OSM (ODbL); demo service intended for testing | (previously StyleSwitchDemo, removed) |
| MapTiler Demo Tiles | `api.maptiler.com/...` | Requires API key for production use; some samples use the public demo key | (none currently) |

Each source's attribution string is encoded in the corresponding sample's
style JSON and is shown automatically by the `MapControlsOverlay`
attribution control. If you swap a source, update its attribution string
to match the new provider."),

            ("Recommended Fonts for CJK (Japanese, Chinese, Korean)", @"| Font | License | URL |
|------|---------|-----|
| Noto Sans JP | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans+JP |
| Noto Sans CJK | SIL Open Font License 1.1 | https://github.com/notofonts/noto-cjk |
| M PLUS 1p | SIL Open Font License 1.1 | https://fonts.google.com/specimen/M+PLUS+1p |
| Source Han Sans | SIL Open Font License 1.1 | https://github.com/adobe-fonts/source-han-sans |
| BIZ UDGothic | SIL Open Font License 1.1 | https://fonts.google.com/specimen/BIZ+UDGothic |"),

            ("Recommended Fonts for Latin / General", @"| Font | License | URL |
|------|---------|-----|
| Noto Sans | SIL Open Font License 1.1 | https://fonts.google.com/noto/specimen/Noto+Sans |
| Open Sans | SIL Open Font License 1.1 | https://fonts.google.com/specimen/Open+Sans |
| Roboto | Apache License 2.0 | https://fonts.google.com/specimen/Roboto |"),

            ("SIL Open Font License 1.1 (Summary)", @"Fonts under SIL OFL 1.1 can be:
- Used freely in any project (commercial or non-commercial)
- Bundled and redistributed with your application
- Modified and redistributed under the same license

The only restriction is that the fonts cannot be sold by themselves.
Full license text: https://openfontlicense.org/"),

            ("Font Setup", @"**Automatic (recommended):** Use the **MapLibre > Font Setup** menu in the Unity Editor.
This generates a TMP_FontAsset from OS system fonts and auto-assigns it to MapLibreMap.

**Manual:** If you prefer to use a specific font file:

1. Import the `.ttf` or `.otf` file into your Unity project
2. Open **Window > TextMeshPro > Font Asset Creator**
3. Select the source font and configure atlas settings
4. For CJK fonts, use **Dynamic** rendering mode to generate glyphs on demand
   (this avoids creating a massive static atlas for thousands of characters)
5. Assign the generated `TMP_FontAsset` to `MapLibreMap > Text > Symbol Font`

**Runtime fallback (development only):**
If no font is assigned, MapLibre Unity reads fonts already installed on the OS
(Hiragino on macOS, Yu Gothic on Windows, etc.) and uses them at runtime.
This is the same as a web browser or word processor displaying text with OS fonts --
no font files are copied or redistributed. This feature exists so that developers
can test without configuring a font first.

> **When distributing your application:**
> The runtime fallback depends on fonts being installed on the end user's machine.
> For reliable text display in a distributed app:
>
> 1. Use **MapLibre > Font Setup** to generate a font asset, or
> 2. Import an OFL-licensed font (e.g. Noto Sans JP) and create a TMP_FontAsset manually
>
> Then assign it to `MapLibreMap > Text > Symbol Font`.
> If you include a font file in your build, make sure its license allows redistribution.
> All fonts listed above under SIL OFL 1.1 permit this."),
        };

        private Button MakeLicenseButton(VisualElement root)
        {
            var btn = new Button(() => ShowLicenseModal(root))
            {
                text = "License",
                style =
                {
                    alignSelf = Align.FlexEnd,
                    flexShrink = 0,
                    height = LicenseButtonHeight,
                    width = 80,
                    marginTop = 0,
                    marginRight = 0,
                    marginLeft = 0,
                    marginBottom = LicenseButtonMarginBottom,
                    paddingTop = 0, paddingBottom = 0, paddingLeft = 0, paddingRight = 0,
                    fontSize = 11,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = new Color(1, 1, 1, 0.8f),
                    backgroundColor = new Color(0.13f, 0.15f, 0.2f, 0.85f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.18f),
                    borderBottomColor = new Color(1, 1, 1, 0.18f),
                    borderLeftColor = new Color(1, 1, 1, 0.18f),
                    borderRightColor = new Color(1, 1, 1, 0.18f),
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            ApplyButtonHoverStates(btn,
                normal: new Color(0.13f, 0.15f, 0.20f, 0.85f),
                hover:  new Color(0.20f, 0.23f, 0.30f, 0.95f),
                active: new Color(0.08f, 0.10f, 0.14f, 0.90f));
            return btn;
        }

        private void ShowLicenseModal(VisualElement root)
        {
            if (_licenseModal == null)
            {
                _licenseModal = BuildLicenseModal();
                root.Add(_licenseModal);
            }
            _licenseModal.style.display = DisplayStyle.Flex;
            _licenseModal.BringToFront();
        }

        private void HideLicenseModal()
        {
            if (_licenseModal != null) _licenseModal.style.display = DisplayStyle.None;
        }

        private VisualElement BuildLicenseModal()
        {
            var overlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    top = 0, left = 0, right = 0, bottom = 0,
                    backgroundColor = new Color(0, 0, 0, 0.6f),
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                }
            };
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == overlay) HideLicenseModal();
            });

            var panel = new VisualElement
            {
                style =
                {
                    width = LicensePanelWidth,
                    height = LicensePanelHeight,
                    flexShrink = 0,
                    flexGrow = 0,
                    backgroundColor = new Color(0.1f, 0.11f, 0.15f, 1f),
                    borderTopLeftRadius = 8,
                    borderTopRightRadius = 8,
                    borderBottomLeftRadius = 8,
                    borderBottomRightRadius = 8,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.2f),
                    borderBottomColor = new Color(1, 1, 1, 0.2f),
                    borderLeftColor = new Color(1, 1, 1, 0.2f),
                    borderRightColor = new Color(1, 1, 1, 0.2f),
                }
            };
            overlay.Add(panel);

            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = LicenseHeaderHeight,
                    flexShrink = 0,
                    paddingLeft = 16,
                    paddingRight = 8,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(1, 1, 1, 0.1f),
                }
            };
            panel.Add(header);

            var headerLabel = new Label("License Information")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    flexGrow = 1,
                    fontSize = 16,
                    minHeight = 22,
                    color = Color.white,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            header.Add(headerLabel);

            var closeBtn = new Button(HideLicenseModal)
            {
                text = "Close",
                style =
                {
                    width = 60,
                    height = 28,
                    paddingTop = 0, paddingBottom = 0, paddingLeft = 0, paddingRight = 0,
                    marginTop = 0, marginBottom = 0, marginLeft = 0, marginRight = 0,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0.22f, 0.24f, 0.3f, 1f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopWidth = 0, borderBottomWidth = 0,
                    borderLeftWidth = 0, borderRightWidth = 0,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            ApplyButtonHoverStates(closeBtn,
                normal: new Color(0.22f, 0.24f, 0.30f, 1f),
                hover:  new Color(0.30f, 0.32f, 0.38f, 1f),
                active: new Color(0.16f, 0.18f, 0.23f, 1f));
            header.Add(closeBtn);

            var licenseScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    paddingTop = 12,
                    paddingBottom = 12,
                    paddingLeft = 16,
                    paddingRight = 16,
                    overflow = Overflow.Hidden,
                }
            };
            panel.Add(licenseScroll);
            SampleScrollViewStyle.Apply(licenseScroll);

            licenseScroll.RegisterCallback<WheelEvent>(evt =>
            {
                licenseScroll.verticalScroller.value += evt.delta.y * WheelScrollStep;
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);

            float total = 0f;
            foreach (var section in LicenseSections)
            {
                var titleLabel = new Label(section.Title)
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        fontSize = 14,
                        minHeight = 22,
                        flexShrink = 0,
                        color = new Color(0.55f, 0.78f, 1f, 1f),
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 4,
                        borderBottomWidth = 1,
                        borderBottomColor = new Color(1, 1, 1, 0.1f),
                        paddingBottom = 2,
                        whiteSpace = WhiteSpace.Normal,
                        unityFontDefinition = StyleKeyword.None,
                        unityFont = new StyleFont(_font),
                    }
                };
                licenseScroll.Add(titleLabel);
                total += 22 + 4 + 2 + 1;

                int lineCount = section.Body.Split('\n').Length;
                float bodyHeight = lineCount * LicenseBodyLineHeight + 4;

                var body = new Label(section.Body)
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        fontSize = 12,
                        minHeight = bodyHeight,
                        flexShrink = 0,
                        color = new Color(1, 1, 1, 0.8f),
                        whiteSpace = WhiteSpace.Normal,
                        marginBottom = 14,
                        unityFontDefinition = StyleKeyword.None,
                        unityFont = new StyleFont(_font),
                    }
                };
                licenseScroll.Add(body);
                total += bodyHeight + 14;
            }

            licenseScroll.contentContainer.style.flexGrow = 0;
            licenseScroll.contentContainer.style.flexShrink = 0;
            licenseScroll.contentContainer.style.width = Length.Percent(100);
            licenseScroll.contentContainer.style.height = total;

            return overlay;
        }

        private void LoadScene(SampleEntry entry)
        {
            if (string.IsNullOrEmpty(entry.SceneName))
            {
                Debug.LogWarning($"[HomeSceneLoader] SceneName is empty for '{entry.Title}'");
                return;
            }

            if (!IsSceneInBuildSettings(entry.SceneName))
            {
                Debug.LogError($"[HomeSceneLoader] Scene '{entry.SceneName}' is not in Build Settings. Add it via File > Build Profiles.");
                return;
            }

            Debug.Log($"[HomeSceneLoader] Loading: {entry.SceneName}");
            s_lastOpenedSceneName = entry.SceneName;
            SceneManager.LoadScene(entry.SceneName);
        }

        private static bool IsSceneInBuildSettings(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name == sceneName) return true;
            }
            return false;
        }
    }
}
