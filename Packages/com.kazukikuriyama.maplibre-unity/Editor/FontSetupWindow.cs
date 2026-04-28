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
                "OS にインストール済みのフォントから TMP_FontAsset (Dynamic SDF) を生成し、" +
                "シーン内の MapLibreMap に自動設定します。",
                MessageType.Info);

            EditorGUILayout.Space(4);

            // --- License notice ---
            EditorGUILayout.HelpBox(
                "フォントのライセンスについて\n\n" +
                "ここに表示されるフォントは、お使いの OS にインストールされているものです。\n" +
                "開発中のエディタ上での使用は問題ありませんが、アプリとして配布する場合は " +
                "フォントのライセンスが再配布を許可しているか確認してください。\n\n" +
                "例:\n" +
                "  Noto Sans JP, M PLUS, Source Han Sans, BIZ UDGothic\n" +
                "    → SIL OFL ライセンス。商用・非商用問わず再配布可能\n" +
                "  ヒラギノ角ゴ (macOS), 游ゴシック (Windows)\n" +
                "    → OS 付属フォント。アプリへの同梱・再配布は不可\n\n" +
                "配布アプリには Noto Sans JP 等の OFL フォントの使用を推奨します。\n" +
                "詳細は THIRD_PARTY_NOTICES.md を参照してください。",
                MessageType.Warning);

            EditorGUILayout.Space(4);

            _showAllFonts = EditorGUILayout.Toggle("Show all fonts", _showAllFonts);

            var fontList = _showAllFonts ? _allFonts : _cjkFonts;

            if (fontList.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _showAllFonts
                        ? "OS にフォントが見つかりません。"
                        : "CJK フォントが見つかりません。\"Show all fonts\" を有効にしてください。",
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

            string message = $"Font Asset を作成しました:\n{assetPath}";
            if (assignedCount > 0)
                message += $"\n\nシーン内の MapLibreMap {assignedCount} 個に自動設定しました。";
            else if (maps.Length > 0)
                message += "\n\nシーン内の MapLibreMap には既にフォントが設定済みです。";
            else
                message += "\n\nシーンに MapLibreMap が見つかりませんでした。\nInspector の Text > Symbol Font に手動で設定してください。";

            EditorUtility.DisplayDialog("MapLibre Font Setup", message, "OK");

            // Ping the asset in Project window
            EditorGUIUtility.PingObject(fontAsset);
        }
    }
}
