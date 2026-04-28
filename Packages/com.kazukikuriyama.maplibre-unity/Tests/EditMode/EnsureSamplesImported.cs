using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Dev-only bootstrap: on Editor startup, ensure the package's
    /// "All Samples" group is imported and every imported scene is
    /// registered in <see cref="EditorBuildSettings"/> so the PlayMode
    /// smoke test can iterate them.
    ///
    /// This file lives under Tests/ so the release workflow's
    /// <c>rm -rf Tests</c> step strips it from the UPM tarball — end
    /// users installing the package via UPM never see this code.
    /// The <c>UNITY_INCLUDE_TESTS</c> define constraint on the Tests
    /// asmdef gates compilation to environments that have the test
    /// framework active (this repo's Editor sessions and GameCI).
    /// </summary>
    [InitializeOnLoad]
    internal static class EnsureSamplesImported
    {
        private const string PackageName = "com.kazukikuriyama.maplibre-unity";
        private const string SampleDisplayName = "All Samples";

        // SessionState marker prevents the import logic from re-running
        // after the recompile that follows AssetDatabase.Refresh, which
        // would otherwise loop indefinitely as InitializeOnLoad fires
        // again on every domain reload.
        private const string SessionMarker = "MapLibreUnity.SamplesAutoImportAttempted";

        static EnsureSamplesImported()
        {
            // Defer to the next editor tick so PackageManager has fully
            // initialized; Sample.FindByPackage can return an empty
            // enumerable when called inside the static constructor on a
            // cold editor start.
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (SessionState.GetBool(SessionMarker, false)) return;
            SessionState.SetBool(SessionMarker, true);

            try
            {
                EnsureImported();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MapLibre.Unity] Sample auto-import skipped: {e.Message}");
            }
        }

        private static void EnsureImported()
        {
            Sample target = default;
            bool found = false;
            foreach (var sample in Sample.FindByPackage(PackageName, null))
            {
                if (sample.displayName == SampleDisplayName)
                {
                    target = sample;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Debug.LogWarning(
                    $"[MapLibre.Unity] Sample '{SampleDisplayName}' not found in package '{PackageName}'. " +
                    "PlayMode smoke tests will report no scenes in EditorBuildSettings.");
                return;
            }

            if (!target.isImported)
            {
                Debug.Log($"[MapLibre.Unity] Importing sample '{SampleDisplayName}' for PlayMode smoke tests...");
                bool ok = target.Import(
                    Sample.ImportOptions.HideImportWindow |
                    Sample.ImportOptions.OverridePreviousImports);
                if (!ok)
                {
                    Debug.LogWarning("[MapLibre.Unity] Sample.Import returned false; aborting auto-setup.");
                    return;
                }
                AssetDatabase.Refresh();
            }

            PopulateBuildSettings(target.importPath);
        }

        private static void PopulateBuildSettings(string importPath)
        {
            if (string.IsNullOrEmpty(importPath) || !Directory.Exists(importPath))
            {
                Debug.LogWarning($"[MapLibre.Unity] Sample import path missing: {importPath}");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var existingPaths = new HashSet<string>(StringComparer.Ordinal);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var scene in scenes) existingPaths.Add(scene.path);

            int added = 0;
            foreach (var absoluteScenePath in Directory.EnumerateFiles(importPath, "*.unity", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(projectRoot, absoluteScenePath).Replace('\\', '/');
                if (existingPaths.Contains(relative)) continue;
                scenes.Add(new EditorBuildSettingsScene(relative, true));
                existingPaths.Add(relative);
                added++;
            }

            if (added == 0) return;

            // Sort so HomeScene boots first; the rest in stable alphabetical
            // order. PlayMode smoke test iterates in build-index order.
            scenes.Sort(CompareScenes);
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[MapLibre.Unity] Registered {added} sample scene(s) in EditorBuildSettings.");
        }

        private static int CompareScenes(EditorBuildSettingsScene a, EditorBuildSettingsScene b)
        {
            bool aHome = a.path.EndsWith("/Home/HomeScene.unity", StringComparison.Ordinal);
            bool bHome = b.path.EndsWith("/Home/HomeScene.unity", StringComparison.Ordinal);
            if (aHome && !bHome) return -1;
            if (!aHome && bHome) return 1;
            return string.Compare(a.path, b.path, StringComparison.Ordinal);
        }
    }
}
