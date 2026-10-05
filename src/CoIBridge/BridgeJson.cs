using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace CoIBridge
{
    // Small JSON value tree, using the framework JSON parser (no game objects on the wire).
    public static class BridgeJson
    {
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        public static object Parse(string text)
        {
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = 1048576, MaxArrayLength = 1048576, MaxBytesPerRead = 4096, MaxNameTableCharCount = 1048576 };
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(new UTF8Encoding(false, true).GetBytes(text), quotas)) {
                var doc = new XmlDocument(); doc.Load(reader); return Read(doc.DocumentElement);
            }
        }
        private static object Read(XmlElement node)
        {
            string type = node.GetAttribute("type");
            if (type == "object") {
                var result = Obj();
                foreach (XmlElement child in node.ChildNodes) {
                    string key = child.HasAttribute("item") ? child.GetAttribute("item") : child.LocalName;
                    if (result.ContainsKey(key)) throw new FormatException("Duplicate JSON key: " + key);
                    result.Add(key, Read(child));
                }
                return result;
            }
            if (type == "array") { var list = new List<object>(); foreach (XmlElement child in node.ChildNodes) list.Add(Read(child)); return list; }
            if (type == "null") return null;
            if (type == "boolean") return Boolean.Parse(node.InnerText);
            if (type == "number") return Decimal.Parse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture);
            return node.InnerText;
        }
        public static string Encode(object value)
        {
            var b = new StringBuilder(); Write(b, value); return b.ToString();
        }
        private static void Write(StringBuilder b, object value)
        {
            if (value == null) { b.Append("null"); return; }
            var dict = value as IDictionary<string, object>;
            if (dict != null) {
                b.Append('{'); bool first = true;
                var keys = new List<string>(dict.Keys); keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys) { if (!first) b.Append(','); first = false; Write(b, key); b.Append(':'); Write(b, dict[key]); }
                b.Append('}'); return;
            }
            var s = value as string;
            if (s != null) {
                b.Append('"'); foreach (char c in s) {
                    if (c == '"' || c == '\\') b.Append('\\').Append(c);
                    else if (c < 32 || Char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else b.Append(c);
                } b.Append('"'); return;
            }
            if (value is bool) { b.Append((bool)value ? "true" : "false"); return; }
            var sequence = value as IEnumerable;
            if (sequence != null) { b.Append('['); bool first = true; foreach (object item in sequence) { if (!first) b.Append(','); first = false; Write(b, item); } b.Append(']'); return; }
            if (value is int || value is long || value is uint || value is ulong || value is byte || value is sbyte || value is short || value is ushort || value is decimal || value is double || value is float) {
                double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (Double.IsNaN(n) || Double.IsInfinity(n)) throw new FormatException("Non-finite number");
                b.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
            throw new ArgumentException("Not a JSON value: " + value.GetType().FullName);
        }
        public static object FromDto<T>(T value)
        {
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Parse(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }
    public sealed class BridgeArgs
    {
        public readonly Dictionary<string, object> Values;
        private readonly HashSet<string> used = new HashSet<string>();
        public BridgeArgs(object value) { Values = value as Dictionary<string, object>; if (Values == null) throw new ArgumentException("Expected JSON object"); }
        public object Take(string key, bool required = true) { used.Add(key); object value; if (!Values.TryGetValue(key, out value) && required) throw new ArgumentException("Missing " + key); return value; }
        public string Text(string key, bool required = true) { object v = Take(key, required); if (v == null && !required) return null; var s = v as string; if (s == null || s.Length == 0 || s.Length > 256) throw new ArgumentException("Invalid " + key); return s; }
        public int Int(string key, int min = Int32.MinValue, int max = Int32.MaxValue, int? fallback = null) {
            object v = Take(key, !fallback.HasValue); if (v == null && fallback.HasValue) return fallback.Value;
            if (!(v is decimal)) throw new ArgumentException("Expected integer " + key);
            decimal n = (decimal)v; if (n != Decimal.Truncate(n) || n < min || n > max) throw new ArgumentException("Out of range " + key); return (int)n;
        }
        public bool Bool(string key, bool? fallback = null) { object v = Take(key, !fallback.HasValue); if (v == null && fallback.HasValue) return fallback.Value; if (!(v is bool)) throw new ArgumentException("Expected boolean " + key); return (bool)v; }
        public double? Ratio(string key) { object v = Take(key, false); if (v == null) return null; if (!(v is decimal) || (decimal)v < 0 || (decimal)v > 1) throw new ArgumentException("Expected ratio 0..1: " + key); return (double)(decimal)v; }
        public List<object> List(string key, int max = 256) { var v = Take(key) as List<object>; if (v == null || v.Count > max) throw new ArgumentException("Invalid array " + key); return v; }
        public void Done() { foreach (string key in Values.Keys) if (!used.Contains(key)) throw new ArgumentException("Unknown argument " + key); }
    }
}
