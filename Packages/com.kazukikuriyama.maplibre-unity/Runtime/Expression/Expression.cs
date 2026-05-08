using System;
using System.Collections.Generic;
using MapLibre.Unity.Style;
using MapLibre.Unity.VectorTile;
using UnityEngine;

namespace MapLibre.Unity.Expressions
{
    public abstract class Expression
    {
        public abstract object Evaluate(EvaluationContext ctx);
        public bool IsConstant { get; protected set; }

        /// <summary>
        /// True if this expression -- or any nested sub-expression -- reads
        /// runtime feature state via <c>["feature-state", ...]</c>. Used by
        /// MapLibreMap to skip costly tile rebuilds when no layer would
        /// actually observe a SetFeatureState call. The default returns false;
        /// composite expressions override and OR-fold their children.
        /// </summary>
        public virtual bool UsesFeatureState() => false;

        public float EvaluateFloat(EvaluationContext ctx, float defaultVal = 0f)
        {
            var result = Evaluate(ctx);
            return result switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                long l => l,
                _ => defaultVal
            };
        }

        public Color EvaluateColor(EvaluationContext ctx, Color defaultVal = default)
        {
            var result = Evaluate(ctx);
            if (result is Color c) return c;
            if (result is string s) return StyleParser.ParseColor(s);
            return defaultVal;
        }

        public string EvaluateString(EvaluationContext ctx, string defaultVal = "")
        {
            var result = Evaluate(ctx);
            return result?.ToString() ?? defaultVal;
        }

        public bool EvaluateBool(EvaluationContext ctx, bool defaultVal = false)
        {
            var result = Evaluate(ctx);
            if (result is bool b) return b;
            return defaultVal;
        }

        protected static bool IsNumeric(object v) => v is float or double or int or long;

