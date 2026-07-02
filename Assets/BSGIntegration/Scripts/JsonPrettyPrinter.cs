using System;
using System.Text;

/// <summary>Pretty-prints compact JSON (similar to JSON.stringify(value, null, 2)).</summary>
public static class JsonPrettyPrinter
{
    public static string TryFormat(string json, int indentSpaces = 2)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;

        try
        {
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (escape) { escape = false; continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        sb.Append('\n');
                        indent++;
                        WriteIndent(sb, indent, indentSpaces);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n');
                        indent = Math.Max(0, indent - 1);
                        WriteIndent(sb, indent, indentSpaces);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        sb.Append('\n');
                        WriteIndent(sb, indent, indentSpaces);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c))
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch
        {
            return json;
        }
    }

    static void WriteIndent(StringBuilder sb, int indent, int spaces)
    {
        for (int i = 0; i < indent * spaces; i++)
            sb.Append(' ');
    }
}
