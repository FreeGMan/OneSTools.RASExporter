using System.Collections.Generic;
using System.Text;

namespace OneSTools.Ras.Exporter.Core.ClickHouse;

/// <summary>
/// Имена БД ClickHouse приводят к конвенции [a-z0-9_]: кириллица транслитерируется,
/// прочие символы (пробелы, дефисы…) схлопываются в '_' (TODO.md T2/R5).
/// </summary>
public static class ClickHouseDatabaseNamer
{
    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch",
        ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };

    public static string Slugify(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length * 2);
        char? last = null;

        foreach (char ch in input)
        {
            char lower = char.ToLowerInvariant(ch);
            string piece;

            if (Translit.TryGetValue(lower, out string? mapped))
                piece = mapped;
            else if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                piece = lower.ToString();
            else
                piece = "_";

            foreach (char c in piece)
            {
                // схлопывание повторных '_'
                if (c == '_' && last == '_')
                    continue;
                sb.Append(c);
                last = c;
            }
        }

        return sb.ToString().Trim('_');
    }
}
