using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MapLibre.Unity.Editor
{
    [CustomEditor(typeof(MapLibreMap))]
    public class MapLibreMapEditor : UnityEditor.Editor
    {
        private SerializedProperty _styleUrl;
        private SerializedProperty _styleFile;
        private SerializedProperty _styleJson;
        private SerializedProperty _initialLongitude;
        private SerializedProperty _initialLatitude;
        private SerializedProperty _initialZoom;
        private SerializedProperty _initialBearing;
        private SerializedProperty _initialPitch;
        private SerializedProperty _tileCacheSize;
        private SerializedProperty _tilePoolInitialSize;
        private SerializedProperty _mapCamera;
        private SerializedProperty _rasterTileShader;
        private SerializedProperty _backgroundShader;
        private SerializedProperty _fillShader;
        private SerializedProperty _lineShader;
        private SerializedProperty _panelSettings;
        private SerializedProperty _iconShader;
        private SerializedProperty _symbolFont;

        private bool _showStyleJson;

        private void OnEnable()
        {
            _styleUrl = serializedObject.FindProperty("_styleUrl");
            _styleFile = serializedObject.FindProperty("_styleFile");
            _styleJson = serializedObject.FindProperty("_styleJson");
            _initialLongitude = serializedObject.FindProperty("_initialLongitude");
            _initialLatitude = serializedObject.FindProperty("_initialLatitude");
            _initialZoom = serializedObject.FindProperty("_initialZoom");
            _initialBearing = serializedObject.FindProperty("_initialBearing");
            _initialPitch = serializedObject.FindProperty("_initialPitch");
            _tileCacheSize = serializedObject.FindProperty("_tileCacheSize");
            _tilePoolInitialSize = serializedObject.FindProperty("_tilePoolInitialSize");
            _mapCamera = serializedObject.FindProperty("_mapCamera");
            _rasterTileShader = serializedObject.FindProperty("_rasterTileShader");
            _backgroundShader = serializedObject.FindProperty("_backgroundShader");
            _fillShader = serializedObject.FindProperty("_fillShader");
            _lineShader = serializedObject.FindProperty("_lineShader");
            _panelSettings = serializedObject.FindProperty("_panelSettings");
            _iconShader = serializedObject.FindProperty("_iconShader");
            _symbolFont = serializedObject.FindProperty("_symbolFont");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // --- Style ---
            EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(_styleUrl, new GUIContent("Style URL", "URLからスタイルJSONを取得"));

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_styleFile, new GUIContent("Style File", "JSONファイルをドラッグ&ドロップ"));
            if (EditorGUI.EndChangeCheck())
            {
                // ファイルが設定されたらStyleJsonを自動で更新
                serializedObject.ApplyModifiedProperties();
                var textAsset = _styleFile.objectReferenceValue as TextAsset;
                if (textAsset != null)
                {
                    _styleJson.stringValue = textAsset.text;
                    Debug.Log($"[MapLibreMap] スタイルファイルを読み込みました: {textAsset.name} ({textAsset.text.Length} bytes)");
                }
            }

            bool hasFile = _styleFile.objectReferenceValue != null;

            if (hasFile)
            {
                // ファイル設定時は読み取り専用で内容を表示
                _showStyleJson = EditorGUILayout.Foldout(_showStyleJson, "Style Json (読み取り専用)");
                if (_showStyleJson)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.PropertyField(_styleJson, GUIContent.none);
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUILayout.HelpBox(
                    $"スタイルファイル: {(_styleFile.objectReferenceValue as TextAsset)?.name}.json\n" +
                    "直接編集するには Style File を None に戻してください",
                    MessageType.Info);
            }
            else
            {
                _showStyleJson = EditorGUILayout.Foldout(_showStyleJson, "Style Json (直接編集)");
                if (_showStyleJson)
                {
                    EditorGUILayout.PropertyField(_styleJson, GUIContent.none);
                }

                if (!string.IsNullOrEmpty(_styleUrl.stringValue))
                {
                    EditorGUILayout.HelpBox("スタイルURL からロードします", MessageType.Info);
                }
                else if (!string.IsNullOrEmpty(_styleJson.stringValue))
                {
                    EditorGUILayout.HelpBox("Style Json を直接使用します", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Style File にJSONファイルをドラッグ&ドロップするか、Style URL を設定してください",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.Space();

            // --- Initial View ---
            // Detect whether the style JSON already provides center/zoom.
            // When it does, those Inspector fields are overridden and shown as disabled.
            bool styleHasCenter = false;
            bool styleHasZoom = false;
            string styleJson = GetEffectiveStyleJson();
            if (!string.IsNullOrEmpty(styleJson))
            {
                try
                {
                    var obj = JObject.Parse(styleJson);
                    styleHasCenter = obj["center"] != null;
                    styleHasZoom = obj["zoom"] != null;
                }
                catch
                {
                    // Invalid JSON — ignore
                }
            }

            EditorGUILayout.LabelField("Initial View", EditorStyles.boldLabel);

            if (styleHasCenter)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_initialLongitude);
                EditorGUILayout.PropertyField(_initialLatitude);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.HelpBox("center はスタイル JSON で設定済み", MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(_initialLongitude);
                EditorGUILayout.PropertyField(_initialLatitude);
            }

            if (styleHasZoom)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_initialZoom);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.HelpBox("zoom はスタイル JSON で設定済み", MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(_initialZoom);
            }

            EditorGUILayout.PropertyField(_initialBearing);
            EditorGUILayout.PropertyField(_initialPitch);

            EditorGUILayout.Space();

            // --- Performance ---
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_tileCacheSize);
            EditorGUILayout.PropertyField(_tilePoolInitialSize);

            EditorGUILayout.Space();

            // --- References ---
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_mapCamera);
            EditorGUILayout.PropertyField(_rasterTileShader);
            EditorGUILayout.PropertyField(_backgroundShader);
            EditorGUILayout.PropertyField(_fillShader);
            EditorGUILayout.PropertyField(_lineShader);
            EditorGUILayout.PropertyField(_iconShader);
            EditorGUILayout.PropertyField(_panelSettings);

            EditorGUILayout.Space();

            // --- Text ---
            EditorGUILayout.LabelField("Text", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_symbolFont, new GUIContent("Symbol Font", "TMP_FontAsset for map labels. Use MapLibre > Font Setup to generate."));

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Get the style JSON text from the style file or the inline JSON field.
        /// Style URL is not resolved here (requires network) so it returns null.
        /// </summary>
        private string GetEffectiveStyleJson()
        {
            var textAsset = _styleFile.objectReferenceValue as TextAsset;
            if (textAsset != null)
                return textAsset.text;
            if (!string.IsNullOrEmpty(_styleJson.stringValue))
                return _styleJson.stringValue;
            return null;
        }
    }
}
