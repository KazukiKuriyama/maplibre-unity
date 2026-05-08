using MapLibre.Unity.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Automatically injects a small "← Home" overlay into every loaded scene
    /// except <see cref="HomeSceneLoader.HomeSceneName"/>. Pressing Escape also
    /// returns to the home scene.
    /// </summary>
    public class BackToHomeOverlay : MonoBehaviour
    {
        private const string OverlayName = "__BackToHomeOverlay";

        private UIDocument _uiDoc;
        private Font _font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BootstrapOnce()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (scene.name == HomeSceneLoader.HomeSceneName) return;
            if (!IsHomeSceneInBuildSettings()) return;

            var existing = GameObject.Find(OverlayName);
            if (existing != null) return;

            var go = new GameObject(OverlayName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<BackToHomeOverlay>();
        }

        private static bool IsHomeSceneInBuildSettings()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (name == HomeSceneLoader.HomeSceneName) return true;
            }
            return false;
        }

        private void Start()
        {
            _font = SystemFontFallback.Resolve("Arial", 16);
            _uiDoc = gameObject.AddComponent<UIDocument>();

            var fallback = Resources.FindObjectsOfTypeAll<PanelSettings>();
            if (fallback.Length > 0) _uiDoc.panelSettings = fallback[0];
            _uiDoc.sortingOrder = 500;

            BuildUI();
        }

        private void BuildUI()
        {
            var root = _uiDoc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            var btn = new Button(ReturnToHome)
            {
                text = "← Home",
                style =
                {
                    position = Position.Absolute,
                    // Stack just above MapControlsOverlay's scale bar. The bar
                    // container now sits at bottom:2 (aligned with the
                    // attribution row); its top edge reaches roughly y=27
                    // (6px bar + 1px gap + ~18px bold fontSize-10 label).
                    // 35 leaves an ~8px gap above the scale label.
                    bottom = 35,
                    left = 8,
                    height = 20,
                    width = 52,
                    paddingTop = 0,
                    paddingBottom = 0,
                    paddingLeft = 0,
                    paddingRight = 0,
                    marginTop = 0,
                    marginBottom = 0,
                    marginLeft = 0,
                    marginRight = 0,
                    fontSize = 10,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = Color.white,
                    backgroundColor = new Color(0.1f, 0.12f, 0.18f, 0.85f),
                    borderTopLeftRadius = 4,
                    borderTopRightRadius = 4,
                    borderBottomLeftRadius = 4,
                    borderBottomRightRadius = 4,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(1, 1, 1, 0.18f),
                    borderBottomColor = new Color(1, 1, 1, 0.18f),
                    borderLeftColor = new Color(1, 1, 1, 0.18f),
                    borderRightColor = new Color(1, 1, 1, 0.18f),
                    unityFontDefinition = StyleKeyword.None,
                    unityFont = new StyleFont(_font),
                }
            };
            root.Add(btn);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                ReturnToHome();
        }

        private static void ReturnToHome()
        {
            SceneManager.LoadScene(HomeSceneLoader.HomeSceneName);
        }
    }
}
