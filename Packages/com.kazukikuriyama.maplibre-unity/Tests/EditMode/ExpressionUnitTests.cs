using System.Collections.Generic;
using System.Linq;
using MapLibre.Unity.Expressions;
using MapLibre.Unity.VectorTile;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MapLibre.Unity.Tests.EditMode
{
    /// <summary>
    /// Coverage of the public expression operators used by the style spec.
    /// Each test parses a JSON expression through <see cref="ExpressionParser"/>
    /// -- the same path the style loader uses -- so we exercise both parsing
    /// and evaluation. JSON literal here is more readable than constructing
    /// expression nodes by hand and matches how regressions would surface
    /// from a real style.
    /// </summary>
    public class ExpressionUnitTests
    {
        // Parses a JSON-shaped operator (e.g. ["==", 1, 1]) into an Expression.
        // bool isColor = false because every test here returns a primitive;
        // colour-typed paths are exercised separately under
        // <see cref="ColorSpaceConversionTests"/>.
        private static Expression ParseExpr(string json)
            => ExpressionParser.Parse(JToken.Parse(json), isColor: false);

        // Build a feature whose tag table encodes the supplied JSON object.
        // Mirrors the on-disk MVT layout (key index + value index per tag)
        // because VectorTileFeature.GetProperty walks Tags via the layer's
        // Keys/Values tables -- there is no shortcut Properties dictionary.
        private static VectorTileFeature MakeFeature(string propertiesJson, ulong id = 0)
        {
            var layer = new VectorTileLayer();
            var tags = new List<uint>();
            foreach (var kv in JObject.Parse(propertiesJson))
            {
                var v = new VectorTileValue();
                switch (kv.Value.Type)
                {
                    case JTokenType.Integer: v.IntValue = (long)kv.Value; break;
                    case JTokenType.Float:   v.DoubleValue = (double)kv.Value; break;
                    case JTokenType.Boolean: v.BoolValue = (bool)kv.Value; break;
                    case JTokenType.String:  v.StringValue = (string)kv.Value; break;
                    default:                 v.StringValue = kv.Value.ToString(); break;
                }
                layer._keys.Add(kv.Key);
                layer._values.Add(v);
                tags.Add((uint)(layer.Keys.Count - 1));
                tags.Add((uint)(layer.Values.Count - 1));
            }

            return new VectorTileFeature
            {
                Id = id,
                Type = GeometryType.Point,
                Layer = layer,
                Tags = tags.ToArray(),
            };
        }

        // === Identity / value operators ===

        [Test]
        public void Literal_ReturnsValue()
        {
            var e = ParseExpr("[\"literal\", 42]");
            Assert.AreEqual(42, e.EvaluateFloat(new EvaluationContext(0f)));
        }

        [Test]
        public void Zoom_ReturnsContextZoom()
        {
            var e = ParseExpr("[\"zoom\"]");
            Assert.AreEqual(7.5f, e.EvaluateFloat(new EvaluationContext(7.5f)));
        }

        [Test]
        public void Get_ReturnsFeatureProperty()
        {
            var e = ParseExpr("[\"get\", \"name\"]");
            var f = MakeFeature("{\"name\": \"Tokyo\"}");
            Assert.AreEqual("Tokyo", e.EvaluateString(new EvaluationContext(0f, f)));
        }

        [Test]
        public void Has_ReturnsTrueWhenPropertyPresent()
        {
            var hasName = ParseExpr("[\"has\", \"name\"]");
            var f = MakeFeature("{\"name\": \"Tokyo\"}");
            Assert.IsTrue(hasName.EvaluateBool(new EvaluationContext(0f, f)));
        }

        [Test]
        public void Has_ReturnsFalseWhenPropertyAbsent()
        {
            var hasName = ParseExpr("[\"has\", \"name\"]");
            var f = MakeFeature("{\"other\": 1}");
            Assert.IsFalse(hasName.EvaluateBool(new EvaluationContext(0f, f)));
        }

        // === Interpolation ===

        [Test]
        public void Interpolate_LinearAtMidpoint()
        {
            var e = ParseExpr(
                "[\"interpolate\", [\"linear\"], [\"zoom\"], 0, 10, 10, 30]");
            // halfway between zoom=0 (=10) and zoom=10 (=30) → 20
            Assert.AreEqual(20f, e.EvaluateFloat(new EvaluationContext(5f)));
        }

        [Test]
        public void Interpolate_BelowFirstStop_ClampsToFirst()
        {
            var e = ParseExpr(
                "[\"interpolate\", [\"linear\"], [\"zoom\"], 5, 10, 10, 30]");
            Assert.AreEqual(10f, e.EvaluateFloat(new EvaluationContext(0f)));
        }

        [Test]
        public void Interpolate_AboveLastStop_ClampsToLast()
        {
            var e = ParseExpr(
                "[\"interpolate\", [\"linear\"], [\"zoom\"], 5, 10, 10, 30]");
            Assert.AreEqual(30f, e.EvaluateFloat(new EvaluationContext(15f)));
        }

        // Runtime AddLayer typically passes paint values as plain C# arrays
        // (`new object[] { "interpolate", ... }`) instead of JArray. The parser
        // must normalize them -- otherwise the array is treated as a literal and
        // colour expressions silently evaluate to the default (black).
        [Test]
        public void Parse_AcceptsPlainCSharpArrayAsExpression()
        {
            var raw = new object[]
            {
                "interpolate",
                new object[] { "linear" },
                new object[] { "zoom" },
                0, 10,
                10, 30,
            };
            var e = ExpressionParser.Parse(raw);
            Assert.AreEqual(20f, e.EvaluateFloat(new EvaluationContext(5f)));
        }

        [Test]
        public void Parse_AcceptsPlainCSharpArrayForColorInterpolation()
        {
            // Same shape as ColorInterpolationDemo. At zoom 10 (midpoint of
            // [8, 12]) RGB lerp yields (0.5, 0, 0.5) -- a real colour, not the
            // black default that signals "expression not recognised".
            var raw = new object[]
            {
                "interpolate",
                new object[] { "linear" },
                new object[] { "zoom" },
                8,  "#ff0000",
                12, "#0000ff",
            };
            var e = ExpressionParser.Parse(raw, isColor: true);
            var c = e.EvaluateColor(new EvaluationContext(10f));
            Assert.That(c.r, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(c.g, Is.EqualTo(0f).Within(0.01f));
            Assert.That(c.b, Is.EqualTo(0.5f).Within(0.01f));
        }

        // text-font is a string-array literal (e.g. ["Open Sans Regular"]) --
        // not an expression. Earlier the parser would route it through
        // ParseExpression with an "unknown operator" warning and collapse to
        // LiteralExpression(null), silently breaking SDF glyph fetching. Both
        // JArray (style JSON) and string[] (runtime AddLayer) inputs must
        // round-trip the array as an iterable so SymbolRenderer can pick the
        // first font name.
        [Test]
        public void Parse_LiteralStringArrayFromCSharpRoundTrips()
        {
            var raw = new[] { "Noto Sans Regular", "Arial Unicode MS" };
            var e = ExpressionParser.Parse(raw);
            var val = e.Evaluate(new EvaluationContext(0f));
            Assert.IsInstanceOf<System.Collections.IEnumerable>(val);
            var list = ((System.Collections.IEnumerable)val).Cast<object>().ToList();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("Noto Sans Regular", list[0]);
            Assert.AreEqual("Arial Unicode MS", list[1]);
        }

        [Test]
        public void Parse_LiteralStringArrayFromJsonRoundTrips()
        {
            var e = ParseExpr("[\"Open Sans Regular\", \"Arial Unicode MS\"]");
            var val = e.Evaluate(new EvaluationContext(0f));
            Assert.IsInstanceOf<System.Collections.IEnumerable>(val);
            var list = ((System.Collections.IEnumerable)val).Cast<object>().ToList();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("Open Sans Regular", list[0]);
            Assert.AreEqual("Arial Unicode MS", list[1]);
        }

        [Test]
        public void Step_PicksLargestMatchingStop()
        {
            var e = ParseExpr(
                "[\"step\", [\"zoom\"], \"small\", 5, \"medium\", 10, \"large\"]");
            Assert.AreEqual("small",  e.EvaluateString(new EvaluationContext(2f)));
            Assert.AreEqual("medium", e.EvaluateString(new EvaluationContext(5f)));
            Assert.AreEqual("medium", e.EvaluateString(new EvaluationContext(9.99f)));
            Assert.AreEqual("large",  e.EvaluateString(new EvaluationContext(10f)));
            Assert.AreEqual("large",  e.EvaluateString(new EvaluationContext(20f)));
        }

        // === Branching ===

        [Test]
        public void Match_PicksLabelArm()
        {
            var e = ParseExpr(
                "[\"match\", [\"get\", \"class\"], \"park\", \"#0f0\", \"water\", \"#00f\", \"#888\"]");
            Assert.AreEqual("#0f0",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"class\":\"park\"}"))));
            Assert.AreEqual("#00f",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"class\":\"water\"}"))));
            Assert.AreEqual("#888",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"class\":\"other\"}"))));
        }

        [Test]
        public void Case_PicksFirstTrueBranch()
        {
            var e = ParseExpr(
                "[\"case\", [\"==\", [\"get\", \"x\"], 1], \"a\"," +
                "          [\"==\", [\"get\", \"x\"], 2], \"b\"," +
                "          \"default\"]");
            Assert.AreEqual("a",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"x\":1}"))));
            Assert.AreEqual("b",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"x\":2}"))));
            Assert.AreEqual("default",
                e.EvaluateString(new EvaluationContext(0f, MakeFeature("{\"x\":99}"))));
        }

        [Test]
        public void Coalesce_ReturnsFirstNonNull()
        {
            var e = ParseExpr("[\"coalesce\", [\"get\", \"missing\"], [\"get\", \"name\"], \"default\"]");
            var f = MakeFeature("{\"name\": \"Tokyo\"}");
            Assert.AreEqual("Tokyo", e.EvaluateString(new EvaluationContext(0f, f)));
        }

        // === Comparison ===

        [TestCase("[\"==\", 1, 1]", true)]
        [TestCase("[\"==\", 1, 2]", false)]
        [TestCase("[\"!=\", 1, 2]", true)]
        [TestCase("[\"<\", 1, 2]", true)]
        [TestCase("[\">\", 2, 1]", true)]
        [TestCase("[\"<=\", 1, 1]", true)]
        [TestCase("[\">=\", 2, 2]", true)]
        public void Comparison_Numeric(string json, bool expected)
        {
            Assert.AreEqual(expected, ParseExpr(json).EvaluateBool(new EvaluationContext(0f)));
        }

        // === Logic ===

        [Test]
        public void All_RequiresEveryArgTrue()
        {
            Assert.IsTrue(ParseExpr("[\"all\", true, true]").EvaluateBool(new EvaluationContext(0f)));
            Assert.IsFalse(ParseExpr("[\"all\", true, false]").EvaluateBool(new EvaluationContext(0f)));
        }

        [Test]
        public void Any_RequiresAtLeastOneArgTrue()
        {
            Assert.IsTrue(ParseExpr("[\"any\", false, true]").EvaluateBool(new EvaluationContext(0f)));
            Assert.IsFalse(ParseExpr("[\"any\", false, false]").EvaluateBool(new EvaluationContext(0f)));
        }

        [Test]
        public void Not_InvertsArg()
        {
            Assert.IsFalse(ParseExpr("[\"!\", true]").EvaluateBool(new EvaluationContext(0f)));
            Assert.IsTrue(ParseExpr("[\"!\", false]").EvaluateBool(new EvaluationContext(0f)));
        }

        // === String ops ===

        [Test]
        public void Concat_JoinsArgs()
        {
            var e = ParseExpr("[\"concat\", \"hello, \", [\"get\", \"name\"]]");
            var f = MakeFeature("{\"name\": \"Tokyo\"}");
            Assert.AreEqual("hello, Tokyo", e.EvaluateString(new EvaluationContext(0f, f)));
        }

        [Test]
        public void Length_ReturnsStringLength()
        {
            var e = ParseExpr("[\"length\", \"hello\"]");
            Assert.AreEqual(5, e.EvaluateFloat(new EvaluationContext(0f)));
        }

        [Test]
        public void Downcase_LowersCase()
        {
            var e = ParseExpr("[\"downcase\", \"HELLO\"]");
            Assert.AreEqual("hello", e.EvaluateString(new EvaluationContext(0f)));
        }

        [Test]
        public void Upcase_RaisesCase()
        {
            var e = ParseExpr("[\"upcase\", \"hello\"]");
            Assert.AreEqual("HELLO", e.EvaluateString(new EvaluationContext(0f)));
        }

        // === Math ===

        [TestCase("[\"+\", 2, 3]", 5)]
        [TestCase("[\"-\", 5, 2]", 3)]
        [TestCase("[\"*\", 4, 5]", 20)]
        [TestCase("[\"/\", 10, 4]", 2.5f)]
        [TestCase("[\"min\", 3, 1, 2]", 1)]
        [TestCase("[\"max\", 3, 1, 2]", 3)]
        [TestCase("[\"abs\", -7]", 7)]
        public void Math_BasicOps(string json, float expected)
        {
            Assert.AreEqual(expected, ParseExpr(json).EvaluateFloat(new EvaluationContext(0f)),
                delta: 0.0001f);
        }

        // === feature-state ===

        [Test]
        public void FeatureState_ResolvesAgainstStore()
        {
            var e = ParseExpr("[\"feature-state\", \"hover\"]");
            var feature = MakeFeature("{\"name\": \"Tokyo\"}", id: 42);
            var store = new TestFeatureStateStore();
            store.Set("source-a", "layer-a", 42, "hover", true);

            var ctx = new EvaluationContext(0f, feature)
            {
                SourceId = "source-a",
                SourceLayer = "layer-a",
                FeatureStateStore = store,
            };

            Assert.IsTrue(e.EvaluateBool(ctx),
                "feature-state should pick up the value from the store");
        }

        [Test]
        public void FeatureState_ReturnsNullWhenStoreMissing()
        {
            var e = ParseExpr("[\"feature-state\", \"hover\"]");
            var feature = MakeFeature("{\"name\": \"Tokyo\"}", id: 42);
            var ctx = new EvaluationContext(0f, feature); // no FeatureStateStore
            // EvaluateBool defaults to false on null, so the feature-state
            // reference acting as a switch correctly falls through to the
            // "false" branch in case/coalesce expressions.
            Assert.IsFalse(e.EvaluateBool(ctx));
        }

        // === UsesFeatureState walker ===

        [Test]
        public void UsesFeatureState_DetectedAtNestedLevels()
        {
            var direct = ParseExpr("[\"feature-state\", \"hover\"]");
            Assert.IsTrue(direct.UsesFeatureState());

            // The walker must descend into case-branch conditions: this case
            // expression's first condition is itself a feature-state lookup,
            // so UsesFeatureState() should return true even though the top
            // node is a CaseExpression.
            var nested = ParseExpr(
                "[\"case\", [\"feature-state\", \"selected\"], \"#f00\", \"#888\"]");
            Assert.IsTrue(nested.UsesFeatureState(),
                "Walker must descend into case branches to find feature-state");

            var noState = ParseExpr("[\"interpolate\", [\"linear\"], [\"zoom\"], 0, 10, 10, 20]");
            Assert.IsFalse(noState.UsesFeatureState());
        }

        // Lightweight in-memory store. Keeps the test independent from
        // MapLibreMap's threaded store implementation.
        private sealed class TestFeatureStateStore : IFeatureStateStore
        {
            private readonly Dictionary<(string, string, long, string), object> _data = new();
            public void Set(string sourceId, string sourceLayer, long featureId, string key, object val)
                => _data[(sourceId, sourceLayer, featureId, key)] = val;
            public object GetFeatureState(string sourceId, string sourceLayer, long featureId, string key)
                => _data.TryGetValue((sourceId, sourceLayer, featureId, key), out var v) ? v : null;
        }
    }
}
