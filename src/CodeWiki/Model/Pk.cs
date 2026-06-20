using System.Globalization;
using System.Text;

namespace CodeWiki.Model;

public static class Pk
{
    public static string Of(params string[] parts)
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong hash = Offset;
        foreach (var b in Encoding.UTF8.GetBytes(string.Join("|", parts)))
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash.ToString(CultureInfo.InvariantCulture);
    }
}
