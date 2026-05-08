using UnityEngine;

namespace MapLibre.Unity.UI
{
    /// <summary>
    /// Resolves a runtime <see cref="Font"/> to drive UI Toolkit / IMGUI text in
    /// MapLibre Unity's built-in overlays and sample scenes. Exists because
    /// <see cref="Font.CreateDynamicFontFromOSFont(string,int)"/> returns a
    /// non-functional font on WebGL Player builds -- the WebAssembly runtime has
    /// no API to enumerate or read OS-installed font files, so the resulting
    /// label silently renders blank. Falling back to Unity's built-in
    /// <c>LegacyRuntime.ttf</c> resource keeps Latin text readable on WebGL
    /// without dragging a font file into the package.
    /// </summary>
    public static class SystemFontFallback
    {
        /// <summary>
        /// Returns a <see cref="Font"/> suitable for assigning to
        /// <c>VisualElement.style.unityFont</c> or <c>GUIStyle.font</c>.
        ///
        /// <para>On WebGL Player builds this is always Unity's built-in
        /// <c>LegacyRuntime.ttf</c> -- <paramref name="osFontName"/> and
        /// <paramref name="size"/> are ignored. Everywhere else (Editor and
        /// non-WebGL Players) the call forwards to
        /// <see cref="Font.CreateDynamicFontFromOSFont(string,int)"/> so the
        /// existing OS-installed font is used.</para>
        /// </summary>
        public static Font Resolve(string osFontName, int size)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            return Font.CreateDynamicFontFromOSFont(osFontName, size);
#endif
        }
    }
}
