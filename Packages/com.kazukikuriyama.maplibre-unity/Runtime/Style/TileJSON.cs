using System;
using System.Collections.Generic;
using MapLibre.Unity.Source;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MapLibre.Unity.Style
{
    [System.Serializable]
    public class TileJSONData
    {
        public string TileJsonVersion;
        public string Name;
        public List<string> Tiles = new();
        public double[] Bounds;
        public double[] Center;
        public int MinZoom;
        public int MaxZoom = 22;
        public string Attribution;
    }

    public static class TileJSONFetcher
    {
        /// <summary>
        /// Fetch and parse a TileJSON document. Returns <c>null</c> when the
        /// request is aborted by <paramref name="transformRequest"/>. Throws
        /// <see cref="Exception"/> on HTTP / parse failure.
        /// </summary>
        public static async Awaitable<TileJSONData> FetchAsync(string url,
            RequestTransformFunction transformRequest = null)
        {
            var transformed = RequestTransformer.Apply(transformRequest, url, ResourceKind.Source);
            if (transformed.Abort)
                throw new Exception("TileJSON request aborted by transformRequest");

            using var request = UnityWebRequest.Get(transformed.Url);
            request.SetRequestHeader("User-Agent", "MapLibre-Unity/0.1");
            ApplyHeaders(request, transformed.Headers);

            await request.SendAsync();

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception(request.error);

            try
            {
                var json = JObject.Parse(request.downloadHandler.text);
                var data = new TileJSONData
                {
                    TileJsonVersion = json["tilejson"]?.ToString(),
                    Name = json["name"]?.ToString(),
                    MinZoom = json["minzoom"]?.ToObject<int>() ?? 0,
                    MaxZoom = json["maxzoom"]?.ToObject<int>() ?? 22,
                    Attribution = json["attribution"]?.ToString()
                };

                if (json["tiles"] is JArray tilesArray)
                {
                    foreach (var t in tilesArray)
                        data.Tiles.Add(t.ToString());
                }

                if (json["bounds"] is JArray boundsArray)
                    data.Bounds = boundsArray.ToObject<double[]>();

                if (json["center"] is JArray centerArray)
                    data.Center = centerArray.ToObject<double[]>();

                return data;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse TileJSON: {ex.Message}", ex);
            }
        }

        private static void ApplyHeaders(UnityWebRequest request,
            Dictionary<string, string> headers)
        {
            if (headers == null) return;
            foreach (var kvp in headers)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                    request.SetRequestHeader(kvp.Key, kvp.Value ?? string.Empty);
            }
        }
    }
}
