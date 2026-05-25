using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityCliConnector
{
    public sealed class CliParams
    {
        private readonly Dictionary<string, object> _values;

        public CliParams(Dictionary<string, object> values)
        {
            _values = values ?? new Dictionary<string, object>();
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public string GetString(string key, string defaultValue = null)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            return raw.ToString();
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            if (raw is bool b) return b;
            return bool.TryParse(raw.ToString(), out var parsed) && parsed;
        }

        public int? GetInt(string key, int? defaultValue = null)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            if (raw is int i)
                return i;
            if (raw is long l)
                return (int)l;
            if (int.TryParse(raw.ToString(), out var parsed))
                return parsed;
            return defaultValue;
        }

        public float? GetFloat(string key, float? defaultValue = null)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return defaultValue;
            if (raw is float f)
                return f;
            if (raw is double d)
                return (float)d;
            if (float.TryParse(raw.ToString(), out var parsed))
                return parsed;
            return defaultValue;
        }

        public string[] GetStringArray(string key)
        {
            if (!_values.TryGetValue(key, out var raw) || raw == null)
                return Array.Empty<string>();

            if (raw is string[] arr)
                return arr;

            if (raw is IEnumerable<string> enumerable)
                return enumerable.ToArray();

            if (raw is System.Collections.IEnumerable list && raw is not string)
            {
                var items = new List<string>();
                foreach (var item in list)
                {
                    if (item != null)
                        items.Add(item.ToString());
                }

                return items.ToArray();
            }

            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        public Dictionary<string, object> ToDictionary() =>
            new Dictionary<string, object>(_values);
    }
}
