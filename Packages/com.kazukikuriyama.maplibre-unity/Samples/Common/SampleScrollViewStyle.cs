using UnityEngine;
using UnityEngine.UIElements;

namespace MapLibre.Unity.Samples
{
    /// <summary>
    /// Slim scrollbar look for the default UI Toolkit <see cref="ScrollView"/>.
    ///
    /// The built-in scroller renders a 13–16px wide bar with chunky up/down
    /// arrow buttons that look out of place on the dark sample HUDs. This
    /// helper hides the buttons, narrows the bar, and replaces the track /
    /// thumb with a transparent track + a translucent rounded thumb -- the
    /// minimal "iOS-like" pattern.
    ///
    /// Apply once after the ScrollView is created. Children are rebuilt by
    /// UI Toolkit when the panel re-attaches, so the helper also hooks
    /// AttachToPanelEvent to re-apply styling each time.
    /// </summary>
    public static class SampleScrollViewStyle
    {
        private const float ScrollbarWidth = 6f;
        private static readonly Color TrackColor = new(0, 0, 0, 0f);
        private static readonly Color ThumbColor = new(1, 1, 1, 0.28f);

        public static void Apply(ScrollView scrollView)
        {
            // Re-apply on every layout pass. Unity's built-in USS for the
            // scroller asserts its own min-width on the slider's __input
            // and __dragger after our inline style is set, so a one-shot
            // Apply leaves the scrollbar at its default 13–16px chunk.
            scrollView.RegisterCallback<AttachToPanelEvent>(_ => Restyle(scrollView));
            scrollView.RegisterCallback<GeometryChangedEvent>(_ => Restyle(scrollView));
            Restyle(scrollView);
        }

        private static void Restyle(ScrollView scrollView)
        {
            if (scrollView.verticalScroller != null)
                ApplyToScroller(scrollView.verticalScroller, vertical: true);
            if (scrollView.horizontalScroller != null)
                ApplyToScroller(scrollView.horizontalScroller, vertical: false);
        }

        private static void ApplyToScroller(Scroller scroller, bool vertical)
        {
            // Hide the up / down arrow buttons -- modern UIs scroll on drag
            // alone and the arrows consume vertical real estate.
            if (scroller.lowButton != null)
                scroller.lowButton.style.display = DisplayStyle.None;
            if (scroller.highButton != null)
                scroller.highButton.style.display = DisplayStyle.None;

            // Size the scroller container. min/max width pinned so the
            // built-in min-width: 13px from the default sheet can't widen us.
            // Also add a small margin so the bar doesn't sit flush against
            // the ScrollView edge -- a few pixels of breathing room reads
            // as a polished native scrollbar.
            if (vertical)
            {
                scroller.style.width = ScrollbarWidth;
                scroller.style.minWidth = ScrollbarWidth;
                scroller.style.maxWidth = ScrollbarWidth;
                scroller.style.flexBasis = ScrollbarWidth;
                scroller.style.marginRight = 6;
                scroller.style.marginTop = 4;
                scroller.style.marginBottom = 4;
            }
            else
            {
                scroller.style.height = ScrollbarWidth;
                scroller.style.minHeight = ScrollbarWidth;
                scroller.style.maxHeight = ScrollbarWidth;
                scroller.style.flexBasis = ScrollbarWidth;
                scroller.style.marginBottom = 6;
                scroller.style.marginLeft = 4;
                scroller.style.marginRight = 4;
            }

            var slider = scroller.slider;
            if (slider == null) return;

            // Cancel the inset margins UI Toolkit applies between the (now
            // hidden) scroller buttons and the slider, so the slider fills
            // the full scroller axis. Lock min/max in the cross axis so the
            // 13px default min-width can't reassert.
            slider.style.marginTop = 0;
            slider.style.marginBottom = 0;
            slider.style.marginLeft = 0;
            slider.style.marginRight = 0;
            if (vertical)
            {
                slider.style.width = ScrollbarWidth;
                slider.style.minWidth = ScrollbarWidth;
                slider.style.maxWidth = ScrollbarWidth;
            }
            else
            {
                slider.style.height = ScrollbarWidth;
                slider.style.minHeight = ScrollbarWidth;
                slider.style.maxHeight = ScrollbarWidth;
            }

            // The slider has an internal "input" element wrapping tracker
            // and dragger; its USS defaults also impose a min-width that
            // the scroller inherits.
            var input = slider.Q<VisualElement>(className: "unity-base-slider__input");
            if (input != null)
            {
                if (vertical)
                {
                    input.style.width = Length.Percent(100);
                    input.style.minWidth = 0;
                }
                else
                {
                    input.style.height = Length.Percent(100);
                    input.style.minHeight = 0;
                }
            }

            // Recolour only -- leave geometry to the default stylesheet.
            var tracker = slider.Q<VisualElement>(className: "unity-base-slider__tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = TrackColor;
                tracker.style.borderTopWidth = 0;
                tracker.style.borderBottomWidth = 0;
                tracker.style.borderLeftWidth = 0;
                tracker.style.borderRightWidth = 0;
            }

            var dragger = slider.Q<VisualElement>(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = ThumbColor;
                dragger.style.borderTopLeftRadius = 3;
                dragger.style.borderTopRightRadius = 3;
                dragger.style.borderBottomLeftRadius = 3;
                dragger.style.borderBottomRightRadius = 3;
                dragger.style.borderTopWidth = 0;
                dragger.style.borderBottomWidth = 0;
                dragger.style.borderLeftWidth = 0;
                dragger.style.borderRightWidth = 0;
                // Stretch the thumb to fill the slim scroller axis. Without
                // this the dragger keeps its default ~14px intrinsic width
                // and gets clipped against the narrower scroller container.
                if (vertical)
                {
                    dragger.style.width = Length.Percent(100);
                    dragger.style.left = 0;
                    dragger.style.right = 0;
                    dragger.style.marginLeft = 0;
                    dragger.style.marginRight = 0;
                }
                else
                {
                    dragger.style.height = Length.Percent(100);
                    dragger.style.top = 0;
                    dragger.style.bottom = 0;
                    dragger.style.marginTop = 0;
                    dragger.style.marginBottom = 0;
                }
            }

            var draggerBorder = slider.Q<VisualElement>(className: "unity-base-slider__dragger-border");
            if (draggerBorder != null)
            {
                draggerBorder.style.borderTopWidth = 0;
                draggerBorder.style.borderBottomWidth = 0;
                draggerBorder.style.borderLeftWidth = 0;
                draggerBorder.style.borderRightWidth = 0;
                draggerBorder.style.backgroundColor = new Color(0, 0, 0, 0);
            }
        }
    }
}
