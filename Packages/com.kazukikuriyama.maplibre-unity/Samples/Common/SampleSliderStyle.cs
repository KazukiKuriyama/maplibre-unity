using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Slim, modern look for the default UI Toolkit Slider.
    ///
    /// The built-in <c>.unity-base-slider</c> control ships with a chunky
    /// 3D-style track and a square dragger that look out of place on the
    /// dark sample HUD overlays. This helper rewrites the relevant child
    /// elements to a thin track plus a circular thumb.
    ///
    /// Apply once after the slider has been added to the panel. Inline
    /// styling is used (rather than a USS sheet) so the helper drops into
    /// any sample without requiring a stylesheet asset.
    /// </summary>
    public static class SampleSliderStyle
    {
        // Tweak in one place if a sample wants a different accent.
        private static readonly Color TrackColor = new(1f, 1f, 1f, 0.18f);
        private static readonly Color ThumbColor = new(0.55f, 0.78f, 1f, 1f);
        private const float TrackHeight = 2f;
        private const float ThumbDiameter = 12f;

        public static void Apply(Slider slider, Font font = null)
        {
            // Slim overall row height; the slider used to be ~24px tall.
            slider.style.height = 16;
            slider.style.marginTop = 2;
            slider.style.marginBottom = 6;
            if (font != null)
            {
                slider.style.unityFontDefinition = StyleKeyword.None;
                slider.style.unityFont = new StyleFont(font);
            }

            // Children of UI Toolkit's Slider are built lazily, so defer
            // restyling until the control is attached to a panel and its
            // visual tree is realised.
            slider.RegisterCallback<AttachToPanelEvent>(_ => Restyle(slider));
            // Also try immediately in case the slider is already in a panel
            // (rare, but cheap to attempt).
            Restyle(slider);
        }

        private static void Restyle(Slider slider)
        {
            var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.height = TrackHeight;
                tracker.style.backgroundColor = TrackColor;
                tracker.style.borderTopLeftRadius = 1;
                tracker.style.borderTopRightRadius = 1;
                tracker.style.borderBottomLeftRadius = 1;
                tracker.style.borderBottomRightRadius = 1;
                tracker.style.borderTopWidth = 0;
                tracker.style.borderBottomWidth = 0;
                tracker.style.borderLeftWidth = 0;
                tracker.style.borderRightWidth = 0;
                tracker.style.marginTop = (16 - TrackHeight) * 0.5f;
            }

            var dragger = slider.Q<VisualElement>(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.width = ThumbDiameter;
                dragger.style.height = ThumbDiameter;
                dragger.style.borderTopLeftRadius = ThumbDiameter * 0.5f;
                dragger.style.borderTopRightRadius = ThumbDiameter * 0.5f;
                dragger.style.borderBottomLeftRadius = ThumbDiameter * 0.5f;
                dragger.style.borderBottomRightRadius = ThumbDiameter * 0.5f;
                dragger.style.backgroundColor = ThumbColor;
                dragger.style.borderTopWidth = 0;
                dragger.style.borderBottomWidth = 0;
                dragger.style.borderLeftWidth = 0;
                dragger.style.borderRightWidth = 0;
                // Vertically centre the thumb on the slim track.
                dragger.style.marginTop = (16 - ThumbDiameter) * 0.5f;
            }

            var draggerBorder = slider.Q<VisualElement>(className: "unity-base-slider__dragger-border");
            if (draggerBorder != null)
            {
                draggerBorder.style.borderTopWidth = 0;
                draggerBorder.style.borderBottomWidth = 0;
                draggerBorder.style.borderLeftWidth = 0;
                draggerBorder.style.borderRightWidth = 0;
            }
        }
    }
}
