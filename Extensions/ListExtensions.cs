namespace MetalMintSolid.Extensions;

public static class ListExtensions
{
    public static int IndexOfOrAdd<T>(this List<T> list, T item)
    {
        var index = list.IndexOf(item);
        if (index == -1)
        {
            list.Add(item);
            return list.Count - 1;
        }

        return index;
    }
}