        protected static double ToDouble(object v) => v switch
        {
            float f => f,
            double d => d,
            int i => i,
            long l => l,
            _ => 0.0
        };
    }

    public class LiteralExpression : Expression
    {
        private readonly object _value;
        public LiteralExpression(object value) { _value = value; IsConstant = true; }
        public override object Evaluate(EvaluationContext ctx) => _value;
    }

    public class ZoomExpression : Expression
    {
        public ZoomExpression() { IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) => ctx.Zoom;
    }

    public class GetExpression : Expression
    {
        private readonly string _property;
        public GetExpression(string property) { _property = property; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (ctx.Feature == null) return null;
            var val = ctx.Feature.GetProperty(_property);
            if (val == null) return null;
            if (val.StringValue != null) return val.StringValue;
            if (val.DoubleValue.HasValue) return val.DoubleValue.Value;
            if (val.FloatValue.HasValue) return (double)val.FloatValue.Value;
            if (val.IntValue.HasValue) return (double)val.IntValue.Value;
            if (val.UIntValue.HasValue) return (double)val.UIntValue.Value;
            if (val.SIntValue.HasValue) return (double)val.SIntValue.Value;
            if (val.BoolValue.HasValue) return val.BoolValue.Value;
            return val.ToString();
        }
    }

    public class HasExpression : Expression
    {
        private readonly string _property;
        public HasExpression(string property) { _property = property; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (ctx.Feature == null) return false;
            return ctx.Feature.GetProperty(_property) != null;
        }
    }

    public class GeometryTypeExpression : Expression
    {
        public GeometryTypeExpression() { IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (ctx.Feature == null) return "Unknown";
            return ctx.Feature.Type switch
            {
                GeometryType.Point => "Point",
                GeometryType.LineString => "LineString",
                GeometryType.Polygon => "Polygon",
                _ => "Unknown"
            };
        }
    }

    public class IdExpression : Expression
    {
        public IdExpression() { IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (ctx.Feature == null) return null;
            return (double)ctx.Feature.Id;
        }
    }

    public class HeatmapDensityExpression : Expression
    {
        public HeatmapDensityExpression() { IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) => ctx.HeatmapDensity;
    }

    /// <summary>
    /// <c>["line-progress"]</c> -- normalised distance (0..1) along the current
    /// line feature. Only meaningful as the input of the line-gradient
    /// interpolate expression: the renderer evaluates it at 256 sample points
    /// while baking the gradient ramp texture.
    /// </summary>
    public class LineProgressExpression : Expression
    {
        public LineProgressExpression() { IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) => ctx.LineProgress;
    }

    /// <summary>
    /// <c>["feature-state", "key"]</c> -- looks up the runtime state value attached
    /// to the current feature. Returns null when no state has been registered for
    /// this feature/key, which lets downstream <c>coalesce</c> or <c>case</c> branches
    /// supply a default.
    /// </summary>
    public class FeatureStateExpression : Expression
    {
        private readonly string _key;
        public FeatureStateExpression(string key) { _key = key; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (ctx.FeatureStateStore == null || ctx.Feature == null) return null;
            return ctx.FeatureStateStore.GetFeatureState(
                ctx.SourceId, ctx.SourceLayer, (long)ctx.Feature.Id, _key);
        }

        public override bool UsesFeatureState() => true;
    }

    public class InterpolateExpression : Expression
    {
        public enum InterpolationType { Linear, Exponential }

        /// <summary>
        /// Color space used when interpolating <see cref="Color"/> outputs.
        /// Driven by the operator at parse time:
        /// <c>interpolate</c> → <see cref="Rgb"/>,
        /// <c>interpolate-hcl</c> → <see cref="Hcl"/>,
        /// <c>interpolate-lab</c> → <see cref="Lab"/>.
        /// Numeric outputs always interpolate linearly regardless of this setting.
        /// </summary>
        public enum ColorSpace { Rgb, Hcl, Lab }

        private readonly InterpolationType _type;
        private readonly float _base;
        private readonly ColorSpace _colorSpace;
        private readonly Expression _input;
        private readonly (float stop, Expression output)[] _stops;

        public InterpolateExpression(InterpolationType type, float @base, Expression input,
            (float, Expression)[] stops)
            : this(type, @base, ColorSpace.Rgb, input, stops) { }

        public InterpolateExpression(InterpolationType type, float @base, ColorSpace colorSpace,
            Expression input, (float, Expression)[] stops)
        {
            _type = type;
            _base = @base;
            _colorSpace = colorSpace;
            _input = input;
            _stops = stops;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            float inputVal = _input.EvaluateFloat(ctx);

            if (_stops.Length == 0) return null;
            if (inputVal <= _stops[0].stop) return _stops[0].output.Evaluate(ctx);
            if (inputVal >= _stops[_stops.Length - 1].stop)
                return _stops[_stops.Length - 1].output.Evaluate(ctx);

            for (int i = 0; i < _stops.Length - 1; i++)
            {
                if (inputVal >= _stops[i].stop && inputVal <= _stops[i + 1].stop)
                {
                    float range = _stops[i + 1].stop - _stops[i].stop;
                    float t;
                    if (_type == InterpolationType.Exponential && Math.Abs(_base - 1f) > 0.001f)
                    {
                        t = (float)((Math.Pow(_base, inputVal - _stops[i].stop) - 1.0) /
                                    (Math.Pow(_base, range) - 1.0));
                    }
                    else
                    {
                        t = (inputVal - _stops[i].stop) / range;
                    }

                    var a = _stops[i].output.Evaluate(ctx);
                    var b = _stops[i + 1].output.Evaluate(ctx);
                    return InterpolateValues(a, b, t);
                }
            }

            return _stops[_stops.Length - 1].output.Evaluate(ctx);
        }

        private object InterpolateValues(object a, object b, float t)
        {
            if (a is Color ca && b is Color cb)
            {
                return _colorSpace switch
                {
                    ColorSpace.Hcl => ColorSpaceConversion.LerpHcl(ca, cb, t),
                    ColorSpace.Lab => ColorSpaceConversion.LerpLab(ca, cb, t),
                    _ => Color.Lerp(ca, cb, t),
                };
            }
            if (IsNumeric(a) && IsNumeric(b))
            {
                double va = ToDouble(a);
                double vb = ToDouble(b);
                return (float)(va + (vb - va) * t);
            }
            return t < 0.5f ? a : b;
        }

        public override bool UsesFeatureState()
        {
            if (_input != null && _input.UsesFeatureState()) return true;
            foreach (var (_, output) in _stops)
                if (output != null && output.UsesFeatureState()) return true;
            return false;
        }
    }

    public class StepExpression : Expression
    {
        private readonly Expression _input;
        private readonly Expression _default;
        private readonly (float stop, Expression output)[] _stops;

        public StepExpression(Expression input, Expression @default, (float, Expression)[] stops)
        {
            _input = input;
            _default = @default;
            _stops = stops;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            float inputVal = _input.EvaluateFloat(ctx);
            object result = _default.Evaluate(ctx);
            for (int i = 0; i < _stops.Length; i++)
            {
                if (inputVal >= _stops[i].stop)
                    result = _stops[i].output.Evaluate(ctx);
                else
                    break;
            }
            return result;
        }

        public override bool UsesFeatureState()
        {
            if (_input != null && _input.UsesFeatureState()) return true;
            if (_default != null && _default.UsesFeatureState()) return true;
            foreach (var (_, output) in _stops)
                if (output != null && output.UsesFeatureState()) return true;
            return false;
        }
    }

    public class MatchExpression : Expression
    {
        private readonly Expression _input;
        private readonly (object[] labels, Expression output)[] _cases;
        private readonly Expression _default;

        public MatchExpression(Expression input, (object[], Expression)[] cases, Expression @default)
        {
            _input = input;
            _cases = cases;
            _default = @default;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            var inputVal = _input.Evaluate(ctx);
            foreach (var (labels, output) in _cases)
            {
                foreach (var label in labels)
                {
                    if (ValuesEqual(inputVal, label))
                        return output.Evaluate(ctx);
                }
            }
            return _default.Evaluate(ctx);
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Equals(b)) return true;
            if (IsNumeric(a) && IsNumeric(b))
                return Math.Abs(ToDouble(a) - ToDouble(b)) < 1e-9;
            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        public override bool UsesFeatureState()
        {
            if (_input != null && _input.UsesFeatureState()) return true;
            if (_default != null && _default.UsesFeatureState()) return true;
            foreach (var (_, output) in _cases)
                if (output != null && output.UsesFeatureState()) return true;
            return false;
        }
    }

    public class CaseExpression : Expression
    {
        private readonly (Expression condition, Expression output)[] _branches;
        private readonly Expression _default;

        public CaseExpression((Expression, Expression)[] branches, Expression @default)
        {
            _branches = branches;
            _default = @default;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            foreach (var (condition, output) in _branches)
            {
                if (condition.EvaluateBool(ctx))
                    return output.Evaluate(ctx);
            }
            return _default.Evaluate(ctx);
        }

        public override bool UsesFeatureState()
        {
            if (_default != null && _default.UsesFeatureState()) return true;
            foreach (var (cond, output) in _branches)
            {
                if (cond != null && cond.UsesFeatureState()) return true;
                if (output != null && output.UsesFeatureState()) return true;
            }
            return false;
        }
    }

    public class CoalesceExpression : Expression
    {
        private readonly Expression[] _args;
        public CoalesceExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            foreach (var arg in _args)
            {
                var val = arg.Evaluate(ctx);
                if (val != null) return val;
            }
            return null;
        }

        public override bool UsesFeatureState()
        {
            foreach (var a in _args)
                if (a != null && a.UsesFeatureState()) return true;
            return false;
        }
    }

    public class ComparisonExpression : Expression
    {
        public enum Op { Eq, Ne, Lt, Gt, Le, Ge }

        private readonly Op _op;
        private readonly Expression _left;
        private readonly Expression _right;

        public ComparisonExpression(Op op, Expression left, Expression right)
        {
            _op = op; _left = left; _right = right;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            var left = _left.Evaluate(ctx);
            var right = _right.Evaluate(ctx);

            if (left == null || right == null)
            {
                return _op switch
                {
                    Op.Eq => left == null && right == null,
                    Op.Ne => !(left == null && right == null),
                    _ => (object)false
                };
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                double l = ToDouble(left);
                double r = ToDouble(right);
                return _op switch
                {
                    Op.Eq => Math.Abs(l - r) < 1e-9,
                    Op.Ne => Math.Abs(l - r) >= 1e-9,
                    Op.Lt => l < r,
                    Op.Gt => l > r,
                    Op.Le => l <= r,
                    Op.Ge => l >= r,
                    _ => (object)false
                };
            }

            string ls = left.ToString();
            string rs = right.ToString();
            int cmp = string.Compare(ls, rs, StringComparison.Ordinal);
            return _op switch
            {
                Op.Eq => ls == rs,
                Op.Ne => ls != rs,
                Op.Lt => cmp < 0,
                Op.Gt => cmp > 0,
                Op.Le => cmp <= 0,
                Op.Ge => cmp >= 0,
                _ => (object)false
            };
        }

        public override bool UsesFeatureState()
            => (_left != null && _left.UsesFeatureState())
            || (_right != null && _right.UsesFeatureState());
    }

    public class AllExpression : Expression
    {
        private readonly Expression[] _args;
        public AllExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            foreach (var arg in _args)
            {
                if (!arg.EvaluateBool(ctx)) return false;
            }
            return true;
        }

        public override bool UsesFeatureState()
        {
            foreach (var a in _args)
                if (a != null && a.UsesFeatureState()) return true;
            return false;
        }
    }

    public class AnyExpression : Expression
    {
        private readonly Expression[] _args;
        public AnyExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            foreach (var arg in _args)
            {
                if (arg.EvaluateBool(ctx)) return true;
            }
            return false;
        }

        public override bool UsesFeatureState()
        {
            foreach (var a in _args)
                if (a != null && a.UsesFeatureState()) return true;
            return false;
        }
    }

    public class NotExpression : Expression
    {
        private readonly Expression _arg;
        public NotExpression(Expression arg) { _arg = arg; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            return !_arg.EvaluateBool(ctx);
        }

        public override bool UsesFeatureState()
            => _arg != null && _arg.UsesFeatureState();
    }

    public class InExpression : Expression
    {
        private readonly Expression _needle;
        private readonly HashSet<object> _haystack;

        public InExpression(Expression needle, HashSet<object> haystack)
        {
            _needle = needle;
            _haystack = haystack;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            var val = _needle.Evaluate(ctx);
            if (val == null) return false;
            if (_haystack.Contains(val)) return true;
            // Try string comparison
            string s = val.ToString();
            foreach (var item in _haystack)
            {
                if (string.Equals(s, item?.ToString(), StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public override bool UsesFeatureState()
            => _needle != null && _needle.UsesFeatureState();
    }

    public class ConcatExpression : Expression
    {
        private readonly Expression[] _args;
        public ConcatExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var arg in _args)
            {
                var val = arg.Evaluate(ctx);
                if (val != null) sb.Append(val);
            }
            return sb.ToString();
        }

        public override bool UsesFeatureState()
        {
            foreach (var a in _args)
                if (a != null && a.UsesFeatureState()) return true;
            return false;
        }
    }

    public class MathExpression : Expression
    {
        public enum MathOp { Add, Sub, Mul, Div, Mod, Min, Max, Abs, Ceil, Floor, Round, Sqrt, Ln, Log2, Log10, Pow }

        private readonly MathOp _op;
        private readonly Expression[] _args;

        public MathExpression(MathOp op, Expression[] args)
        {
            _op = op; _args = args;
            IsConstant = false;
        }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (_args.Length == 0) return 0f;

            double a = _args[0].EvaluateFloat(ctx);

            return _op switch
            {
                MathOp.Abs => (float)Math.Abs(a),
                MathOp.Ceil => (float)Math.Ceiling(a),
                MathOp.Floor => (float)Math.Floor(a),
                MathOp.Round => (float)Math.Round(a),
                MathOp.Sqrt => (float)Math.Sqrt(a),
                MathOp.Ln => (float)Math.Log(a),
                MathOp.Log2 => (float)Math.Log(a, 2),
                MathOp.Log10 => (float)Math.Log10(a),
                _ => EvaluateBinary(a, ctx)
            };
        }

        private float EvaluateBinary(double a, EvaluationContext ctx)
        {
            if (_args.Length < 2) return (float)a;
            double b = _args[1].EvaluateFloat(ctx);

            return _op switch
            {
                MathOp.Add => (float)(_args.Length == 2 ? a + b : SumAll(ctx)),
                MathOp.Sub => (float)(a - b),
                MathOp.Mul => (float)(_args.Length == 2 ? a * b : ProductAll(ctx)),
                MathOp.Div => b != 0 ? (float)(a / b) : 0f,
                MathOp.Mod => b != 0 ? (float)(a % b) : 0f,
                MathOp.Min => (float)MinAll(ctx),
                MathOp.Max => (float)MaxAll(ctx),
                MathOp.Pow => (float)Math.Pow(a, b),
                _ => 0f
            };
        }

        private double SumAll(EvaluationContext ctx)
        {
            double sum = 0;
            foreach (var arg in _args) sum += arg.EvaluateFloat(ctx);
            return sum;
        }

        private double ProductAll(EvaluationContext ctx)
        {
            double prod = 1;
            foreach (var arg in _args) prod *= arg.EvaluateFloat(ctx);
            return prod;
        }

        private double MinAll(EvaluationContext ctx)
        {
            double min = double.MaxValue;
            foreach (var arg in _args) min = Math.Min(min, arg.EvaluateFloat(ctx));
            return min;
        }

        private double MaxAll(EvaluationContext ctx)
        {
            double max = double.MinValue;
            foreach (var arg in _args) max = Math.Max(max, arg.EvaluateFloat(ctx));
            return max;
        }

        public override bool UsesFeatureState()
        {
            foreach (var a in _args)
                if (a != null && a.UsesFeatureState()) return true;
            return false;
        }
    }

    public class ToStringExpression : Expression
    {
        private readonly Expression _arg;
        public ToStringExpression(Expression arg) { _arg = arg; IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) => _arg.Evaluate(ctx)?.ToString() ?? "";
        public override bool UsesFeatureState() => _arg != null && _arg.UsesFeatureState();
    }

    public class ToNumberExpression : Expression
    {
        private readonly Expression _arg;
        public ToNumberExpression(Expression arg) { _arg = arg; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            var val = _arg.Evaluate(ctx);
            if (val == null) return 0f;
            if (IsNumeric(val)) return (float)ToDouble(val);
            if (double.TryParse(val.ToString(), out double d)) return (float)d;
            return 0f;
        }

        public override bool UsesFeatureState() => _arg != null && _arg.UsesFeatureState();
    }

    /// <summary>
    /// MapLibre Style Spec type-assertion: <c>["boolean", value, ...fallbacks]</c>.
    /// Returns the first argument that evaluates to a boolean; otherwise
    /// returns false. Common use: <c>["boolean", ["feature-state", "hover"], false]</c>
    /// to coerce missing feature-state to a definite false.
    /// </summary>
    public class BooleanAssertExpression : Expression
    {
        private readonly Expression[] _args;
        public BooleanAssertExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (_args == null) return false;
            for (int i = 0; i < _args.Length; i++)
            {
                var v = _args[i]?.Evaluate(ctx);
                if (v is bool b) return b;
            }
            return false;
        }

        public override bool UsesFeatureState()
        {
            if (_args == null) return false;
            for (int i = 0; i < _args.Length; i++)
                if (_args[i] != null && _args[i].UsesFeatureState()) return true;
            return false;
        }
    }

    /// <summary>
    /// MapLibre Style Spec type-assertion: <c>["string", value, ...fallbacks]</c>.
    /// </summary>
    public class StringAssertExpression : Expression
    {
        private readonly Expression[] _args;
        public StringAssertExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (_args == null) return "";
            for (int i = 0; i < _args.Length; i++)
            {
                var v = _args[i]?.Evaluate(ctx);
                if (v is string s) return s;
            }
            return "";
        }

        public override bool UsesFeatureState()
        {
            if (_args == null) return false;
            for (int i = 0; i < _args.Length; i++)
                if (_args[i] != null && _args[i].UsesFeatureState()) return true;
            return false;
        }
    }

    /// <summary>
    /// MapLibre Style Spec type-assertion: <c>["number", value, ...fallbacks]</c>.
    /// </summary>
    public class NumberAssertExpression : Expression
    {
        private readonly Expression[] _args;
        public NumberAssertExpression(Expression[] args) { _args = args; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            if (_args == null) return 0f;
            for (int i = 0; i < _args.Length; i++)
            {
                var v = _args[i]?.Evaluate(ctx);
                if (IsNumeric(v)) return (float)ToDouble(v);
            }
            return 0f;
        }

        public override bool UsesFeatureState()
        {
            if (_args == null) return false;
            for (int i = 0; i < _args.Length; i++)
                if (_args[i] != null && _args[i].UsesFeatureState()) return true;
            return false;
        }
    }

    public class DowncaseExpression : Expression
    {
        private readonly Expression _arg;
        public DowncaseExpression(Expression arg) { _arg = arg; IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) =>
            _arg.Evaluate(ctx)?.ToString()?.ToLowerInvariant() ?? "";
        public override bool UsesFeatureState() => _arg != null && _arg.UsesFeatureState();
    }

    public class UpcaseExpression : Expression
    {
        private readonly Expression _arg;
        public UpcaseExpression(Expression arg) { _arg = arg; IsConstant = false; }
        public override object Evaluate(EvaluationContext ctx) =>
            _arg.Evaluate(ctx)?.ToString()?.ToUpperInvariant() ?? "";
        public override bool UsesFeatureState() => _arg != null && _arg.UsesFeatureState();
    }

    public class LengthExpression : Expression
    {
        private readonly Expression _arg;
        public LengthExpression(Expression arg) { _arg = arg; IsConstant = false; }

        public override object Evaluate(EvaluationContext ctx)
        {
            var val = _arg.Evaluate(ctx);
            if (val is string s) return (float)s.Length;
            return 0f;
        }

        public override bool UsesFeatureState() => _arg != null && _arg.UsesFeatureState();
    }
}
