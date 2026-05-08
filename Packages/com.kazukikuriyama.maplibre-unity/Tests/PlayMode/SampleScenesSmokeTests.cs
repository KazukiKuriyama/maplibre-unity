using System.Collections;
using System.Collections.Generic;
using System.IO;
using MapLibre.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MapLibre.Unity.Tests.PlayMode
{
    /// <summary>
    /// Loads every sample scene listed in EditorBuildSettings and asserts that
    /// nothing throws an exception or logs an Assert during initialisation.
    ///
    /// The bar here is deliberately low: we don't verify visuals or that any
    /// network resource resolved, only that the scene boots without a code-side
    /// regression (NullReferenceException, missing component reference, ASMDEF
    /// graph mismatch, etc.). Network-driven errors are tolerated because most
    /// CI environments cannot reach the demo's tile endpoints.
    /// </summary>
    public class SampleScenesSmokeTests
    {
        // Hard cap on how long one scene can settle. Demos with long
        // SerializeField-driven timed sequences (RuntimeGeoJsonDemo) intentionally
        // schedule operations several seconds out -- those are not covered by
        // the smoke test, but the test still needs an upper bound so a stuck
        // scene doesn't run forever.
        private const float MaxSettleSeconds = 1.5f;

        // Minimum wall-clock wait before the early-exit kicks in. Demos that
        // do <c>while (_map.State == null) yield return null;</c> followed by
        // a couple of frame yields and AddSource/AddLayer settle within
        // ~150 ms on a mid-range Mac. 0.3 s gives enough headroom while still
        // letting fast scenes early-exit aggressively.
        private const float MinSettleSeconds = 0.3f;

        // Pulled at first access -- Build Settings can change between sessions.
        private static IEnumerable<string> SceneNames
        {
            get
            {
                int count = SceneManager.sceneCountInBuildSettings;
                if (count == 0)
                {
                    // No scenes registered at all is itself a failure surface,
                    // but report a single named case so the runner shows the
                    // problem rather than silently skipping.
                    yield return "<no-scenes-in-build-settings>";
                    yield break;
                }
                for (int i = 0; i < count; i++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(i);
                    yield return Path.GetFileNameWithoutExtension(path);
                }
            }
        }

        [UnityTest]
        public IEnumerator SceneLoadsWithoutExceptions(
            [ValueSource(nameof(SceneNames))] string sceneName)
        {
            if (sceneName == "<no-scenes-in-build-settings>")
            {
                Assert.Fail("EditorBuildSettings contains no scenes. Add sample scenes via " +
                            "File > Build Profiles before running this test.");
                yield break;
            }

            var caught = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception || type == LogType.Assert)
                {
                    caught.Add($"[{type}] {condition}\n{stackTrace}");
                }
            }
            Application.logMessageReceived += OnLog;

            var loadOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            Assert.IsNotNull(loadOp,
                $"Scene '{sceneName}' could not be queued for loading. " +
                "Confirm it is enabled in Build Settings.");
            yield return loadOp;

            // Let Awake/Start coroutines settle. Wall-clock time, not
            // Time.deltaTime -- the Editor's PlayMode test runner reports
            // deltaTime ≈ 0 once the editor window loses focus, which
            // would stall the polling loop indefinitely. realtimeSinceStartup
            // keeps advancing regardless of focus state.
            //
            // Two-phase wait: first MinSettleSeconds always elapses so any
            // synchronous Start exception is caught; after that the loop
            // exits as soon as every MapLibreMap on the scene reports
            // IsInitialized -- most scenes hit that within ~0.5 s, which is
            // much faster than blanket-waiting MaxSettleSeconds for all of
            // them.
            float startTime = Time.realtimeSinceStartup;
            while (true)
            {
                float elapsed = Time.realtimeSinceStartup - startTime;
                if (elapsed >= MaxSettleSeconds) break;
                yield return null;
                if (elapsed < MinSettleSeconds) continue;

                if (AllMapsInitialized()) break;
            }

            Application.logMessageReceived -= OnLog;

            if (caught.Count > 0)
            {
                Assert.Fail(
                    $"Scene '{sceneName}' produced {caught.Count} unexpected log(s) during boot:\n\n" +
                    string.Join("\n----\n", caught));
            }

            // Forced cleanup between tests. Without this Unity Editor hangs
            // somewhere around the 22nd consecutive LoadSceneAsync -- appears
            // to be the Editor's PlayMode test runner accumulating asset /
            // GameObject state that isn't GC'd in time. UnloadUnusedAssets
            // returns an AsyncOperation we have to await, then a manual
            // GC.Collect to force the managed side too.
            var unload = Resources.UnloadUnusedAssets();
            yield return unload;
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
            // Give Unity a couple of extra wall-clock seconds to drain
            // pending UnityWebRequest connections and run its own internal
            // housekeeping. Without this PMTiles / glyph-fetching demos
            // leave HTTP connections that pile up and eventually deadlock
            // the editor on the next LoadSceneAsync.
            float drainStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - drainStart < CleanupDrainSeconds)
                yield return null;
        }

        // Wall-clock seconds to wait after GC so Unity can drain pending
        // UnityWebRequest connections / housekeeping. 0.3 s is enough on a
        // mid-range Mac; raise toward 1 s if the editor freezes again on
        // long runs (HTTP-heavy demos like PMTiles / Glyph URL).
        private const float CleanupDrainSeconds = 0.3f;

        /// <summary>
        /// True when every <see cref="MapLibreMap"/> currently in the loaded
        /// scene reports <see cref="MapLibreMap.IsInitialized"/>. Returns true
        /// for scenes that have no map at all (lets non-map scenes early-exit
        /// straight after the minimum settle time).
        /// </summary>
        private static bool AllMapsInitialized()
        {
            var maps = Object.FindObjectsByType<MapLibreMap>(FindObjectsSortMode.None);
            if (maps == null || maps.Length == 0) return true;
            for (int i = 0; i < maps.Length; i++)
                if (!maps[i].IsInitialized) return false;
            return true;
        }
    }
}
