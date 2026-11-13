using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// The small JSON reader and writer behind <see cref="TouchTuning"/>. Hand-written because
    /// <c>JsonUtility</c> fills defaults silently, and tuning must tell a missing field from a wrong one.
    /// </summary>
    /// <remarks>
    /// Values come back as <see cref="JsonObject"/>, <c>List&lt;object&gt;</c>, <c>string</c>, <c>bool</c>,
    /// <see cref="JsonNumber"/> or <see cref="JsonNull"/>. Numbers keep their raw text so floats round-trip exactly.
    /// </remarks>
    static class TuningJson
    {
        internal sealed class JsonObject
        {
            public readonly List<KeyValuePair<string, object>> Members = new List<KeyValuePair<string, object>>();

            public bool TryGet(string key, out object value)
            {
                for (int i = 0; i < Members.Count; i++)
                {
                    if (Members[i].Key == key)
                    {
                        value = Members[i].Value;
                        return true;
                    }
                }

                value = null;
                return false;
            }
        }

        internal sealed class JsonNumber
        {
            public readonly string Raw;
            public JsonNumber(string raw) => Raw = raw;

            public bool TryFloat(out float value) =>
                float.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            public bool TryInt(out int value) =>
                int.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        internal sealed class JsonNull
        {
            public static readonly JsonNull Value = new JsonNull();
        }

        /// <summary>Parses <paramref name="text"/>; on failure returns <c>false</c> and a readable reason.</summary>
        public static bool TryParse(string text, out object result, out string error)
        {
            var reader = new Reader(text);
            result = null;
            error = null;

            if (!reader.TryValue(out result, out error))
                return false;

            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                error = reader.Error("unexpected text after the end of the JSON");
                result = null;
                return false;
            }

            return true;
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }

        /// <summary>A float as the shortest text that reads back as the same float.</summary>
        public static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        struct Reader
        {
            readonly string m_Text;
            int m_Pos;
            int m_Depth;

            public Reader(string text)
            {
                m_Text = text ?? "";
                m_Pos = 0;
                m_Depth = 0;
            }

            public bool AtEnd => m_Pos >= m_Text.Length;

            public string Error(string what) => $"not valid JSON at character {m_Pos + 1}: {what}";

            public void SkipWhitespace()
            {
                while (m_Pos < m_Text.Length && char.IsWhiteSpace(m_Text[m_Pos]))
                    m_Pos++;
            }

            public bool TryValue(out object value, out string error)
            {
                value = null;
                error = null;
                SkipWhitespace();

                if (AtEnd)
                {
                    error = Error("the text ended where a value was expected");
                    return false;
                }

                char c = m_Text[m_Pos];
                if (c == '{') return TryObject(out value, out error);
                if (c == '[') return TryArray(out value, out error);
                if (c == '"')
                {
                    bool ok = TryString(out string s, out error);
                    value = s;
                    return ok;
                }

                if (c == '-' || (c >= '0' && c <= '9')) return TryNumber(out value, out error);
                if (TryWord("true")) { value = true; return true; }
                if (TryWord("false")) { value = false; return true; }
                if (TryWord("null")) { value = JsonNull.Value; return true; }

                error = Error($"unexpected '{c}'");
                return false;
            }

            bool TryWord(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Pos, word, 0, word.Length) != 0)
                    return false;

                m_Pos += word.Length;
                return true;
            }

            bool TryObject(out object value, out string error)
            {
                value = null;
                if (++m_Depth > 64)
                {
                    error = Error("nested too deeply");
                    return false;
                }

                var obj = new JsonObject();
                m_Pos++; // {
                SkipWhitespace();
                if (!AtEnd && m_Text[m_Pos] == '}')
                {
                    m_Pos++;
                    m_Depth--;
                    value = obj;
                    error = null;
                    return true;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || m_Text[m_Pos] != '"')
                    {
                        error = Error("expected a quoted member name");
                        return false;
                    }

                    if (!TryString(out string key, out error))
                        return false;

                    SkipWhitespace();
                    if (AtEnd || m_Text[m_Pos] != ':')
                    {
                        error = Error($"expected ':' after \"{key}\"");
                        return false;
                    }

                    m_Pos++;
                    if (!TryValue(out object member, out error))
                        return false;

                    obj.Members.Add(new KeyValuePair<string, object>(key, member));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        error = Error("the text ended inside an object");
                        return false;
                    }

                    char c = m_Text[m_Pos++];
                    if (c == '}')
                        break;
                    if (c != ',')
                    {
                        m_Pos--;
                        error = Error("expected ',' or '}'");
                        return false;
                    }
                }

                m_Depth--;
                value = obj;
                error = null;
                return true;
            }

            bool TryArray(out object value, out string error)
            {
                value = null;
                if (++m_Depth > 64)
                {
                    error = Error("nested too deeply");
                    return false;
                }

                var list = new List<object>();
                m_Pos++; // [
                SkipWhitespace();
                if (!AtEnd && m_Text[m_Pos] == ']')
                {
                    m_Pos++;
                    m_Depth--;
                    value = list;
                    error = null;
                    return true;
                }

                while (true)
                {
                    if (!TryValue(out object item, out error))
                        return false;

                    list.Add(item);
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        error = Error("the text ended inside a list");
                        return false;
                    }

                    char c = m_Text[m_Pos++];
                    if (c == ']')
                        break;
                    if (c != ',')
                    {
                        m_Pos--;
                        error = Error("expected ',' or ']'");
                        return false;
                    }
                }

                m_Depth--;
                value = list;
                error = null;
                return true;
            }

            bool TryString(out string value, out string error)
            {
                value = null;
                var sb = new StringBuilder();
                m_Pos++; // opening quote

                while (m_Pos < m_Text.Length)
                {
                    char c = m_Text[m_Pos++];
                    if (c == '"')
                    {
                        value = sb.ToString();
                        error = null;
                        return true;
                    }

                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        break;

                    char e = m_Text[m_Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_Pos + 4 > m_Text.Length ||
                                !int.TryParse(m_Text.Substring(m_Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                error = Error("bad \\u escape");
                                return false;
                            }

                            sb.Append((char)code);
                            m_Pos += 4;
                            break;
                        default:
                            error = Error($"bad escape '\\{e}'");
                            return false;
                    }
                }

                error = Error("a string is not closed");
                return false;
            }

            bool TryNumber(out object value, out string error)
            {
                int start = m_Pos;
                while (m_Pos < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Pos]) >= 0)
                    m_Pos++;

                string raw = m_Text.Substring(start, m_Pos - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    m_Pos = start;
                    value = null;
                    error = Error($"'{raw}' is not a number");
                    return false;
                }

                value = new JsonNumber(raw);
                error = null;
                return true;
            }
        }
    }
}
