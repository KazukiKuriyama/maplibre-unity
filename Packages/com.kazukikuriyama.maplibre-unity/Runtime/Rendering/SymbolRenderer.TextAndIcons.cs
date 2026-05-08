using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Source;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// Text-field resolution, scale conversion, and the GameObject builders for
    /// SDF / TMP text and sprite icons. The mesh-building primitives that ship
    /// with the partial don't track any per-tile state -- they rely solely on
    /// the immutable fields owned by the main partial (font, sprite atlas,
    /// glyph source, icon material).
    /// </summary>
    public partial class SymbolRenderer
    {
        private string ResolveText(SymbolLayoutProperties layout, EvaluationContext ctx)
        {
            if (layout.TextField == null) return null;
            string text = layout.ResolveTextField(ctx);
            if (string.IsNullOrEmpty(text)) return null;

            // Handle MapLibre template strings like "{name}" or "{name:latin}"
            if (text.Contains("{") && ctx.Feature != null)
            {
                text = ResolveTemplateString(text, ctx.Feature);
            }
            return text;
        }

        private static string ResolveTemplateString(string template, VectorTileFeature feature)
        {
            var result = new System.Text.StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '{')
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end > i)
                    {
                        string key = template.Substring(i + 1, end - i - 1);
                        // Handle "name:latin" → try "name:latin" first, then "name"
                        var val = feature.GetProperty(key);
                        if (val == null && key.Contains(":"))
                        {
                            string baseKey = key.Substring(0, key.IndexOf(':'));
                            val = feature.GetProperty(baseKey);
                        }
                        result.Append(val?.ToString() ?? "");
                        i = end + 1;
                    }
                    else
                    {
                        result.Append(template[i]);
                        i++;
                    }
                }
                else
                {
                    result.Append(template[i]);
                    i++;
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// CSS pixel to physical pixel ratio for HiDPI displays.
        /// MapLibre text-size is in CSS pixels (device-independent).
        /// On Retina/HiDPI, 1 CSS pixel = multiple physical pixels.
        /// </summary>
        private static float CSSPixelRatio
        {
            get
            {
                if (Screen.dpi > 0f)
                    return Mathf.Max(Screen.dpi / 96f, 1f);
                return 1f;
            }
        }

        private static float ComputeTextScale(float textSize, float frustumHeight)
        {
            if (frustumHeight <= 0f) return 0f;
            float screenHeight = Mathf.Max(Screen.height, MinScreenHeight);
            // Convert CSS pixels to physical pixels, then to world units.
            // With isOrthographic=true, TMP mesh height ≈ TmpFontSize local units.
            return textSize * CSSPixelRatio * frustumHeight / (screenHeight * TmpFontSize);
        }

        /// <summary>
        /// Compute icon scale: converts CSS-pixel width to world units.
        /// The icon quad is 1x1 in local space, so scale = world size in units.
        /// </summary>
        private static float ComputeIconScale(float cssPixels, float frustumHeight)
        {
            if (frustumHeight <= 0f) return 0f;
            float screenHeight = Mathf.Max(Screen.height, MinScreenHeight);
            return cssPixels * CSSPixelRatio * frustumHeight / screenHeight;
        }

        private static Vector2? GetFeatureCentroid(VectorTileFeature feature, float invExtent)
        {
            if (feature.RawGeometry == null || feature.RawGeometry.Length == 0)
                return null;

            var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
            if (rings.Count == 0 || rings[0].Count == 0) return null;

            if (feature.Type == GeometryType.Point)
            {
                var pt = rings[0][0];
                return new Vector2(pt.x * invExtent, pt.y * invExtent);
            }

            // For lines: use midpoint of first ring
            var line = rings[0];
            float totalLen = 0f;
            for (int i = 1; i < line.Count; i++)
                totalLen += Vector2.Distance(line[i - 1], line[i]);

            float halfLen = totalLen * 0.5f;
            float acc = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float segLen = Vector2.Distance(line[i - 1], line[i]);
                if (acc + segLen >= halfLen)
                {
                    float t = (halfLen - acc) / segLen;
                    var pt = Vector2.Lerp(line[i - 1], line[i], t);
                    return new Vector2(pt.x * invExtent, pt.y * invExtent);
                }
                acc += segLen;
            }

            var last = line[line.Count / 2];
            return new Vector2(last.x * invExtent, last.y * invExtent);
        }

        /// <summary>
        /// Pull the primary font stack name out of a layout's text-font value.
        /// MapLibre style spec allows a string array (e.g. ["Open Sans Regular",
        /// "Arial Unicode MS"]); we use the first entry as the SDF fontstack
        /// since on-demand range fetching only supports one stack at a time.
        /// Falls back to "Open Sans Regular" -- the conventional MapLibre
        /// default -- when the expression is missing or evaluates oddly.
        /// </summary>
        private static string ResolveFontstack(SymbolLayoutProperties layout,
            EvaluationContext ctx)
        {
            if (layout?.TextFont == null) return "Open Sans Regular";
            var val = layout.TextFont.Evaluate(ctx);
            if (val is string s && !string.IsNullOrEmpty(s)) return s;
            if (val is System.Collections.IEnumerable list)
            {
                foreach (var item in list)
                {
                    if (item is string str && !string.IsNullOrEmpty(str)) return str;
                }
            }
            return "Open Sans Regular";
        }

        /// <summary>
        /// Build a Unity GameObject that renders <paramref name="text"/> via
        /// the SDF mesh path. Glyphs missing from the atlas trigger a fetch;
        /// if none of the glyphs are available yet, a stub GameObject is
        /// returned so the rest of the symbol-placement pipeline (collision
        /// detection, transform updates) doesn't have to special-case empty
        /// labels. Returns null only when the renderer prerequisites are
        /// unmet -- the caller falls back to TMP in that case.
        /// </summary>
        private GameObject CreateSdfTextObject(string text, string fontstack,
            Color color, float opacity, Color haloColor, float haloWidth)
        {
            var atlas = _glyphSource.Atlas;
            if (atlas == null) return null;

            // Kick off a fetch for any range that hasn't been loaded yet.
            // EnsureRange is cheap (idempotent) so calling it for every char
            // is fine even when most ranges are already cached.
            foreach (var c in text)
            {
                _glyphSource.EnsureRange(fontstack, c);
            }

            var mesh = SdfTextMeshBuilder.Build(text, fontstack, atlas, out bool anyMissing);

            var go = new GameObject(anyMissing ? "SdfLabel(partial)" : "SdfLabel");
            go.transform.SetParent(_parent, false);

            if (mesh != null)
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sortingOrder = 100;
                mr.sharedMaterial = new Material(_sdfTextShader);

                var block = new MaterialPropertyBlock();
                block.SetTexture(SdfGlyphAtlasPropertyId, atlas.Texture);
                block.SetColor(SdfTextColorPropertyId,
                    new Color(color.r, color.g, color.b, color.a * opacity));
                block.SetColor(SdfHaloColorPropertyId, haloColor);
                // Halo width is supplied in CSS pixels; the shader expects an
                // SDF-normalised value where 1.0 spans the full SDF padding
                // (3 px / 24 px font cell = 0.125 of unit distance).
                block.SetFloat(SdfHaloWidthPropertyId, Mathf.Clamp01(haloWidth / TmpFontSize));
                block.SetFloat(SdfOpacityPropertyId, opacity);
                mr.SetPropertyBlock(block);
            }

            // Match the TMP path's orientation: -90° around X lays text on the
            // XZ plane facing +Y. The caller flips Y scale to compensate for
            // glyph mesh y-down convention vs Unity world y-up.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            return go;
        }

        private GameObject CreateTextObject(string text, float fontSize, Color color,
            float opacity, Color haloColor, float haloWidth, string fontstack = null)
        {
            // SDF path: when the style has a glyphs URL and the caller supplied
            // a fontstack, build a self-rendered SDF mesh instead of using TMP.
            // This is the path used by MapLibre-spec stylesheets that ship a
            // glyph PBF endpoint (e.g. demotiles.maplibre.org).
            if (!string.IsNullOrEmpty(fontstack)
                && _glyphSource != null && _sdfTextShader != null)
            {
                var sdfGo = CreateSdfTextObject(text, fontstack,
                    color, opacity, haloColor, haloWidth);
                if (sdfGo != null) return sdfGo;
                // Fall through to TMP if SDF path fails (e.g. shader unset
                // mid-frame). Defensive -- should be rare in practice.
            }

            var go = new GameObject("Label");
            go.transform.SetParent(_parent, false);

            var tmp = go.AddComponent<TextMeshPro>();
            if (_fontAsset != null)
                tmp.font = _fontAsset;
            tmp.text = text;
            tmp.fontSize = TmpFontSize;
            tmp.isOrthographic = true; // Prevent TMP's 0.1x perspective scale factor
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.color = new Color(color.r, color.g, color.b, color.a * opacity);

            // Configure SDF outline for text halo (requires a valid font material)
            if (haloWidth > 0f && tmp.fontSharedMaterial != null)
            {
                tmp.outlineWidth = Mathf.Clamp01(haloWidth / TmpFontSize);
                tmp.outlineColor = new Color32(
                    (byte)(haloColor.r * 255),
                    (byte)(haloColor.g * 255),
                    (byte)(haloColor.b * 255),
                    (byte)(haloColor.a * 255));
            }

            // Lay text flat on XZ plane facing upward (+Y) toward camera.
            // Negate local Y scale in the caller to flip characters right-side-up.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Configure renderer
            var meshRenderer = tmp.renderer;
            if (meshRenderer != null)
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.sortingOrder = 100;
            }

            return go;
        }

        /// <summary>
        /// Create a GameObject for a sprite icon.
        /// The quad mesh has UVs mapped to the specific sprite region in the atlas.
        /// </summary>
        private GameObject CreateIconObject(SpriteEntry entry, Rect uvRect, Texture2D iconTexture,
            float opacity, Color color)
        {
            var go = new GameObject("Icon");
            go.transform.SetParent(_parent, false);

            // Create quad mesh with atlas UVs baked in
            var mesh = CreateIconQuadMesh(uvRect);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _iconMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 100;

            // Set per-instance properties. iconTexture may be the shared atlas
            // or a runtime-added texture (map.addImage).
            var block = new MaterialPropertyBlock();
            block.SetTexture(MainTexPropertyId, iconTexture);
            block.SetFloat(OpacityPropertyId, opacity);
            block.SetColor(ColorPropertyId, color);
            block.SetFloat(IsSdfPropertyId, entry.Sdf ? 1f : 0f);
            mr.SetPropertyBlock(block);

            // Lay flat on XZ plane, same convention as text labels.
            // Euler(-90,0,0) + negative Y scale flips the icon right-side-up.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            return go;
        }

        /// <summary>
        /// Create a unit quad mesh (-0.5 to 0.5) with UVs mapped to the given atlas rect.
        /// </summary>
        private static Mesh CreateIconQuadMesh(Rect uvRect)
        {
            var mesh = new Mesh();
            mesh.name = "IconQuad";

            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };

            mesh.uv = new[]
            {
                new Vector2(uvRect.xMin, uvRect.yMin),
                new Vector2(uvRect.xMax, uvRect.yMin),
                new Vector2(uvRect.xMax, uvRect.yMax),
                new Vector2(uvRect.xMin, uvRect.yMax),
            };

            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
