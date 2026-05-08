using Newtonsoft.Json;
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
        private SerializedProperty _panelSettings;
        private SerializedProperty _symbolFont;

        // Cache for JSON validation: re-parsing on every OnInspectorGUI repaint
        // is wasted work, so we only re-run when the text changes.
        private string _lastValidatedJson;
        private string _lastValidationError;

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
            _panelSettings = serializedObject.FindProperty("_panelSettings");
            _symbolFont = serializedObject.FindProperty("_symbolFont");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // --- Style ---
            EditorGUILayout.LabelField("Style (one of Style URL / File / Json is required)", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(_styleUrl, new GUIContent("Style URL (Optional)", "Fetch the style JSON from this URL. Not needed if Style File or Style Json is set."));

            EditorGUILayout.PropertyField(_styleFile, new GUIContent("Style File (Optional)", "Drag & drop a JSON file here. Not needed if Style URL or Style Json is set."));

            bool hasFile = _styleFile.objectReferenceValue != null;

            if (hasFile)
            {
                // Mirror the file's text into _styleJson on every repaint so the
                // read-only TextArea reflects the current file content, even if it
                // was edited externally after first being attached. The != guard
                // avoids marking the scene dirty when nothing changed.
                var textAsset = _styleFile.objectReferenceValue as TextAsset;
                string fileText = textAsset != null ? textAsset.text : string.Empty;
                if (_styleJson.stringValue != fileText)
                    _styleJson.stringValue = fileText;

                EditorGUILayout.LabelField("Style Json (read-only)");
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_styleJson, GUIContent.none);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.HelpBox(
                    $"Style file: {textAsset?.name}.json\n" +
                    "Reset Style File to None to edit the JSON inline.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Style Json (Optional, inline edit)");
                EditorGUILayout.PropertyField(_styleJson, GUIContent.none);

                if (!string.IsNullOrEmpty(_styleUrl.stringValue))
                {
                    EditorGUILayout.HelpBox("Loading from Style URL.", MessageType.Info);
                }
                else if (!string.IsNullOrEmpty(_styleJson.stringValue))
                {
                    EditorGUILayout.HelpBox("Using the inline Style Json.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Drag & drop a JSON file onto Style File, or set a Style URL.",
                        MessageType.Warning);
                }
            }

            // Validate the JSON content (covers both inline edits and file-backed
            // assignments -- a corrupt file is just as broken as a corrupt inline string).
            if (!string.IsNullOrEmpty(_styleJson.stringValue))
            {
                string error = GetCachedValidationError(_styleJson.stringValue);
                if (error != null)
                    EditorGUILayout.HelpBox($"Invalid Style JSON: {error}", MessageType.Error);
            }

            EditorGUILayout.Space();

            // --- Initial View ---
            // Inspector fields override the style JSON when set (matches MapLibre GL JS
            // constructor options). A field is "unset" when its value is NaN -- the
            // effective value then comes from the style JSON (or 0 if neither has it).
            double? styleZoom = null;
            double? styleLng = null;
            double? styleLat = null;
            double? stylePitch = null;
            double? styleBearing = null;
            string styleJson = GetEffectiveStyleJson();
            if (!string.IsNullOrEmpty(styleJson))
            {
                try
                {
                    var obj = JObject.Parse(styleJson);
                    styleZoom = (double?)obj["zoom"];
                    stylePitch = (double?)obj["pitch"];
                    styleBearing = (double?)obj["bearing"];
                    if (obj["center"] is JArray centerArr && centerArr.Count >= 2)
                    {
                        styleLng = (double?)centerArr[0];
                        styleLat = (double?)centerArr[1];
                    }
                }
                catch
                {
                    // Invalid JSON -- ignore
                }
            }

            EditorGUILayout.LabelField("Initial View (overrides style JSON when set)", EditorStyles.boldLabel);

            DrawOverrideDouble(_initialLongitude, "Initial Longitude", "longitude", styleLng);
            DrawOverrideDouble(_initialLatitude, "Initial Latitude", "latitude", styleLat);
            DrawOverrideFloat(_initialZoom, "Initial Zoom", "zoom", styleZoom);
            DrawOverrideFloat(_initialBearing, "Initial Bearing", "bearing", styleBearing);
            DrawOverrideFloat(_initialPitch, "Initial Pitch", "pitch", stylePitch);

            EditorGUILayout.Space();

            // --- Performance ---
            EditorGUILayout.LabelField("Performance (all Optional)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_tileCacheSize, new GUIContent("Tile Cache Size (Optional)"));
            EditorGUILayout.PropertyField(_tilePoolInitialSize, new GUIContent("Tile Pool Initial Size (Optional)"));

            EditorGUILayout.Space();

            // --- References ---
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_mapCamera, new GUIContent("Map Camera (Optional)", "Falls back to Camera.main when unset."));
            EditorGUILayout.PropertyField(_panelSettings, new GUIContent("Panel Settings", "PanelSettings asset for UI Toolkit (required)."));

            EditorGUILayout.Space();

            // --- Text ---
            EditorGUILayout.LabelField("Text (all Optional)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_symbolFont, new GUIContent("Symbol Font (Optional)", "TMP_FontAsset for map labels. Falls back to the TMP default when unset. Use MapLibre > Font Setup to generate."));

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawOverrideFloat(SerializedProperty property, string label, string keyName, double? styleValue)
        {
            bool isSet = !float.IsNaN(property.floatValue);
            EditorGUILayout.BeginHorizontal();
            if (isSet)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                if (GUILayout.Button("Use style", GUILayout.Width(80)))
                    property.floatValue = float.NaN;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(label, FormatStyleHint(keyName, styleValue));
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Override", GUILayout.Width(80)))
                    property.floatValue = styleValue.HasValue ? (float)styleValue.Value : 0f;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawOverrideDouble(SerializedProperty property, string label, string keyName, double? styleValue)
        {
            bool isSet = !double.IsNaN(property.doubleValue);
            EditorGUILayout.BeginHorizontal();
            if (isSet)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                if (GUILayout.Button("Use style", GUILayout.Width(80)))
                    property.doubleValue = double.NaN;
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(label, FormatStyleHint(keyName, styleValue));
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Override", GUILayout.Width(80)))
                    property.doubleValue = styleValue ?? 0.0;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatStyleHint(string keyName, double? styleValue) =>
            styleValue.HasValue
                ? $"(using style.{keyName} = {styleValue.Value})"
                : $"(using default 0 -- style has no {keyName})";

        private string GetCachedValidationError(string json)
        {
            if (json != _lastValidatedJson)
            {
                _lastValidatedJson = json;
                _lastValidationError = ValidateStyleJson(json);
            }
            return _lastValidationError;
        }

        private static string ValidateStyleJson(string json)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch (JsonReaderException e)
            {
                return $"parse error at line {e.LineNumber}, col {e.LinePosition}: {e.Message}";
            }
            catch (System.Exception e)
            {
                return e.Message;
            }

            if (obj["version"] == null)
                return "missing required field \"version\".";
            if (obj["version"].Type != JTokenType.Integer)
                return "\"version\" must be an integer (current spec uses 8).";
            if (obj["sources"] == null && obj["layers"] == null)
                return "style must define at least one of \"sources\" or \"layers\".";
            if (obj["layers"] != null && obj["layers"].Type != JTokenType.Array)
                return "\"layers\" must be an array.";
            if (obj["sources"] != null && obj["sources"].Type != JTokenType.Object)
                return "\"sources\" must be an object.";

            return null;
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
