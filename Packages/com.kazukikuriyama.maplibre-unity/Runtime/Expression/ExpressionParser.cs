using System;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLibre.Unity.Expressions
{
    /// <summary>
    /// Parses MapLibre Style Spec expressions and legacy stop functions from JSON.
    /// Supports both modern expression syntax ["operator", ...] and legacy {"stops": [...]} format.
    /// </summary>
    public static class ExpressionParser
    {
        /// <summary>
        /// Parse a style property value into an Expression.
        /// Handles literals, legacy stop functions, and expression arrays.
        /// </summary>
        /// <param name="value">Raw value from Newtonsoft deserialization (string, long, double, bool, JArray, JObject)</param>
        /// <param name="isColor">If true, string values are parsed as colors</param>
        public static Expression Parse(object value, bool isColor = false)
        {
            if (value == null) return null;

            if (value is JArray arr) return ParseArray(arr, isColor);
            if (value is JObject obj) return ParseStopsFunction(obj, isColor);
            if (value is string s)
                return isColor
                    ? new LiteralExpression(StyleParser.ParseColor(s))
                    : new LiteralExpression(s);
            if (value is long l) return new LiteralExpression((float)l);
            if (value is double d) return new LiteralExpression((float)d);
            if (value is bool b) return new LiteralExpression(b);

            try { return new LiteralExpression(Convert.ToSingle(value)); }
            catch { return new LiteralExpression(value); }
        }

        /// <summary>
        /// Parse a filter expression. Handles both legacy and expression filter syntax.
        /// </summary>
        public static Expression ParseFilter(JToken token)
        {
            if (token == null) return null;
            if (token is not JArray arr || arr.Count == 0) return null;

            string op = arr[0].ToString();

            // Detect legacy vs expression filter:
            // Legacy: ["==", "property_name", value] where second arg is string
            // Expression: ["==", ["get", "prop"], value] where second arg is array
            if (IsComparisonOp(op) && arr.Count >= 3 && arr[1] is JValue)
            {
                return ParseLegacyFilter(arr);
            }

            return ParseExpression(arr, false);
        }

        private static bool IsComparisonOp(string op) =>
            op is "==" or "!=" or "<" or ">" or "<=" or ">=";

        private static Expression ParseLegacyFilter(JArray arr)
        {
            string op = arr[0].ToString();

            switch (op)
            {
                case "all":
                {
                    var conditions = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        conditions[i - 1] = ParseFilter(arr[i]);
                    return new AllExpression(conditions);
                }
                case "any":
                {
                    var conditions = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        conditions[i - 1] = ParseFilter(arr[i]);
                    return new AnyExpression(conditions);
                }
                case "none":
                {
                    var conditions = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        conditions[i - 1] = ParseFilter(arr[i]);
                    return new NotExpression(new AnyExpression(conditions));
                }
                case "!":
                    return new NotExpression(ParseFilter(arr[1]));
                case "has":
                    return arr[1].ToString() == "$type"
                        ? new LiteralExpression(true)
                        : new HasExpression(arr[1].ToString());
                case "!has":
                    return arr[1].ToString() == "$type"
                        ? new LiteralExpression(false)
                        : new NotExpression(new HasExpression(arr[1].ToString()));
                case "in":
                {
                    string property = arr[1].ToString();
                    var values = new HashSet<object>();
                    for (int i = 2; i < arr.Count; i++)
                        values.Add(JTokenToLiteral(arr[i]));

                    Expression input = property == "$type"
                        ? new GeometryTypeExpression()
                        : property == "$id"
                            ? new IdExpression()
                            : new GetExpression(property);

                    return new InExpression(input, values);
                }
                case "!in":
                {
                    string property = arr[1].ToString();
                    var values = new HashSet<object>();
                    for (int i = 2; i < arr.Count; i++)
                        values.Add(JTokenToLiteral(arr[i]));

                    Expression input = property == "$type"
                        ? new GeometryTypeExpression()
                        : property == "$id"
                            ? new IdExpression()
                            : new GetExpression(property);

                    return new NotExpression(new InExpression(input, values));
                }
                default:
                {
                    if (!IsComparisonOp(op) || arr.Count < 3) return new LiteralExpression(true);

                    string property = arr[1].ToString();
                    Expression left = property switch
                    {
                        "$type" => new GeometryTypeExpression(),
                        "$id" => new IdExpression(),
                        _ => new GetExpression(property)
                    };
                    Expression right = new LiteralExpression(JTokenToLiteral(arr[2]));

                    return op switch
                    {
                        "==" => new ComparisonExpression(ComparisonExpression.Op.Eq, left, right),
                        "!=" => new ComparisonExpression(ComparisonExpression.Op.Ne, left, right),
                        "<" => new ComparisonExpression(ComparisonExpression.Op.Lt, left, right),
                        ">" => new ComparisonExpression(ComparisonExpression.Op.Gt, left, right),
                        "<=" => new ComparisonExpression(ComparisonExpression.Op.Le, left, right),
                        ">=" => new ComparisonExpression(ComparisonExpression.Op.Ge, left, right),
                        _ => new LiteralExpression(true)
                    };
                }
            }
        }

        private static Expression ParseArray(JArray arr, bool isColor)
        {
            if (arr.Count == 0) return new LiteralExpression(null);

            // Check if first element is a string (expression operator) or a value (literal array)
            if (arr[0] is JValue firstVal && firstVal.Type == JTokenType.String)
            {
                return ParseExpression(arr, isColor);
            }

            // Literal array (e.g., [255, 0, 0] could be a color in some contexts)
            return new LiteralExpression(arr.ToString());
        }

        private static Expression ParseExpression(JArray arr, bool isColor)
        {
            string op = arr[0].ToString();

            switch (op)
            {
                case "literal":
                    return new LiteralExpression(arr.Count > 1 ? JTokenToLiteral(arr[1]) : null);

                case "zoom":
                    return new ZoomExpression();

                case "get":
                    return new GetExpression(arr[1].ToString());

                case "has":
                    return new HasExpression(arr[1].ToString());

                case "geometry-type":
                    return new GeometryTypeExpression();

                case "heatmap-density":
                    return new HeatmapDensityExpression();

                case "line-progress":
                    return new LineProgressExpression();

                case "feature-state":
                    return new FeatureStateExpression(arr[1].ToString());

                case "id":
                    return new IdExpression();

                case "interpolate":
                case "interpolate-hcl":
                case "interpolate-lab":
                    return ParseInterpolateExpression(arr, isColor, op);

                case "step":
                    return ParseStepExpression(arr, isColor);

                case "match":
                    return ParseMatchExpression(arr, isColor);

                case "case":
                    return ParseCaseExpression(arr, isColor);

                case "coalesce":
                {
                    var args = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        args[i - 1] = ParseTokenExpression(arr[i], isColor);
                    return new CoalesceExpression(args);
                }

                case "all":
                {
                    var args = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        args[i - 1] = ParseTokenExpression(arr[i], false);
                    return new AllExpression(args);
                }

                case "any":
                {
                    var args = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        args[i - 1] = ParseTokenExpression(arr[i], false);
                    return new AnyExpression(args);
                }

                case "!":
                    return new NotExpression(ParseTokenExpression(arr[1], false));

                case "==":
                case "!=":
                case "<":
                case ">":
                case "<=":
                case ">=":
                {
                    var left = ParseTokenExpression(arr[1], false);
                    var right = ParseTokenExpression(arr[2], false);
                    var compOp = op switch
                    {
                        "==" => ComparisonExpression.Op.Eq,
                        "!=" => ComparisonExpression.Op.Ne,
                        "<" => ComparisonExpression.Op.Lt,
                        ">" => ComparisonExpression.Op.Gt,
                        "<=" => ComparisonExpression.Op.Le,
                        ">=" => ComparisonExpression.Op.Ge,
                        _ => ComparisonExpression.Op.Eq
                    };
                    return new ComparisonExpression(compOp, left, right);
                }

                case "in":
                {
                    var needle = ParseTokenExpression(arr[1], false);
                    var values = new HashSet<object>();
                    for (int i = 2; i < arr.Count; i++)
                        values.Add(JTokenToLiteral(arr[i]));
                    return new InExpression(needle, values);
                }

                case "concat":
                {
                    var args = new Expression[arr.Count - 1];
                    for (int i = 1; i < arr.Count; i++)
                        args[i - 1] = ParseTokenExpression(arr[i], false);
                    return new ConcatExpression(args);
                }

                case "downcase":
                    return new DowncaseExpression(ParseTokenExpression(arr[1], false));

                case "upcase":
                    return new UpcaseExpression(ParseTokenExpression(arr[1], false));

                case "to-string":
                    return new ToStringExpression(ParseTokenExpression(arr[1], false));

                case "to-number":
                    return new ToNumberExpression(ParseTokenExpression(arr[1], false));

                case "length":
                    return new LengthExpression(ParseTokenExpression(arr[1], false));

                case "+":
                case "-":
                case "*":
                case "/":
                case "%":
                case "min":
                case "max":
                case "abs":
                case "ceil":
                case "floor":
                case "round":
                case "sqrt":
                case "ln":
                case "log2":
                case "log10":
                case "^":
                    return ParseMathExpression(op, arr);

                case "format":
                    return ParseFormatExpression(arr);

                default:
                    Debug.LogWarning($"[MapLibre] Unknown expression operator: {op}");
                    return new LiteralExpression(null);
            }
        }

        private static Expression ParseInterpolateExpression(JArray arr, bool isColor, string op)
        {
            // ["interpolate", interpolation, input, stop1, output1, stop2, output2, ...]
            if (arr.Count < 5) return new LiteralExpression(null);

            var interpType = InterpolateExpression.InterpolationType.Linear;
            float interpBase = 1f;

            if (arr[1] is JArray interpArr && interpArr.Count > 0)
            {
                string type = interpArr[0].ToString();
                if (type == "exponential" && interpArr.Count > 1)
                {
                    interpType = InterpolateExpression.InterpolationType.Exponential;
                    interpBase = interpArr[1].ToObject<float>();
                }
            }

            // The operator name selects the color-interpolation space; this is
            // orthogonal to the linear/exponential curve type chosen above.
            var colorSpace = op switch
            {
                "interpolate-hcl" => InterpolateExpression.ColorSpace.Hcl,
                "interpolate-lab" => InterpolateExpression.ColorSpace.Lab,
                _ => InterpolateExpression.ColorSpace.Rgb,
            };

            var input = ParseTokenExpression(arr[2], false);

            var stops = new List<(float, Expression)>();
            for (int i = 3; i + 1 < arr.Count; i += 2)
            {
                float stopVal = arr[i].ToObject<float>();
                var output = ParseTokenExpression(arr[i + 1], isColor);
                stops.Add((stopVal, output));
            }

            return new InterpolateExpression(interpType, interpBase, colorSpace, input, stops.ToArray());
        }

        private static Expression ParseStepExpression(JArray arr, bool isColor)
        {
            // ["step", input, default, stop1, output1, stop2, output2, ...]
            if (arr.Count < 4) return new LiteralExpression(null);

            var input = ParseTokenExpression(arr[1], false);
            var defaultExpr = ParseTokenExpression(arr[2], isColor);

            var stops = new List<(float, Expression)>();
            for (int i = 3; i + 1 < arr.Count; i += 2)
            {
                float stopVal = arr[i].ToObject<float>();
                var output = ParseTokenExpression(arr[i + 1], isColor);
                stops.Add((stopVal, output));
            }

            return new StepExpression(input, defaultExpr, stops.ToArray());
        }

        private static Expression ParseMatchExpression(JArray arr, bool isColor)
        {
            // ["match", input, label1, output1, label2, output2, ..., default]
            if (arr.Count < 4) return new LiteralExpression(null);

            var input = ParseTokenExpression(arr[1], false);
            var defaultExpr = ParseTokenExpression(arr[arr.Count - 1], isColor);

            var cases = new List<(object[], Expression)>();
            for (int i = 2; i + 1 < arr.Count - 1; i += 2)
            {
                object[] labels;
                if (arr[i] is JArray labelArr)
                {
                    labels = new object[labelArr.Count];
                    for (int j = 0; j < labelArr.Count; j++)
                        labels[j] = JTokenToLiteral(labelArr[j]);
                }
                else
                {
                    labels = new[] { JTokenToLiteral(arr[i]) };
                }

                var output = ParseTokenExpression(arr[i + 1], isColor);
                cases.Add((labels, output));
            }

            return new MatchExpression(input, cases.ToArray(), defaultExpr);
        }

        private static Expression ParseCaseExpression(JArray arr, bool isColor)
        {
            // ["case", condition1, output1, condition2, output2, ..., default]
            if (arr.Count < 3) return new LiteralExpression(null);

            var defaultExpr = ParseTokenExpression(arr[arr.Count - 1], isColor);

            var branches = new List<(Expression, Expression)>();
            for (int i = 1; i + 1 < arr.Count - 1; i += 2)
            {
                var condition = ParseTokenExpression(arr[i], false);
                var output = ParseTokenExpression(arr[i + 1], isColor);
                branches.Add((condition, output));
            }

            return new CaseExpression(branches.ToArray(), defaultExpr);
        }

        private static Expression ParseMathExpression(string op, JArray arr)
        {
            var mathOp = op switch
            {
                "+" => MathExpression.MathOp.Add,
                "-" => MathExpression.MathOp.Sub,
                "*" => MathExpression.MathOp.Mul,
                "/" => MathExpression.MathOp.Div,
                "%" => MathExpression.MathOp.Mod,
                "min" => MathExpression.MathOp.Min,
                "max" => MathExpression.MathOp.Max,
                "abs" => MathExpression.MathOp.Abs,
                "ceil" => MathExpression.MathOp.Ceil,
                "floor" => MathExpression.MathOp.Floor,
                "round" => MathExpression.MathOp.Round,
                "sqrt" => MathExpression.MathOp.Sqrt,
                "ln" => MathExpression.MathOp.Ln,
                "log2" => MathExpression.MathOp.Log2,
                "log10" => MathExpression.MathOp.Log10,
                "^" => MathExpression.MathOp.Pow,
                _ => MathExpression.MathOp.Add
            };

            var args = new Expression[arr.Count - 1];
            for (int i = 1; i < arr.Count; i++)
                args[i - 1] = ParseTokenExpression(arr[i], false);

            return new MathExpression(mathOp, args);
        }

        /// <summary>
        /// Parse "format" expression. Simplified: concatenates text sections.
        /// ["format", text1, options1, text2, options2, ...]
        /// </summary>
        private static Expression ParseFormatExpression(JArray arr)
        {
            var parts = new List<Expression>();
            for (int i = 1; i < arr.Count; i++)
            {
                // Skip option objects (JObject)
                if (arr[i] is JObject) continue;
                parts.Add(ParseTokenExpression(arr[i], false));
            }

            return parts.Count == 1 ? parts[0] : new ConcatExpression(parts.ToArray());
        }

        /// <summary>
        /// Parse legacy stop function: {"stops": [[z1, v1], [z2, v2]], "base": 1.2}
        /// </summary>
        private static Expression ParseStopsFunction(JObject obj, bool isColor)
        {
            if (obj["stops"] is not JArray stopsArr) return new LiteralExpression(null);

            float baseVal = obj["base"]?.ToObject<float>() ?? 1f;

            // Check if it's a property function (data-driven)
            string property = obj["property"]?.ToString();
            Expression input;
            if (!string.IsNullOrEmpty(property))
            {
                input = new GetExpression(property);
            }
            else
            {
                input = new ZoomExpression();
            }

            var stops = new List<(float, Expression)>();
            foreach (var stop in stopsArr)
            {
                if (stop is JArray pair && pair.Count >= 2)
                {
                    float stopVal = pair[0].ToObject<float>();
                    var output = ParseTokenLiteral(pair[1], isColor);
                    stops.Add((stopVal, output));
                }
            }

            if (stops.Count == 0) return new LiteralExpression(null);

            // Determine interpolation type
            string type = obj["type"]?.ToString();
            if (type == "identity" && !string.IsNullOrEmpty(property))
            {
                return new GetExpression(property);
            }

            if (type == "categorical")
            {
                var cases = new (object[], Expression)[stops.Count];
                for (int i = 0; i < stops.Count; i++)
                    cases[i] = (new object[] { stops[i].Item1 }, stops[i].Item2);
                return new MatchExpression(input, cases,
                    stops.Count > 0 ? stops[0].Item2 : new LiteralExpression(null));
            }

            // Default: interpolate (exponential or linear)
            var interpType = Math.Abs(baseVal - 1f) > 0.001f
                ? InterpolateExpression.InterpolationType.Exponential
                : InterpolateExpression.InterpolationType.Linear;

            return new InterpolateExpression(interpType, baseVal, input, stops.ToArray());
        }

        /// <summary>
        /// Parse a JToken that could be a literal or a sub-expression.
        /// </summary>
        private static Expression ParseTokenExpression(JToken token, bool isColor)
        {
            if (token is JArray arr) return ParseExpression(arr, isColor);
            return ParseTokenLiteral(token, isColor);
        }

        /// <summary>
        /// Parse a JToken as a literal value.
        /// </summary>
        private static Expression ParseTokenLiteral(JToken token, bool isColor)
        {
            if (token == null) return new LiteralExpression(null);

            if (token is JValue jval)
            {
                return jval.Type switch
                {
                    JTokenType.String => isColor
                        ? new LiteralExpression(StyleParser.ParseColor(jval.ToString()))
                        : new LiteralExpression(jval.ToString()),
                    JTokenType.Integer => new LiteralExpression((float)jval.ToObject<long>()),
                    JTokenType.Float => new LiteralExpression(jval.ToObject<float>()),
                    JTokenType.Boolean => new LiteralExpression(jval.ToObject<bool>()),
                    JTokenType.Null => new LiteralExpression(null),
                    _ => new LiteralExpression(jval.ToString())
                };
            }

            if (token is JArray arr) return ParseExpression(arr, isColor);
            return new LiteralExpression(token.ToString());
        }

        /// <summary>
        /// Convert a JToken to a primitive value for use in match labels, comparisons, etc.
        /// </summary>
        private static object JTokenToLiteral(JToken token)
        {
            if (token is JValue jval)
            {
                return jval.Type switch
                {
                    JTokenType.String => jval.ToString(),
                    JTokenType.Integer => (double)jval.ToObject<long>(),
                    JTokenType.Float => (double)jval.ToObject<double>(),
                    JTokenType.Boolean => jval.ToObject<bool>(),
                    _ => jval.ToString()
                };
            }
            return token.ToString();
        }
    }
}
