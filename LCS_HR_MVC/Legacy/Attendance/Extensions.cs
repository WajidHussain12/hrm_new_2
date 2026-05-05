using System.Collections.Generic;
using System.Linq;

public static class Extensions
{
    public static bool IsAny<T>(this IEnumerable<T> data)
    {
        return data != null && data.Any();
    }
}
