using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Dev-only bootstrap: on Editor startup, register every sample scene
    /// shipped under <c>Packages/com.kazukikuriyama.maplibre-unity/Samples/</c>
    /// in <see cref="EditorBuildSettings"/> so HomeSceneLoader and the
    /// PlayMode smoke test can resolve them by name.
    ///
    /// This file lives under Tests/ so the release workflow's
    /// <c>rm -rf Tests</c> step strips it from the UPM tarball -- end users
    /// installing the package via UPM never see this code. They are
    /// expected to add scenes to Build Settings themselves the first
    /// time they want to launch HomeScene.
    /// </summary>
    [InitializeOnLoad]
    internal static class EnsureSamplesImported
    {
        private const string SamplesRelativePath =
            "Packages/com.kazukikuriyama.maplibre-unity/Samples";

        // SessionState marker prevents the registration logic from re-running
        // after the recompile that follows AssetDatabase changes, which
        // would otherwise loop indefinitely as InitializeOnLoad fires
        // again on every domain reload.
        private const string SessionMarker =
            "MapLibreUnity.SamplesAutoRegistrationAttempted";

        static EnsureSamplesImported()
        {
            // Defer to the next editor tick; some Unity APIs (e.g. AssetDatabase)
            // are not safe to call from the static constructor.
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (SessionState.GetBool(SessionMarker, false)) return;
            SessionState.SetBool(SessionMarker, true);

            try
            {
                RegisterSampleScenes();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MapLibre.Unity] Sample scene auto-registration skipped: {e.Message}");
            }
        }

        private static void RegisterSampleScenes()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absoluteSamplesRoot = Path.Combine(projectRoot, SamplesRelativePath);
            if (!Directory.Exists(absoluteSamplesRoot))
            {
                Debug.LogWarning(
                    $"[MapLibre.Unity] Sample folder not found at '{SamplesRelativePath}'. " +
                    "PlayMode smoke tests will report no scenes in EditorBuildSettings.");
                return;
            }

            var existingPaths = new HashSet<string>(StringComparer.Ordinal);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var scene in scenes) existingPaths.Add(scene.path);

            int added = 0;
            foreach (var absoluteScenePath in Directory.EnumerateFiles(absoluteSamplesRoot, "*.unity", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(projectRoot, absoluteScenePath).Replace('\\', '/');
                if (existingPaths.Contains(relative)) continue;
                scenes.Add(new EditorBuildSettingsScene(relative, true));
                existingPaths.Add(relative);
                added++;
            }

            // Drop entries left over from the legacy opt-in import flow that
            // copied samples to Assets/Samples/MapLibre Unity/<version>/...
            // After the move to bundled samples those paths no longer exist,
            // and would surface as "Missing" entries in Build Settings.
            int removed = scenes.RemoveAll(s =>
                s.path.StartsWith("Assets/Samples/MapLibre Unity/", StringComparison.Ordinal));

            if (added == 0 && removed == 0) return;

            // Sort so HomeScene boots first; the rest in stable alphabetical
            // order. PlayMode smoke test iterates in build-index order.
            scenes.Sort(CompareScenes);
            EditorBuildSettings.scenes = scenes.ToArray();

            if (removed > 0)
                Debug.Log($"[MapLibre.Unity] Removed {removed} legacy Assets/Samples/MapLibre Unity/ entry/entries from EditorBuildSettings.");
            if (added > 0)
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
