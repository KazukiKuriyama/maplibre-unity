using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLibre.Unity.Editor
{
    /// <summary>
    /// Auto-registers every shader the package ships with the project's
    /// <c>Project Settings → Graphics → Always Included Shaders</c> list so
    /// they survive build stripping without the user having to edit Project
    /// Settings by hand. Runs once per Editor load via
    /// <see cref="InitializeOnLoadAttribute"/>; re-runs are cheap because
    /// already-registered shaders are skipped.
    /// </summary>
    [InitializeOnLoad]
    internal static class AlwaysIncludedShadersRegistration
    {
        // Path the package's shaders live under, relative to project root.
        // AssetDatabase.FindAssets accepts package paths verbatim.
        private const string PackageShadersPath = "Packages/com.kazukikuriyama.maplibre-unity/Shaders";

        static AlwaysIncludedShadersRegistration()
        {
            // Defer to delayCall so the AssetDatabase is fully ready -- on the
            // first Editor open after a fresh package install, FindAssets
            // returns 0 hits if called from the static ctor directly.
            EditorApplication.delayCall += Register;
        }

        private static void Register()
        {
            var packagedShaders = LoadPackagedShaders();
            if (packagedShaders.Count == 0) return;

            var graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            var so = new SerializedObject(graphicsSettings);
            var arrayProp = so.FindProperty("m_AlwaysIncludedShaders");
            if (arrayProp == null) return;

            var existing = new HashSet<Shader>();
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue is Shader s)
                    existing.Add(s);
            }

            int added = 0;
            foreach (var shader in packagedShaders)
            {
                if (shader == null || existing.Contains(shader)) continue;
                int idx = arrayProp.arraySize;
                arrayProp.arraySize = idx + 1;
                arrayProp.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
                existing.Add(shader);
                added++;
            }

            if (added == 0) return;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[MapLibre Unity] Registered {added} shader(s) to " +
                      "Project Settings → Graphics → Always Included Shaders.");
        }

        private static List<Shader> LoadPackagedShaders()
        {
            var result = new List<Shader>();
            var guids = AssetDatabase.FindAssets("t:Shader", new[] { PackageShadersPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader != null) result.Add(shader);
            }
            return result;
        }
    }
}
