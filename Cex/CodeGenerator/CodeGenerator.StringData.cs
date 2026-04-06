using System.Text;

namespace Cex;

public partial class CodeGenerator
{
    private string GetOrAddString(string text, bool newline)
    {
        string key = text + (newline ? "\n" : "");
        if (_strMap.TryGetValue(key, out string? existing)) return existing;

        _strCount++;
        string label = $"msg{_strCount}";

        var sb = new StringBuilder();
        sb.Append($"{label}: db ");

        bool inStr = false;
        foreach (char ch in text)
        {
            if (ch == '\n' || ch == '\t' || ch == '"')
            {
                if (inStr) { sb.Append("\","); inStr = false; }
                sb.Append($"{(ch == '\n' ? "0xA" : ch == '\t' ? "0x9" : "0x22")},");
            }
            else
            {
                if (!inStr) { sb.Append('"'); inStr = true; }
                sb.Append(ch);
            }
        }

        if (inStr) sb.Append('"');
        else if (text.Length > 0) sb.Length--;

        sb.Append(newline
            ? (text.Length > 0 ? ",0xA,0" : "0xA,0")
            : (text.Length > 0 ? ",0" : "0"));

        _data.AppendLine(sb.ToString());
        _strMap[key] = label;
        return label;
    }
}