using System.Collections.Generic;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLibre.Unity.Rendering
{
    /// <summary>
    /// symbol-placement: "line" path. Lays text along LineString geometry --
    /// either by warping a TMP mesh or by baking a curved SDF mesh through
    /// <see cref="SdfLineTextMeshBuilder"/>. All of the path-sampling /
    /// max-angle / left-to-right helpers used by both flows live here too.
    /// </summary>
    public partial class SymbolRenderer
    {
        /// <summary>
        /// Maximum angle (degrees) between adjacent characters.
        /// Labels on segments with sharper turns are rejected.
        /// Matches MapLibre GL JS default for text-max-angle (45°).
        /// </summary>
        private const float DefaultMaxAngleDeg = 45f;

        private void ShowTileLinePlacement(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, SymbolLayoutProperties layoutProps,
            SymbolPaintProperties paintProps, EvaluationContext ctx,
            MercatorCoordinate mapCenter, float zoom)
        {
            float textSize = layoutProps.ResolveTextSize(ctx);
            Color textColor = paintProps.ResolveTextColor(ctx);
            float textOpacity = paintProps.ResolveTextOpacity(ctx);
            Color haloColor = paintProps.ResolveTextHaloColor(ctx);
            float haloWidth = paintProps.ResolveTextHaloWidth(ctx);
            float symbolSpacing = layoutProps.ResolveSymbolSpacing(ctx);

            if (layoutProps.TextSize != null)
                _textSizeExpr = layoutProps.TextSize;

            ApplyPaintTranslate(paintProps);

            var features = FilterFeaturesForLine(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;
            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;
            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 tileWorldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            float tileWorldSize = (float)(tileSize * worldScale);
            float invExtent = 1f / tileLayer.Extent;

            float fh = _lastFrustumHeight;
            // Defer line placement until frustumHeight is available -- textScale=0 would
            // collapse all vertices to a single point and produce invisible labels.
            if (fh <= 0f)
            {
                _requestedTiles.Remove(tileId);
                return;
            }
            float textScale = ComputeTextScale(textSize, fh);
            float yOffset = ComputeYOffset(fh);

            var lineLabels = new List<LineLabelData>();
            int count = 0;

            foreach (var feature in features)
            {
                if (count >= _maxLabelsPerTile) break;

                var featureCtx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer, feature);
                string text = ResolveText(layoutProps, featureCtx);
                if (string.IsNullOrEmpty(text)) continue;

                float textPadding = layoutProps.ResolveTextPadding(featureCtx);
                bool textAllowOverlap = layoutProps.ResolveTextAllowOverlap(featureCtx);
                bool textIgnorePlacement = layoutProps.ResolveTextIgnorePlacement(featureCtx);
                float sortKey = layoutProps.ResolveSymbolSortKey(featureCtx);

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                if (rings.Count == 0) continue;

                foreach (var ring in rings)
                {
                    if (count >= _maxLabelsPerTile) break;
                    if (ring.Count < 2) continue;

                    // Convert to normalized tile-local coordinates (0-1)
                    var normalizedPath = new Vector2[ring.Count];
                    for (int i = 0; i < ring.Count; i++)
                        normalizedPath[i] = new Vector2(ring[i].x * invExtent, ring[i].y * invExtent);

                    // Compute path in world space for text fitting check
                    var worldPath = new Vector2[normalizedPath.Length];
                    for (int i = 0; i < normalizedPath.Length; i++)
                    {
                        float wx = (normalizedPath[i].x - 0.5f) * tileWorldSize;
                        float wz = -(normalizedPath[i].y - 0.5f) * tileWorldSize;
                        worldPath[i] = new Vector2(tileWorldPos.x + wx, tileWorldPos.z + wz);
                    }

                    float pathLength = ComputePathLength(worldPath);

                    // Create TMP to measure text width -- must be active for ForceMeshUpdate
                    var go = CreateTextObject(text, textSize, textColor, textOpacity, haloColor, haloWidth);
                    var tmp = go.GetComponent<TextMeshPro>();
                    tmp.ForceMeshUpdate();
                    float textWorldWidth = tmp.preferredWidth * textScale;
                    go.SetActive(false);

                    // If symbol-spacing is set, place multiple labels along the path
                    float spacingWorld = symbolSpacing * CSSPixelRatio * fh /
                        Mathf.Max(Screen.height, MinScreenHeight);
                    if (spacingWorld <= 0f) spacingWorld = textWorldWidth * 2f;

                    // Find placement positions along the path
                    var placements = ComputeLinePlacements(pathLength, textWorldWidth, spacingWorld);
                    if (placements.Count == 0)
                    {
                        Object.Destroy(go);
                        continue;
                    }

                    // Use the first placement for the already-created TMP
                    bool firstPlaced = false;
                    foreach (float midDist in placements)
                    {
                        if (count >= _maxLabelsPerTile) break;

                        // Check max angle at this placement
                        if (!CheckMaxAngle(worldPath, midDist, textWorldWidth, textScale))
                            continue;

                        TextMeshPro labelTmp;
                        GameObject labelGo;
                        if (!firstPlaced)
                        {
                            labelGo = go;
                            labelTmp = tmp;
                            firstPlaced = true;
                        }
                        else
                        {
                            labelGo = CreateTextObject(text, textSize, textColor, textOpacity,
                                haloColor, haloWidth);
                            labelTmp = labelGo.GetComponent<TextMeshPro>();
                            labelTmp.ForceMeshUpdate();
                            labelGo.SetActive(false);
                        }

                        // Remove default rotation; we place vertices directly on XZ plane
                        labelGo.transform.localRotation = Quaternion.identity;
                        labelGo.transform.localScale = Vector3.one;

                        // text-keep-upright: when true (default), reverse the path
                        // direction whenever the text would render right-to-left in screen
                        // space. When false, render exactly along the path order.
                        float orientedMidDist = midDist;
                        Vector2[] orientedPath;
                        if (layoutProps.TextKeepUpright)
                        {
                            orientedPath = EnsureLeftToRight(worldPath, midDist, textWorldWidth,
                                out bool wasReversed);
                            if (wasReversed)
                                orientedMidDist = pathLength - midDist;
                        }
                        else
                        {
                            orientedPath = worldPath;
                        }

                        WarpTextAlongPath(labelTmp, orientedPath, orientedMidDist, textScale,
                            yOffset, tileWorldPos);

                        labelGo.SetActive(false); // Hidden until collision detection

                        lineLabels.Add(new LineLabelData
                        {
                            Go = labelGo,
                            Tmp = labelTmp,
                            TileId = tileId,
                            NormalizedPath = normalizedPath,
                            CharCount = text.Length,
                            PaddingCSS = textPadding,
                            AllowOverlap = textAllowOverlap,
                            IgnorePlacement = textIgnorePlacement,
                            SortKey = sortKey,
                            KeepUpright = layoutProps.TextKeepUpright,
                        });
                        count++;
                    }

                    // If no placement used the initial GO, destroy it
                    if (!firstPlaced)
                        Object.Destroy(go);
                }
            }

            if (lineLabels.Count > 0)
                _activeLineLabels[tileId] = lineLabels;
        }

        // SDF variant of <see cref="ShowTileLinePlacement"/>. The TMP path queries
        // <c>preferredWidth</c> from a temporarily-instantiated TextMeshPro to size
        // labels along the curve; here we measure once via the glyph atlas and bake
        // the curved mesh up front. The trade-off is that the mesh is frozen at the
        // dispatch zoom -- pan still tracks via parent-localPosition updates, but a
        // major zoom change requires the tile to be re-issued (which TileManager
        // does at zoom step boundaries).
        private void ShowTileLinePlacementSdf(CanonicalTileID tileId, VectorTileLayer tileLayer,
            LayerDefinition layerDef, SymbolLayoutProperties layoutProps,
            SymbolPaintProperties paintProps, EvaluationContext ctx,
            MercatorCoordinate mapCenter, float zoom)
        {
            float textSize = layoutProps.ResolveTextSize(ctx);
            Color textColor = paintProps.ResolveTextColor(ctx);
            float textOpacity = paintProps.ResolveTextOpacity(ctx);
            Color haloColor = paintProps.ResolveTextHaloColor(ctx);
            float haloWidth = paintProps.ResolveTextHaloWidth(ctx);
            float symbolSpacing = layoutProps.ResolveSymbolSpacing(ctx);

            if (layoutProps.TextSize != null)
                _textSizeExpr = layoutProps.TextSize;

            ApplyPaintTranslate(paintProps);

            var features = FilterFeaturesForLine(tileLayer, layerDef.Filter, zoom,
                layerDef.Source, layerDef.SourceLayer);
            if (features.Count == 0) return;

            float worldScale = CoordinateConversion.GetWorldScale(zoom);
            int n = 1 << tileId.Z;
            double tileSize = 1.0 / n;
            double tileCenterX = (tileId.X + 0.5) * tileSize;
            double tileCenterY = (tileId.Y + 0.5) * tileSize;
            var tileCenterMerc = new MercatorCoordinate(tileCenterX, tileCenterY);
            Vector3 tileWorldPos = CoordinateConversion.MercatorToUnityWorld(tileCenterMerc, mapCenter, worldScale);
            float tileWorldSize = (float)(tileSize * worldScale);
            float invExtent = 1f / tileLayer.Extent;

            float fh = _lastFrustumHeight;
            // Defer line placement until frustumHeight is available -- textScale=0 would
            // collapse all vertices to a single point and produce invisible labels.
            if (fh <= 0f)
            {
                _requestedTiles.Remove(tileId);
                return;
            }
            float textScale = ComputeTextScale(textSize, fh);
            float yOffset = ComputeYOffset(fh);

            var atlas = _glyphSource.Atlas;
            var lineLabels = new List<LineLabelData>();
            int count = 0;
            bool tileHasMissingGlyphs = false;

            foreach (var feature in features)
            {
                if (count >= _maxLabelsPerTile) break;

                var featureCtx = MakeContext(zoom, layerDef.Source, layerDef.SourceLayer, feature);
                string text = ResolveText(layoutProps, featureCtx);
                if (string.IsNullOrEmpty(text)) continue;

                string fontstack = ResolveFontstack(layoutProps, featureCtx);
                // Schedule on-demand fetch for any range that hasn't been seen yet.
                // EnsureRange is idempotent so this is cheap on the hot path.
                foreach (var c in text)
                    _glyphSource.EnsureRange(fontstack, c);

                float textPadding = layoutProps.ResolveTextPadding(featureCtx);
                bool textAllowOverlap = layoutProps.ResolveTextAllowOverlap(featureCtx);
                bool textIgnorePlacement = layoutProps.ResolveTextIgnorePlacement(featureCtx);
                float sortKey = layoutProps.ResolveSymbolSortKey(featureCtx);

                var measure = SdfTextMeshBuilder.Measure(text, fontstack, atlas);
                float textWorldWidth = measure.Width * textScale;
                if (measure.AnyMissing) tileHasMissingGlyphs = true;
                if (textWorldWidth <= 0f) continue;

                var rings = GeometryDecoder.Decode(feature.RawGeometry, feature.Type);
                if (rings.Count == 0) continue;

                foreach (var ring in rings)
                {
                    if (count >= _maxLabelsPerTile) break;
                    if (ring.Count < 2) continue;

                    var normalizedPath = new Vector2[ring.Count];
                    for (int i = 0; i < ring.Count; i++)
                        normalizedPath[i] = new Vector2(ring[i].x * invExtent, ring[i].y * invExtent);

                    // Bake the path in tile-local world coordinates so the mesh can
                    // be re-positioned by setting GameObject.localPosition each frame.
                    var tileLocalPath = new Vector2[normalizedPath.Length];
                    for (int i = 0; i < normalizedPath.Length; i++)
                    {
                        float lx = (normalizedPath[i].x - 0.5f) * tileWorldSize;
                        float lz = -(normalizedPath[i].y - 0.5f) * tileWorldSize;
                        tileLocalPath[i] = new Vector2(lx, lz);
                    }

                    float pathLength = ComputePathLength(tileLocalPath);
                    float spacingWorld = symbolSpacing * CSSPixelRatio * fh /
                        Mathf.Max(Screen.height, MinScreenHeight);
                    if (spacingWorld <= 0f) spacingWorld = textWorldWidth * 2f;

                    var placements = ComputeLinePlacements(pathLength, textWorldWidth, spacingWorld);
                    if (placements.Count == 0) continue;

                    foreach (float midDist in placements)
                    {
                        if (count >= _maxLabelsPerTile) break;
                        if (!CheckMaxAngle(tileLocalPath, midDist, textWorldWidth, textScale))
                            continue;

                        float orientedMidDist = midDist;
                        Vector2[] orientedPath;
                        if (layoutProps.TextKeepUpright)
                        {
                            orientedPath = EnsureLeftToRight(tileLocalPath, midDist, textWorldWidth,
                                out bool wasReversed);
                            if (wasReversed)
                                orientedMidDist = pathLength - midDist;
                        }
                        else
                        {
                            orientedPath = tileLocalPath;
                        }

                        var mesh = SdfLineTextMeshBuilder.Build(text, fontstack, atlas,
                            orientedPath, orientedMidDist, textScale, out bool buildAnyMissing);
                        if (mesh == null) continue;
                        if (buildAnyMissing) tileHasMissingGlyphs = true;

                        var go = new GameObject(buildAnyMissing ? "SdfLineLabel(partial)" : "SdfLineLabel");
                        go.transform.SetParent(_parent, false);
                        go.transform.localPosition = new Vector3(tileWorldPos.x, yOffset, tileWorldPos.z);
                        go.transform.localRotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;

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
                            new Color(textColor.r, textColor.g, textColor.b, textColor.a * textOpacity));
                        block.SetColor(SdfHaloColorPropertyId, haloColor);
                        // Same SDF-padding normalisation as the point-placement path.
                        block.SetFloat(SdfHaloWidthPropertyId, Mathf.Clamp01(haloWidth / TmpFontSize));
                        block.SetFloat(SdfOpacityPropertyId, textOpacity);
                        mr.SetPropertyBlock(block);

                        go.SetActive(false); // Hidden until collision detection runs

                        lineLabels.Add(new LineLabelData
                        {
                            Go = go,
                            Tmp = null, // SDF entry -- UpdateAllPositions skips re-warping
                            TileId = tileId,
                            NormalizedPath = normalizedPath,
                            CharCount = text.Length,
                            PaddingCSS = textPadding,
                            AllowOverlap = textAllowOverlap,
                            IgnorePlacement = textIgnorePlacement,
                            SortKey = sortKey,
                            KeepUpright = layoutProps.TextKeepUpright,
                        });
                        count++;
                    }
                }
            }

            if (lineLabels.Count > 0)
                _activeLineLabels[tileId] = lineLabels;

            if (tileHasMissingGlyphs)
                _tilesWithMissingGlyphs.Add(tileId);
        }

        /// <summary>Filter features for line placement -- only LineString geometry.</summary>
        private List<VectorTileFeature> FilterFeaturesForLine(VectorTileLayer layer,
            Expression filter, float zoom, string sourceId, string sourceLayer)
        {
            var result = new List<VectorTileFeature>();
            foreach (var feature in layer.Features)
            {
                if (feature.Type != GeometryType.LineString) continue;
                if (filter != null)
                {
                    var ctx = MakeContext(zoom, sourceId, sourceLayer, feature);
                    if (!filter.EvaluateBool(ctx)) continue;
                }
                result.Add(feature);
            }
            return result;
        }

        /// <summary>Compute total length of a 2D path.</summary>
        private static float ComputePathLength(Vector2[] path)
        {
            float len = 0f;
            for (int i = 1; i < path.Length; i++)
                len += Vector2.Distance(path[i - 1], path[i]);
            return len;
        }

        /// <summary>
        /// Compute placement positions (distances from path start to label midpoint).
        /// Returns empty if text doesn't fit at all.
        /// </summary>
        private static List<float> ComputeLinePlacements(float pathLength, float textWidth,
            float spacing)
        {
            var result = new List<float>();
            if (textWidth > pathLength * 0.8f) return result;

            // Place first label at midpoint, then repeat at spacing intervals
            float mid = pathLength * 0.5f;
            result.Add(mid);

            // Additional labels before midpoint
            for (float d = mid - spacing; d >= textWidth * 0.5f; d -= spacing)
                result.Add(d);

            // Additional labels after midpoint
            for (float d = mid + spacing; d <= pathLength - textWidth * 0.5f; d += spacing)
                result.Add(d);

            return result;
        }

        /// <summary>
        /// Check that the path curvature at the label placement doesn't exceed max angle.
        /// </summary>
        private static bool CheckMaxAngle(Vector2[] path, float midDist, float textWidth,
            float textScale)
        {
            float halfWidth = textWidth * 0.5f;
            float startDist = midDist - halfWidth;
            float endDist = midDist + halfWidth;

            // Sample several points along the text extent and check angle between tangents
            const int samples = 4;
            float step = textWidth / samples;
            Vector2 prevTangent = default;
            bool hasPrev = false;

            for (int i = 0; i <= samples; i++)
            {
                float d = startDist + i * step;
                SamplePathAtDistance(path, d, out _, out var tangent);
                if (hasPrev)
                {
                    float dot = Vector2.Dot(prevTangent, tangent);
                    float angleDeg = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
                    if (angleDeg > DefaultMaxAngleDeg)
                        return false;
                }
                prevTangent = tangent;
                hasPrev = true;
            }
            return true;
        }

        /// <summary>
        /// Ensure the path reads left-to-right at the label placement position.
        /// Returns a potentially reversed copy of the path.
        /// </summary>
        /// <summary>
        /// If the text along this path would render right-to-left on screen, reverse the
        /// path so it reads left-to-right. The screen-X direction depends on the camera
        /// bearing -- at bearing=0 it equals world +X, at bearing=90 it equals world -Z.
        /// Pass <paramref name="bearingDeg"/>=0 to fall back to world-X comparison.
        /// </summary>
        private static Vector2[] EnsureLeftToRight(Vector2[] path, float midDist, float textWidth,
            float bearingDeg, out bool wasReversed)
        {
            wasReversed = false;
            float halfWidth = textWidth * 0.5f;
            SamplePathAtDistance(path, midDist - halfWidth, out var startPt, out _);
            SamplePathAtDistance(path, midDist + halfWidth, out var endPt, out _);

            // path uses world (x, z) per LineLabelData re-warp:
            //   worldPath[i] = (worldX, worldZ)
            // Project (dx, dz) onto camera right axis to get screen-X displacement.
            // Camera right = (cos b, -sin b) in world XZ, so screen_x = dx*cos b - dz*sin b.
            float dx = endPt.x - startPt.x;
            float dz = endPt.y - startPt.y;
            float bearingRad = bearingDeg * Mathf.Deg2Rad;
            float screenX = dx * Mathf.Cos(bearingRad) - dz * Mathf.Sin(bearingRad);

            if (screenX < 0)
            {
                wasReversed = true;
                var reversed = new Vector2[path.Length];
                for (int i = 0; i < path.Length; i++)
                    reversed[i] = path[path.Length - 1 - i];
                return reversed;
            }
            return path;
        }

        // Backward-compat overload -- defaults to bearing=0 (world-X check).
        private static Vector2[] EnsureLeftToRight(Vector2[] path, float midDist, float textWidth,
            out bool wasReversed)
            => EnsureLeftToRight(path, midDist, textWidth, 0f, out wasReversed);

        /// <summary>
        /// Sample position and normalized tangent at a given distance along a 2D path.
        /// </summary>
        private static void SamplePathAtDistance(Vector2[] path, float distance,
            out Vector2 position, out Vector2 tangent)
        {
            if (path.Length < 2)
            {
                position = path.Length > 0 ? path[0] : Vector2.zero;
                tangent = Vector2.right;
                return;
            }

            float acc = 0f;
            for (int i = 1; i < path.Length; i++)
            {
                float segLen = Vector2.Distance(path[i - 1], path[i]);
                if (segLen < 0.0001f) continue;

                if (acc + segLen >= distance)
                {
                    float t = (distance - acc) / segLen;
                    position = Vector2.Lerp(path[i - 1], path[i], t);
                    tangent = (path[i] - path[i - 1]).normalized;
                    return;
                }
                acc += segLen;
            }

            // Past the end -- clamp to last segment
            position = path[path.Length - 1];
            tangent = (path[path.Length - 1] - path[path.Length - 2]).normalized;
        }

        /// <summary>
        /// Warp TMP text vertices along a 2D path on the XZ plane.
        /// The TMP object must have identity rotation and scale=1.
        /// </summary>
        private static void WarpTextAlongPath(TextMeshPro tmp, Vector2[] worldPath,
            float midDist, float textScale, float yOffset, Vector3 parentWorldPos)
        {
            var textInfo = tmp.textInfo;
            if (textInfo.characterCount == 0) return;

            // Parent position to make vertices relative
            float parentX = parentWorldPos.x;
            float parentZ = parentWorldPos.z;

            // midDist is the distance along the path where the label center should be placed
            float pathMidDist = midDist;
            float pathLen = ComputePathLength(worldPath);

            for (int c = 0; c < textInfo.characterCount; c++)
            {
                var charInfo = textInfo.characterInfo[c];
                if (!charInfo.isVisible) continue;

                int matIdx = charInfo.materialReferenceIndex;
                int vertIdx = charInfo.vertexIndex;
                var verts = textInfo.meshInfo[matIdx].vertices;

                // Character center in TMP local space (XY plane)
                float charMidX = (charInfo.bottomLeft.x + charInfo.topRight.x) * 0.5f;
                float charMidY = (charInfo.bottomLeft.y + charInfo.topRight.y) * 0.5f;

                // Map character center X to distance along the world path
                // charMidX is in TMP local units; multiply by textScale to get world distance from text center
                float distFromCenter = charMidX * textScale;
                float distOnPath = pathMidDist + distFromCenter;
                distOnPath = Mathf.Clamp(distOnPath, 0f, pathLen);

                SamplePathAtDistance(worldPath, distOnPath, out var pathPt, out var tangent);

                // Angle of tangent on 2D XZ plane
                float angle = Mathf.Atan2(tangent.y, tangent.x);
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                // Transform each of the 4 character vertices
                for (int v = 0; v < 4; v++)
                {
                    // Offset from character center in TMP local space
                    float dx = (verts[vertIdx + v].x - charMidX) * textScale;
                    float dy = (verts[vertIdx + v].y - charMidY) * textScale;

                    // Rotate by tangent angle (maps TMP X→path direction, TMP Y→perpendicular)
                    float rx = dx * cos - dy * sin;
                    float rz = dx * sin + dy * cos;

                    // Place on XZ plane relative to parent
                    verts[vertIdx + v] = new Vector3(
                        pathPt.x + rx - parentX,
                        yOffset,
                        pathPt.y + rz - parentZ  // pathPt.y = world Z
                    );
                }
            }

            // Push modified vertices to the mesh
            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                var meshInfo = textInfo.meshInfo[i];
                meshInfo.mesh.vertices = meshInfo.vertices;
                tmp.UpdateGeometry(meshInfo.mesh, i);
            }

            // Set parent position at tile world pos
            tmp.transform.localPosition = parentWorldPos;

            // Recalculate normals to face up (+Y) for top-down visibility
            for (int i = 0; i < textInfo.meshInfo.Length; i++)
            {
                var mesh = textInfo.meshInfo[i].mesh;
                var normals = mesh.normals;
                for (int n = 0; n < normals.Length; n++)
                    normals[n] = Vector3.up;
                mesh.normals = normals;
            }
        }
    }
}
