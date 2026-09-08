using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Mosaic.UI
{
    /// <summary>
    /// Turns an arbitrary store member value into JSON-safe display text, and reads a member
    /// through <see cref="StoreReflectionCache.MemberEntry"/> without ever throwing.
    /// </summary>
    internal static class InspectionValueFormatter
    {
        /// <summary>Maximum items rendered inside a collection value.</summary>
        internal const int MaxCollectionItems = 20;

        /// <summary>Maximum length of the fallback object form.</summary>
        internal const int MaxFallbackLength = 200;

        /// <summary>
        /// Formats a value per the documented rule table. A collection renders as
        /// <c>{count: N, items: [first 20]}</c>, and an item inside it uses the scalar rules only,
        /// so a nested collection falls through to the object form.
        /// </summary>
        internal static string Format(object value)
        {
            if (value == null)
                return "null";

            if (TryFormatScalar(value, out var scalar))
                return scalar;

            if (value is IEnumerable enumerable)
                return FormatCollection(enumerable);

            return FormatFallback(value);
        }

        /// <summary>
        /// Renders a type name in friendly form: <c>List&lt;Int32&gt;</c> instead of <c>List`1</c>.
        /// Handles nested generics and arrays.
        /// </summary>
        internal static string FriendlyTypeName(Type t)
        {
            if (t == null)
                return "null";

            if (t.IsArray)
            {
                var rank = t.GetArrayRank();
                var commas = rank > 1 ? new string(',', rank - 1) : string.Empty;
                return FriendlyTypeName(t.GetElementType()) + "[" + commas + "]";
            }

            if (!t.IsGenericType)
                return t.Name;

            var name = t.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);

            var args = t.GetGenericArguments();
            var sb = new StringBuilder(name);
            sb.Append('<');
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FriendlyTypeName(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        /// <summary>
        /// Reads one cached member off the target and fills a <see cref="MosaicInspector.StoreValue"/>.
        /// A throwing getter yields <c>&lt;error: ExceptionTypeName&gt;</c> and the declared type,
        /// and never propagates.
        /// </summary>
        internal static MosaicInspector.StoreValue Read(object target, StoreReflectionCache.MemberEntry entry)
        {
            var result = new MosaicInspector.StoreValue();
            if (entry == null)
                return result;

            result.name = entry.Name;

            object raw;
            try
            {
                raw = entry.Member switch
                {
                    PropertyInfo pi => pi.GetValue(target),
                    FieldInfo fi => fi.GetValue(target),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                // A property getter surfaces as TargetInvocationException — report the real cause.
                while (ex is TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                result.type = FriendlyTypeName(entry.DeclaredType);
                result.value = "<error: " + ex.GetType().Name + ">";
                return result;
            }

            result.type = raw == null
                ? FriendlyTypeName(entry.DeclaredType)
                : FriendlyTypeName(raw.GetType());
            result.value = Format(raw);
            return result;
        }

        // ── Rules ─────────────────────────────────────────────────────────────

        /// <summary>Every non-collection rule. Returns false when no scalar rule matches.</summary>
        private static bool TryFormatScalar(object value, out string text)
        {
            var inv = CultureInfo.InvariantCulture;

            switch (value)
            {
                case bool b: text = b ? "true" : "false"; return true;
                case string s: text = s; return true;
                case char c: text = c.ToString(); return true;
                case sbyte n: text = n.ToString(inv); return true;
                case byte n: text = n.ToString(inv); return true;
                case short n: text = n.ToString(inv); return true;
                case ushort n: text = n.ToString(inv); return true;
                case int n: text = n.ToString(inv); return true;
                case uint n: text = n.ToString(inv); return true;
                case long n: text = n.ToString(inv); return true;
                case ulong n: text = n.ToString(inv); return true;
                case float n: text = n.ToString(inv); return true;
                case double n: text = n.ToString(inv); return true;
                case decimal n: text = n.ToString(inv); return true;
                case Vector2 v: text = Floats(v.x, v.y); return true;
                case Vector3 v: text = Floats(v.x, v.y, v.z); return true;
                case Vector4 v: text = Floats(v.x, v.y, v.z, v.w); return true;
                case Quaternion q: text = Floats(q.x, q.y, q.z, q.w); return true;
                case float2 f: text = Floats(f.x, f.y); return true;
                case float3 f: text = Floats(f.x, f.y, f.z); return true;
                case float4 f: text = Floats(f.x, f.y, f.z, f.w); return true;
            }

            // Enum after the primitives, because an enum boxes as its own type, not its base.
            if (value is Enum e)
            {
                text = e.ToString();
                return true;
            }

            text = null;
            return false;
        }

        private static string Floats(params float[] components)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("[");
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(components[i].ToString(inv));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string FormatCollection(IEnumerable enumerable)
        {
            var sb = new StringBuilder();
            int count = 0;
            int shown = 0;

            try
            {
                foreach (var item in enumerable)
                {
                    count++;
                    if (shown >= MaxCollectionItems)
                        continue;

                    if (shown > 0) sb.Append(", ");
                    sb.Append(FormatItem(item));
                    shown++;
                }
            }
            catch (Exception ex)
            {
                return "<error: " + ex.GetType().Name + ">";
            }

            return "{count: " + count.ToString(CultureInfo.InvariantCulture) + ", items: [" + sb + "]}";
        }

        /// <summary>An item inside a collection uses the scalar rules only, then the object form.</summary>
        private static string FormatItem(object item)
        {
            if (item == null)
                return "null";

            return TryFormatScalar(item, out var scalar) ? scalar : FormatFallback(item);
        }

        private static string FormatFallback(object value)
        {
            string text;
            try
            {
                text = value.GetType().Name + ": " + value;
            }
            catch (Exception ex)
            {
                return "<error: " + ex.GetType().Name + ">";
            }

            return text.Length > MaxFallbackLength ? text.Substring(0, MaxFallbackLength) : text;
        }
    }
}
