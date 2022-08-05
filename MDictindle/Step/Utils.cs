using System.Text;

namespace MDictindle.Step;

public static class Utils
{
    public static int[] GetSubStringIndexes(string str, string substr, int startPos)
    {
        int foundPos;
        var foundItems = new List<int>();
        do
        {
            foundPos = str.IndexOf(substr, startPos, StringComparison.Ordinal);
            if (foundPos == -1) continue;
            startPos = foundPos + substr.Length;
            foundItems.Add(foundPos);
        } while (foundPos > -1 && startPos < str.Length);

        return foundItems.ToArray();
    }

    public static string GetId(string id)
    {
        if (id.Contains('@') || id.Contains('=') || id.Contains('&'))
        {
            // 'h' for HashCode
            id = ("h" + id.GetHashCode()).Replace('-', '0');
        }

        return id;
    }

    public static IEnumerable<List<T>> GetPermutationAndCombination<T>(List<ISet<T>> sets)
    {
        switch (sets.Count)
        {
            case 1:
                return sets[0].Select(it => new List<T> { it }).ToHashSet();
            case 0:
                throw new ArgumentException("没有参数", nameof(sets));
        }

        var res = new HashSet<List<T>>();
        // sets 长度至少是 2
        foreach (var t in sets[0])
        {
            var res2 = GetPermutationAndCombination(sets.GetRange(1, sets.Count-1));
            foreach (var list in res2)
            {
                list.Insert(0, t);
                res.Add(list);
            }
        }

        return res;
    }

    public static string GetInflString(string[] infls)
    {
        if (infls.Length == 0)
        {
            return "";
        }
        
        const string front = "<idx:infl>";
        const string end = "</idx:infl>";
        var sb = new StringBuilder(front);
        foreach (var infl in infls)
        {
            if (infl != string.Empty)
            {
                sb.Append($"<idx:iform name=\"\" value=\"{infl}\" />");
            }
        }

        sb.Append(end);
        return sb.ToString();
    }
}