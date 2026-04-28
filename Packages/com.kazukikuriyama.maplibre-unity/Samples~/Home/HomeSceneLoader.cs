using System;
using System.Collections;
using System.Collections.Generic;
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
            // === Basic — start here ===
            new SampleEntry { Title = "Basic Raster Map",      Description = "Raster tiles, no styling",                                       SceneName = "BasicRasterMapScene",         Category = "Basic" },
            new SampleEntry { Title = "Vector Demo",           Description = "Basic vector tile rendering",                                    SceneName = "VectorDemoScene",             Category = "Basic" },
            new SampleEntry { Title = "Vector With Labels",    Description = "Vector tiles + text labels",                                     SceneName = "SampleScene",                 Category = "Basic" },

            // === Source — adding data dynamically ===
            new SampleEntry { Title = "GeoJSON",               Description = "Render a GeoJSON source",                                        SceneName = "GeoJsonDemoScene",            Category = "Source" },
            new SampleEntry { Title = "Runtime GeoJSON",       Description = "Update GeoJSON data at runtime",                                 SceneName = "RuntimeGeoJsonDemoScene",     Category = "Source" },
            new SampleEntry { Title = "Dynamic Source/Layer",  Description = "Add / remove sources and layers at runtime",                     SceneName = "DynamicSourceLayerDemoScene", Category = "Source" },
            new SampleEntry { Title = "PMTiles (HTTP Range)",  Description = "Stream a single-file PMTiles archive over HTTP Range — works on WebGL", SceneName = "PMTilesDemoScene",      Category = "Source" },
            new SampleEntry { Title = "Image Source",          Description = "Drape a single image between four geographic corners",            SceneName = "ImageSourceDemoScene",        Category = "Source" },

            // === Layer — every built-in layer type ===
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

            // === Text / Icon — symbol layer paths ===
            new SampleEntry { Title = "Font Test",             Description = "Multi-language font rendering",                                  SceneName = "FontTestScene",               Category = "Text/Icon" },
            new SampleEntry { Title = "Sprite Icon",           Description = "Sprite icon rendering",                                          SceneName = "SpriteIconTestScene",         Category = "Text/Icon" },
            new SampleEntry { Title = "Glyph URL",             Description = "SDF text via MapLibre glyph PBFs (no TMP font asset needed)",    SceneName = "GlyphUrlDemoScene",           Category = "Text/Icon" },

            // === UI ===
            new SampleEntry { Title = "Marker & Popup",        Description = "Marker / Popup API",                                             SceneName = "MarkerPopupDemoScene",        Category = "UI" },

            // === API — runtime style + camera mutation ===
            new SampleEntry { Title = "Style Switch",          Description = "Swap style.json at runtime",                                     SceneName = "StyleSwitchDemoScene",        Category = "API" },
            new SampleEntry { Title = "Fit Bounds",            Description = "fitBounds() camera helper",                                      SceneName = "FitBoundsDemoScene",          Category = "API" },
            new SampleEntry { Title = "Camera Animation",      Description = "easeTo / flyTo / zoomTo / rotateTo / panBy / jumpTo side-by-side", SceneName = "CameraAnimationDemoScene",   Category = "API" },
            new SampleEntry { Title = "Sky & Light",           Description = "setSky / setLight driven by a time-of-day slider",               SceneName = "SkyAndLightDemoScene",        Category = "API" },
            new SampleEntry { Title = "Free Camera",           Description = "Drive the map camera externally (Cinemachine / orbit rig)",     SceneName = "FreeCameraDemoScene",         Category = "API" },
            new SampleEntry { Title = "Paint Property",        Description = "Update paint properties at runtime",                             SceneName = "PropertyDemoScene",           Category = "API" },
            new SampleEntry { Title = "Query Features",        Description = "queryRenderedFeatures",                                          SceneName = "QueryFeaturesDemoScene",      Category = "API" },
            new SampleEntry { Title = "Feature State Hover",   Description = "Hover / click polygons via setFeatureState",                     SceneName = "FeatureStateHoverDemoScene",  Category = "API" },

            // === Custom — extension points for user code ===
            new SampleEntry { Title = "Custom Layer",          Description = "Render user-supplied geometry via ICustomLayer",                 SceneName = "CustomLayerDemoScene",        Category = "Custom" },
            new SampleEntry { Title = "Custom Source",         Description = "Serve tiles from a user backend via ICustomRasterSource",       SceneName = "CustomSourceDemoScene",       Category = "Custom" },

            // === Event ===
            new SampleEntry { Title = "Pointer Event",         Description = "Pointer event handling",                                         SceneName = "PointerEventDemoScene",       Category = "Event" },
            new SampleEntry { Title = "Event Demo",            Description = "All map events",                                                 SceneName = "EventDemoScene",              Category = "Event" },
        };

        public const string HomeSceneName = "HomeScene";

        private Font _font;
        private VisualElement _licenseModal;

        private IEnumerator Start()
        {
            _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
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
            container.Add(scroll);

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
                    fontSize = 28,
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
                    fontSize = 14,
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

            float contentHeight = LicenseButtonBlockHeight + TitleBlockHeight + SubtitleBlockHeight;
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
        }

        private const float CardHeight = 60f;
        private const float CardSpacing = 6f;
        private const float WheelScrollStep = 20f;
        // Section label = marginTop 10 + minHeight 26 + paddingBottom 4 + border 1 + marginBottom 6
        private const float SectionLabelBlockHeight = 47f;
        private const float ListBottomMargin = 12f;
        private const float TitleBlockMargin = 4f;
        private const float TitleBlockHeight = 40f;      // minHeight 36 + marginBottom 4
        private const float SubtitleBlockMargin = 16f;
        private const float SubtitleBlockHeight = 36f;   // minHeight 20 + marginBottom 16

        private Label MakeSectionLabel(string text)
        {
            return new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    fontSize = 16,
                    minHeight = 26,
                    width = Length.Percent(100),
                    color = new Color(0.55f, 0.78f, 1f, 1f),
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginTop = 10,
                    marginBottom = 6,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(1, 1, 1, 0.08f),
                    paddingBottom = 4,
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
                    borderTopLeftRadius = 8,
                    borderTopRightRadius = 8,
                    borderBottomLeftRadius = 8,
                    borderBottomRightRadius = 8,
                    paddingTop = 10,
                    paddingBottom = 10,
                    paddingLeft = 14,
                    paddingRight = 12,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.08f),
                    borderBottomColor = new Color(1, 1, 1, 0.08f),
                    borderLeftColor = new Color(1, 1, 1, 0.08f),
                    borderRightColor = new Color(1, 1, 1, 0.08f),
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
                    fontSize = 14,
                    minHeight = 22,
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
                    fontSize = 12,
                    minHeight = 18,
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
                    minHeight = 32,
                    height = 32,
                    width = 88,
                    marginTop = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginLeft = 0,
                    paddingTop = 0,
                    paddingBottom = 0,
                    paddingLeft = 0,
                    paddingRight = 0,
                    flexShrink = 0,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0.22f, 0.5f, 0.85f, 1f),
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    borderTopWidth = 0,
                    borderBottomWidth = 0,
                    borderLeftWidth = 0,
                    borderRightWidth = 0,
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            card.Add(openBtn);

            return card;
        }

        private const float LicensePanelWidth = 560f;
        private const float LicensePanelHeight = 360f;
        private const float LicenseHeaderHeight = 44f;
        private const float LicenseBodyLineHeight = 16f;
        private const float LicenseButtonHeight = 24f;
        private const float LicenseButtonMarginBottom = 8f;
        private const float LicenseButtonBlockHeight = LicenseButtonHeight + LicenseButtonMarginBottom;

        private static readonly (string Title, string Body)[] LicenseSections =
        {
            ("MapLibre Unity",
             "License: MIT License\n" +
             "Copyright (c) 2026 Kazuki Kuriyama\n" +
             "A Unity map rendering library based on the MapLibre Style Spec.\n" +
             "See LICENSE and THIRD_PARTY_NOTICES.md for full terms."),

            ("Newtonsoft.Json (Json.NET)",
             "Package: com.unity.nuget.newtonsoft-json (3.2.1)\n" +
             "License: MIT License\n" +
             "Copyright (c) 2007 James Newton-King\n" +
             "https://www.newtonsoft.com/json"),

            ("Unity Input System",
             "Package: com.unity.inputsystem (1.18.0)\n" +
             "License: Unity Companion License\n" +
             "https://docs.unity3d.com/Packages/com.unity.inputsystem@1.18/"),

            ("TextMeshPro",
             "Package: com.unity.textmeshpro (Unity 6 built-in)\n" +
             "License: Unity Companion License"),

            ("Recommended Fonts (user-provided)",
             "MapLibre Unity does not bundle any font files.\n" +
             "Recommended open-source fonts for map labels:\n" +
             "  Noto Sans JP / Noto Sans CJK — SIL Open Font License 1.1\n" +
             "  Roboto — Apache License 2.0\n" +
             "See THIRD_PARTY_NOTICES.md for the full list and terms."),
        };

        private Button MakeLicenseButton(VisualElement root)
        {
            return new Button(() => ShowLicenseModal(root))
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
