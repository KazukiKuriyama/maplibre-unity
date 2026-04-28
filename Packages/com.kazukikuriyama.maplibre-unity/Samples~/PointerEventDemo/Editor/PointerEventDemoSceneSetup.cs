using MapLibre.Unity.Samples;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples.PointerEventDemoEditor
{
    /// <summary>
    /// Editor utility to create the PointerEventDemo scene.
    /// Menu: MapLibre > Samples > Create Pointer Event Demo Scene
    /// </summary>
    public static class PointerEventDemoSceneSetup
    {
        [MenuItem("MapLibre/Samples/Create Pointer Event Demo Scene")]
        public static void CreateScene()
        {
            // Create new scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // --- Configure Camera ---
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f);
                cam.transform.position = new Vector3(0, 10, 0);
                cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                cam.orthographic = false;
                cam.fieldOfView = 60;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 2000;
            }

            // --- PanelSettings ---
            var panelSettingsGuids = AssetDatabase.FindAssets("t:PanelSettings");
            PanelSettings panelSettings = null;
            foreach (var guid in panelSettingsGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
                if (panelSettings != null) break;
            }

            // --- MapLibreMap ---
            var mapGo = new GameObject("MapLibreMap");
            var map = mapGo.AddComponent<MapLibreMap>();

            var so = new SerializedObject(map);
            so.FindProperty("_mapCamera").objectReferenceValue = cam;
            so.FindProperty("_initialLongitude").doubleValue = 139.7670;
            so.FindProperty("_initialLatitude").doubleValue = 35.6814;
            so.FindProperty("_initialZoom").floatValue = 12f;
            so.FindProperty("_styleUrl").stringValue =
                "https://tile.openstreetmap.jp/styles/osm-bright-ja/style.json";
            if (panelSettings != null)
                so.FindProperty("_panelSettings").objectReferenceValue = panelSettings;
            so.ApplyModifiedPropertiesWithoutUndo();

            // --- PointerEventDemo component ---
            var demo = mapGo.AddComponent<PointerEventDemo>();
            var demoSo = new SerializedObject(demo);
            if (panelSettings != null)
                demoSo.FindProperty("_panelSettings").objectReferenceValue = panelSettings;
            demoSo.ApplyModifiedPropertiesWithoutUndo();

            // Resolve the scene path relative to where this script lives so
            // it works whether the sample sits in the package source
            // (Packages/...) or in the user's project after import
            // (Assets/Samples/<package>/<version>/...).
            var scenePath = "Assets/PointerEventDemoScene.unity";
            var scriptGuids = AssetDatabase.FindAssets($"t:MonoScript {nameof(PointerEventDemoSceneSetup)}");
            if (scriptGuids.Length > 0)
            {
                var scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuids[0]);
                var editorDir = System.IO.Path.GetDirectoryName(scriptPath);
                var sampleRoot = System.IO.Path.GetDirectoryName(editorDir);
                if (!string.IsNullOrEmpty(sampleRoot))
                {
                    scenePath = System.IO.Path.Combine(sampleRoot, "PointerEventDemoScene.unity")
                        .Replace('\\', '/');
                }
            }
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.OpenScene(scenePath);

            Debug.Log($"[PointerEventDemo] Scene created at {scenePath}");
        }
    }
}
