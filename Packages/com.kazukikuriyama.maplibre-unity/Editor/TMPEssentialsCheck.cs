using UnityEditor;
using UnityEngine;
using System.IO;

namespace MapLibre.Unity.Editor
{
    /// <summary>
    /// Checks whether TMP Essential Resources have been imported.
    /// Shows a dialog prompting the user to import them if missing.
    /// </summary>
    [InitializeOnLoad]
    internal static class TMPEssentialsCheck
    {
        private const string SkipSessionKey = "MapLibre_SkipTMPCheck";

        static TMPEssentialsCheck()
        {
            // Delay check to avoid running during domain reload
            EditorApplication.delayCall += CheckTMPEssentials;
        }

        private static void CheckTMPEssentials()
        {
            if (SessionState.GetBool(SkipSessionKey, false))
                return;

            if (IsTMPEssentialsImported())
                return;

            int choice = EditorUtility.DisplayDialogComplex(
                "MapLibre Unity - TextMeshPro Setup",
                "MapLibre Unity のシンボルレイヤー（テキスト・アイコン）には TMP Essential Resources が必要です。\n\n" +
                "インポートしますか？",
                "インポート",       // 0
                "今回はスキップ",    // 1
                "");                // 2

            if (choice == 0)
            {
                ImportTMPEssentials();
            }
            else
            {
                // Don't ask again this session
                SessionState.SetBool(SkipSessionKey, true);
            }
        }

        private static bool IsTMPEssentialsImported()
        {
            // TMP Essential Resources creates this folder when imported
            return Directory.Exists("Assets/TextMesh Pro/Resources");
        }

        private static void ImportTMPEssentials()
        {
            // TMP Essential Resources is bundled inside the TMP package
            string packagePath = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
            if (File.Exists(packagePath))
            {
                AssetDatabase.ImportPackage(packagePath, false);
                Debug.Log("[MapLibre] TMP Essential Resources をインポートしました。");
            }
            else
            {
                Debug.LogWarning(
                    "[MapLibre] TMP Essential Resources のパッケージが見つかりません。\n" +
                    "Window > TextMeshPro > Import TMP Essential Resources から手動でインポートしてください。");
            }
        }
    }
}
