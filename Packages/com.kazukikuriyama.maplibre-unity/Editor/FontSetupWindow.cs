using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MapLibre.Unity.Editor
{
    /// <summary>
    /// Editor window for setting up a TMP_FontAsset from OS system fonts.
    /// Accessed via MapLibre > Font Setup.
    /// </summary>
    public class FontSetupWindow : EditorWindow
    {
        private List<string> _cjkFonts = new();
        private List<string> _allFonts = new();
        private Dictionary<string, string> _fontPathMap = new();
        private int _selectedIndex;
        private bool _showAllFonts;
        private Vector2 _scrollPos;

        private const string DefaultSavePath = "Assets/MapLibre/Fonts";

        /// <summary>Known CJK-capable font name fragments for filtering.</summary>
        private static readonly string[] CjkKeywords =
        {
            "Hiragino", "Yu Gothic", "Meiryo", "MS Gothic", "MS Mincho",
            "Noto Sans CJK", "Noto Sans JP", "Noto Sans KR", "Noto Sans SC", "Noto Sans TC",
            "Source Han", "M PLUS", "BIZ UD",
            "Arial Unicode", "SimSun", "SimHei", "Microsoft YaHei", "Malgun Gothic",
            "IPAex", "IPAmj", "Klee", "Tsukushi", "Toppan",
        };

        [MenuItem("MapLibre/Font Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<FontSetupWindow>("MapLibre Font Setup");
            window.minSize = new Vector2(420, 340);
        }

        private void OnEnable()
        {
            RefreshFontList();
        }

        private void RefreshFontList()
        {
            _cjkFonts ??= new List<string>();
            _allFonts ??= new List<string>();
            _fontPathMap ??= new Dictionary<string, string>();

            _cjkFonts.Clear();
            _allFonts.Clear();
            _fontPathMap.Clear();

            var fontPaths = Font.GetPathsToOSFonts();
            foreach (var path in fontPaths)
            {
                var displayName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(displayName)) continue;
                if (_fontPathMap.ContainsKey(displayName)) continue;

                _fontPathMap[displayName] = path;
                _allFonts.Add(displayName);

                foreach (var keyword in CjkKeywords)
                {
                    if (displayName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _cjkFonts.Add(displayName);
                        break;
                    }
                }
            }

            _allFonts.Sort();
            _cjkFonts.Sort();
            _selectedIndex = 0;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("MapLibre Font Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generates a TMP_FontAsset (Dynamic SDF) from a font installed on " +
                "this OS and auto-assigns it to MapLibreMap components in the scene.",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // --- License notice ---
            EditorGUILayout.HelpBox(
                "Font licensing\n\n" +
                "The fonts listed here are the ones installed on your OS.\n" +
                "Using them in the Editor during development is fine, but if you ship " +
                "the font with a built application, make sure its license permits " +
                "redistribution.\n\n" +
                "Examples:\n" +
                "  Noto Sans JP, M PLUS, Source Han Sans, BIZ UDGothic\n" +
                "    -> SIL OFL. Free to redistribute for commercial and non-commercial use.\n" +
                "  Hiragino Kaku Gothic (macOS), Yu Gothic (Windows)\n" +
                "    -> OS-bundled fonts. Bundling/redistributing them with an app is not permitted.\n\n" +
                "We recommend OFL fonts such as Noto Sans JP for shipped builds.\n" +
                "See THIRD_PARTY_NOTICES.md for details.",
                MessageType.Warning);

            EditorGUILayout.Space(4);

            _showAllFonts = EditorGUILayout.Toggle("Show all fonts", _showAllFonts);

            var fontList = _showAllFonts ? _allFonts : _cjkFonts;

            if (fontList.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _showAllFonts
                        ? "No fonts found on this OS."
                        : "No CJK fonts found. Enable \"Show all fonts\" to list every font.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(_showAllFonts ? "All Fonts:" : "CJK Fonts:");

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(160));
            var fontNames = fontList.ToArray();
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, fontNames.Length - 1);
            _selectedIndex = GUILayout.SelectionGrid(_selectedIndex, fontNames, 1, EditorStyles.radioButton);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Selected:", fontNames[_selectedIndex], EditorStyles.boldLabel);

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Generate Font Asset & Apply", GUILayout.Height(32)))
            {
                GenerateAndApply(fontNames[_selectedIndex]);
            }
        }

        private void GenerateAndApply(string fontName)
        {
            // 1. Load Font from file path (CreateDynamicFontFromOSFont lacks font data for TMP)
            if (!_fontPathMap.TryGetValue(fontName, out var fontPath))
            {
                EditorUtility.DisplayDialog("Error", $"Font path not found for: {fontName}", "OK");
                return;
            }

            var font = new Font(fontPath);
            if (font == null)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to load font file: {fontPath}", "OK");
                return;
            }

            // 2. Create TMP_FontAsset (Dynamic)
            var fontAsset = TMP_FontAsset.CreateFontAsset(font);
            if (fontAsset == null)
            {
                EditorUtility.DisplayDialog("Error",
                    $"Failed to create TMP_FontAsset from: {fontName}\nPath: {fontPath}", "OK");
                return;
            }

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            // 3. Save asset
            if (!Directory.Exists(DefaultSavePath))
                Directory.CreateDirectory(DefaultSavePath);

            string safeName = fontName.Replace(" ", "_").Replace("/", "_");
            string assetPath = $"{DefaultSavePath}/{safeName} SDF.asset";
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // Save atlas texture and material as sub-assets (required for TMP to find them)
            if (fontAsset.atlasTexture != null)
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, assetPath);
            if (fontAsset.material != null)
                AssetDatabase.AddObjectToAsset(fontAsset.material, assetPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Reload the saved asset
            fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);

            // 4. Auto-assign to all MapLibreMap components in the scene
            int assignedCount = 0;
            var maps = Object.FindObjectsByType<MapLibreMap>(FindObjectsSortMode.None);
            foreach (var map in maps)
            {
                var so = new SerializedObject(map);
                var fontProp = so.FindProperty("_symbolFont");
                if (fontProp != null && fontProp.objectReferenceValue == null)
                {
                    fontProp.objectReferenceValue = fontAsset;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(map);
                    assignedCount++;
                }
            }

            string message = $"Created Font Asset:\n{assetPath}";
            if (assignedCount > 0)
                message += $"\n\nAuto-assigned to {assignedCount} MapLibreMap component(s) in the scene.";
            else if (maps.Length > 0)
                message += "\n\nMapLibreMap components in the scene already have a font assigned.";
            else
                message += "\n\nNo MapLibreMap component was found in the scene.\nAssign it manually under Inspector > Text > Symbol Font.";

            EditorUtility.DisplayDialog("MapLibre Font Setup", message, "OK");

            // Ping the asset in Project window
            EditorGUIUtility.PingObject(fontAsset);
        }
    }
}
