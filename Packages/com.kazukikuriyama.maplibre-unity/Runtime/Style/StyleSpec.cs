using System.Collections.Generic;
using UnityEngine;

namespace MapLibre.Unity.Style
{
    [System.Serializable]
    public class MapLibreStyle
    {
        public int Version;
        public string Name;
        public LngLat? Center;
        public float? Zoom;
        public float? Bearing;
        public float? Pitch;
        public string Sprite;
        public string Glyphs;
        public Dictionary<string, SourceDefinition> Sources = new();
        public List<LayerDefinition> Layers = new();
        public TransitionDefinition Transition;

        /// <summary>
        /// Optional 3D terrain definition. When non-null, raster layers are rendered
        /// with vertex displacement based on the referenced raster-dem source.
        /// </summary>
        public TerrainDefinition Terrain;

        /// <summary>
        /// Style root "light" property. Controls directional/ambient lighting for
        /// 3D layers (fill-extrusion). Optional -- null falls back to defaults.
        /// </summary>
        public LightDefinition Light;

        /// <summary>
        /// Style root "sky" property. Drives the screen-space sky gradient drawn
        /// behind the map (visible at high pitch).
        /// </summary>
        public SkyDefinition Sky;
    }

    [System.Serializable]
    public class TransitionDefinition
    {
        public int Duration = 300;
        public int Delay = 0;
    }

    /// <summary>
    /// MapLibre Style Spec "light" root property. Controls directional lighting
    /// applied to 3D layers (currently fill-extrusion).
    /// See: https://maplibre.org/maplibre-style-spec/light/
    /// </summary>
    [System.Serializable]
    public class LightDefinition
    {
        /// <summary>"viewport" (default) or "map". Position is relative to this anchor.</summary>
        public string Anchor = "viewport";

        /// <summary>
        /// [radial, azimuthal, polar] in [unit-radius, degrees, degrees].
        /// Default per spec: [1.15, 210, 30].
        /// Azimuth is measured CW from north; polar is the angle from straight-up.
        /// </summary>
        public float[] Position = new[] { 1.15f, 210f, 30f };

        /// <summary>Light color. Default white.</summary>
        public Color Color = Color.white;

        /// <summary>Intensity in [0, 1]. Default 0.5.</summary>
        public float Intensity = 0.5f;
    }

    /// <summary>
    /// MapLibre Style Spec "sky" root property. Drives the screen-space gradient
    /// drawn behind the map and visible at high pitch. Field names mirror the
    /// current MapLibre Style Spec (sky-color / horizon-color / sky-horizon-blend
    /// / horizon-fog-blend / fog-color / fog-ground-blend / atmosphere-blend).
    /// See: https://maplibre.org/maplibre-style-spec/sky/
    /// </summary>
    [System.Serializable]
    public class SkyDefinition
    {
        /// <summary>Color at the top of the sky. Spec default rgba(85,151,210,1).</summary>
        public Color SkyColor = new(85f / 255f, 151f / 255f, 210f / 255f, 1f);

        /// <summary>Color at the horizon. Spec default white.</summary>
        public Color HorizonColor = new(1f, 1f, 1f, 1f);

        /// <summary>
        /// Fraction (0..1) of the sky region that fades from horizon-color into
        /// sky-color. 0 = sharp transition, 1 = soft gradient. Spec default 0.8.
        /// </summary>
        public float SkyHorizonBlend = 0.8f;

        /// <summary>
        /// Fraction (0..1) of the horizon-blend region that further blends into
        /// fog-color. Spec default 0.8. (Parsed; renderer currently ignores.)
        /// </summary>
        public float HorizonFogBlend = 0.8f;

        /// <summary>Fog color. Spec default rgba(255,255,255,0.5).</summary>
        public Color FogColor = new(1f, 1f, 1f, 0.5f);

        /// <summary>Fog/ground blend. Spec default 0.5.</summary>
        public float FogGroundBlend = 0.5f;

        /// <summary>
        /// Atmosphere blend factor (0..1). When 0 the sky is rendered as a flat
        /// gradient; positive values gradually mix in an atmospheric tint. Spec
        /// default is a zoom-driven interpolate that fades it out by zoom 7.
        /// (Parsed; renderer currently ignores.)
        /// </summary>
        public float AtmosphereBlend = 1f;
    }
}
