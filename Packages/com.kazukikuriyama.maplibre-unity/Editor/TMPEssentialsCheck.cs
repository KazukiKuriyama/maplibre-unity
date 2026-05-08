using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MapLibre.Unity.Editor
{
    /// <summary>
    /// Ensures TMP Essential Resources are imported. Silently imports them
    /// on editor load when missing -- Symbol layers (text/icons) require them.
    /// </summary>
    [InitializeOnLoad]
    internal static class TMPEssentialsCheck
    {
        private const string AttemptedSessionKey = "MapLibre_TMPImportAttempted";

        static TMPEssentialsCheck()
        {
            EditorApplication.delayCall += CheckTMPEssentials;
        }

        private static void CheckTMPEssentials()
        {
            if (IsTMPEssentialsImported())
                return;

            if (SessionState.GetBool(AttemptedSessionKey, false))
                return;

            SessionState.SetBool(AttemptedSessionKey, true);
            ImportTMPEssentials();
        }

        private static bool IsTMPEssentialsImported()
        {
            // TMP Essential Resources creates this folder when imported
            return Directory.Exists("Assets/TextMesh Pro/Resources");
        }

        private static void ImportTMPEssentials()
        {
            // Unity 6+ ships TMP inside com.unity.ugui; older versions ship it as
            // com.unity.textmeshpro. Resolve the package's actual path (PackageCache
            // adds a hash suffix, so a hard-coded path won't survive upgrades).
            string[] candidatePackages = { "com.unity.ugui", "com.unity.textmeshpro" };
            foreach (var pkgName in candidatePackages)
            {
                var info = PackageInfo.FindForPackageName(pkgName);
                if (info == null) continue;

                string path = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (!File.Exists(path)) continue;

                AssetDatabase.ImportPackage(path, false);
                Debug.Log($"[MapLibre] Imported TMP Essential Resources from {pkgName}.");
                return;
            }

            Debug.LogWarning(
                "[MapLibre] TMP Essential Resources package not found.\n" +
                "Please import it manually via Window > TextMeshPro > Import TMP Essential Resources.");
        }
    }
}
