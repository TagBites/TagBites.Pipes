using System.Text;

namespace TagBites.Pipes;

internal static class NamedPipeUtils
{
#if true

    public static string Encode(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);

        foreach (var c in input)
        {
            switch (c)
            {
                case '\'':
                    sb.Append("\\'");
                    break;
                case '\"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\0':
                    sb.Append("\\0");
                    break;
                case '\a':
                    sb.Append("\\a");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\v':
                    sb.Append("\\v");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    public static string Decode(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        var escape = false;

        foreach (var c in input)
        {
            if (escape)
            {
                switch (c)
                {
                    case '\'':
                        sb.Append('\'');
                        break;
                    case '\"':
                        sb.Append('\"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '0':
                        sb.Append('\0');
                        break;
                    case 'a':
                        sb.Append('\a');
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'v':
                        sb.Append('\v');
                        break;
                    default:
                        // For other characters, keep the backslash and the character as is.
                        sb.Append('\\');
                        sb.Append(c);
                        break;
                }

                escape = false;
            }
            else
            {
                if (c == '\\')
                {
                    escape = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        return sb.ToString();
    }

#else

    public static string Encode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var value = text
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
        value = value.TrimEnd();
        return value;
    }
    public static string Decode(string? response)
    {
        if (string.IsNullOrEmpty(response))
            return string.Empty;

        response = Replace(Replace(response, true), false);

        return response;
    }
    private static string Replace(string s, bool enter)
    {
        var index = 0;

        while (index < s.Length)
        {
            index = s.IndexOf(enter ? "\\n" : "\\r", index, StringComparison.Ordinal);
            if (index < 0)
                break;

            var count = 0;
            while (index - count - 1 >= 0 && s[index - count - 1] == '\\')
                ++count;

            if (count % 2 != 0)
            {
                index++;
                continue;
            }

            s = s.Substring(0, index) + (enter ? "\n" : "\r") + s.Substring(index + 2);
        }

        return s;
    }

#endif
}
