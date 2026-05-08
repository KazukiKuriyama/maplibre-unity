using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Guards the HomeScene's serialized sample list against drifting from the
    /// scenes actually shipped under Samples/. HomeSceneLoader._samples is
    /// [SerializeField], so adding an entry to the C# default list does not
    /// surface in HomeScene's UI until HomeScene.unity is re-saved with the new
    /// entry. This test catches that gap by parsing HomeScene.unity directly
    /// and asserting every *.unity under Samples/ is registered there.
    /// </summary>
    public class HomeSceneSampleListTests
    {
        private const string SamplesRoot =
            "Packages/com.kazukikuriyama.maplibre-unity/Samples";
        private const string HomeScenePath =
            SamplesRoot + "/Home/HomeScene.unity";

        [Test]
        public void HomeSceneSamplesListContainsEverySampleScene()
        {
            Assert.IsTrue(Directory.Exists(SamplesRoot),
                $"Samples root not found at '{SamplesRoot}'.");
            Assert.IsTrue(File.Exists(HomeScenePath),
                $"HomeScene.unity not found at '{HomeScenePath}'.");

            // Discover every shipping sample scene by walking the Samples/ tree.
            // HomeScene itself is excluded -- it loads the others.
            var sampleScenes = Directory
                .GetFiles(SamplesRoot, "*.unity", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name != "HomeScene")
                .OrderBy(s => s)
                .ToList();

            CollectionAssert.IsNotEmpty(sampleScenes,
                $"No sample scenes were found under '{SamplesRoot}'.");

            // Pull SceneName values out of the serialized _samples list. We
            // avoid OpenScene here so this stays an EditMode test with no
            // side effects on the user's open scene.
            var yaml = File.ReadAllText(HomeScenePath);
            var registered = new HashSet<string>(
                Regex.Matches(yaml, @"^\s*SceneName:\s*(\S+)\s*$", RegexOptions.Multiline)
                    .Select(m => m.Groups[1].Value));

            var missing = sampleScenes.Where(s => !registered.Contains(s)).ToList();
            Assert.IsEmpty(missing,
                $"HomeScene._samples is missing {missing.Count} sample scene(s):\n  - " +
                string.Join("\n  - ", missing) +
                "\n\n_samples is [SerializeField], so updating HomeSceneLoader.cs alone " +
                "does not surface new entries in the runtime UI. Open HomeScene.unity, " +
                "add the missing entries via the Inspector, and save.");
        }

        [Test]
        public void HomeSceneSamplesListDoesNotReferenceMissingScenes()
        {
            var yaml = File.ReadAllText(HomeScenePath);
            var registered = Regex
                .Matches(yaml, @"^\s*SceneName:\s*(\S+)\s*$", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value)
                .ToList();

            var existingNames = new HashSet<string>(Directory
                .GetFiles(SamplesRoot, "*.unity", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension));

            var orphans = registered.Where(s => !existingNames.Contains(s)).ToList();
            Assert.IsEmpty(orphans,
                $"HomeScene._samples references {orphans.Count} scene(s) that no longer exist:\n  - " +
                string.Join("\n  - ", orphans) +
                "\n\nRemove the dead entries from HomeScene.unity.");
        }
    }
}
