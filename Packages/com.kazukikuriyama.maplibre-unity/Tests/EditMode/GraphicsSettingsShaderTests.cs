using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Guards the dev project's m_AlwaysIncludedShaders list against drifting
    /// from the *.shader files actually shipped under the Shaders/ folder.
    /// Shaders resolved at runtime via <c>Shader.Find()</c> get stripped from
    /// player builds unless they are referenced by a Material in an included
    /// scene OR registered in Graphics Settings &gt; Always Included Shaders.
    /// MapLibre Unity uses the latter, so a missing entry only surfaces in
    /// release builds -- this test catches it at CI time.
    /// </summary>
    public class GraphicsSettingsShaderTests
    {
        private const string ShadersRoot =
            "Packages/com.kazukikuriyama.maplibre-unity/Shaders";
        private const string GraphicsSettingsPath =
            "ProjectSettings/GraphicsSettings.asset";

        [Test]
        public void AlwaysIncludedShadersListsEveryMapLibreShader()
        {
            Assert.IsTrue(Directory.Exists(ShadersRoot),
                $"Shaders root not found at '{ShadersRoot}'.");
            Assert.IsTrue(File.Exists(GraphicsSettingsPath),
                $"GraphicsSettings.asset not found at '{GraphicsSettingsPath}'.");

            var shaderGuids = ReadShaderGuids();
            CollectionAssert.IsNotEmpty(shaderGuids,
                $"No *.shader files were found under '{ShadersRoot}'.");

            var includedGuids = ReadAlwaysIncludedShaderGuids();

            var missing = shaderGuids
                .Where(kv => !includedGuids.Contains(kv.Key))
                .Select(kv => $"{kv.Value} ({kv.Key})")
                .OrderBy(s => s)
                .ToList();

            Assert.IsEmpty(missing,
                $"{missing.Count} MapLibre shader(s) are not registered in " +
                "m_AlwaysIncludedShaders:\n  - " +
                string.Join("\n  - ", missing) +
                "\n\nOpen Edit > Project Settings > Graphics, scroll to " +
                "'Always Included Shaders', and add the missing shaders. " +
                "Without this, Shader.Find() returns null in player builds.");
        }

        // Read every *.shader.meta under Shaders/ and return guid -> name.
        private static Dictionary<string, string> ReadShaderGuids()
        {
            var result = new Dictionary<string, string>();
            foreach (var meta in Directory.GetFiles(ShadersRoot, "*.shader.meta"))
            {
                var content = File.ReadAllText(meta);
                var match = Regex.Match(content,
                    @"^guid:\s*([0-9a-fA-F]+)\s*$",
                    RegexOptions.Multiline);
                Assert.IsTrue(match.Success,
                    $"Could not parse 'guid:' from '{meta}'.");

                // Strip both .meta and .shader to get the bare shader name.
                var name = Path.GetFileNameWithoutExtension(
                    Path.GetFileNameWithoutExtension(meta));
                result[match.Groups[1].Value] = name;
            }
            return result;
        }

        // Pull GUIDs out of the m_AlwaysIncludedShaders YAML block. Filtering
        // by `type: 3` (project-asset reference) excludes Unity built-in
        // shaders that share the all-zero guid.
        private static HashSet<string> ReadAlwaysIncludedShaderGuids()
        {
            var yaml = File.ReadAllText(GraphicsSettingsPath);
            var block = Regex.Match(yaml,
                @"m_AlwaysIncludedShaders:\s*\n((?:[ \t]+-[^\n]*\n)+)");
            Assert.IsTrue(block.Success,
                "Could not locate the m_AlwaysIncludedShaders block in " +
                GraphicsSettingsPath + ".");

            return new HashSet<string>(
                Regex.Matches(block.Groups[1].Value,
                        @"guid:\s*([0-9a-fA-F]+),\s*type:\s*3")
                    .Select(m => m.Groups[1].Value));
        }
    }
}
