using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JSONPath
{
    abstract class JsonElement
    {
        public abstract JsonElement this[object key] { get; set; }
    }

    class JsonValue : JsonElement
    {
        public object Value { get; set; }

        public override JsonElement this[object key]
        {
            get { throw new NotSupportedException(); }

            set { throw new NotSupportedException(); }
        }

        public JsonValue(object value)
        {
            Value = value;
        }

        public override string ToString()
        {
            return Value is string ? "\"" + Value + "\"" : Value.ToString();
        }
    }

    class JsonObject : JsonElement
    {
        public Dictionary<string, JsonElement> Properties { get; set; } = new Dictionary<string, JsonElement>();

        public override JsonElement this[object key]
        {
            get { JsonElement tmp; Properties.TryGetValue(key.ToString(), out tmp); return tmp; }

            set { Properties[key.ToString()] = value; }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder("{");
            foreach (KeyValuePair<string, JsonElement> property in Properties)
            {
                sb.Append("\"").Append(property.Key).Append("\"").Append(":").Append(property.Value.ToString()).Append(",");
            }

            if (sb.Length > 1)
                sb.Length -= 1;

            sb.Append("}");
            return sb.ToString();
        }
    }

    class JsonArray : JsonElement
    {
        public List<JsonElement> Elements { get; set; } = new List<JsonElement>();

        public override JsonElement this[object key]
        {
            get { return Elements.ElementAtOrDefault((int)key); }

            set
            {
                var index = (int)key;
                for (int i = Elements.Count; i <= index; i++)
                {
                    Elements.Add(null);
                }
                Elements[index] = value;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder("[");
            foreach (JsonElement element in Elements)
            {
                sb.Append(element?.ToString() ?? "null").Append(",");
            }

            if (sb.Length > 1)
                sb.Length -= 1;

            sb.Append("]");
            return sb.ToString();
        }
    }
}
