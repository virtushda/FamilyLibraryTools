namespace FamilyLibraryTools.Core;

public static class CollisionGroups
{
    /// <summary>Connect name collisions and exact-content candidates, including renamed copies.</summary>
    public static IReadOnlyList<IReadOnlyList<MediaFile>> Build(IReadOnlyList<MediaFile> files, bool includeOutputOnly = false)
    {
        int[] parents = new int[files.Count];
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var contents = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < files.Count; index++)
        {
            parents[index] = index;
            Connect(names, files[index].NameKey, index);
            Connect(contents, files[index].ContentKey, index);
        }

        var groups = new Dictionary<int, List<MediaFile>>();
        for (int index = 0; index < files.Count; index++)
        {
            int root = Find(index);
            if (!groups.TryGetValue(root, out var group)) groups[root] = group = [];
            group.Add(files[index]);
        }
        var result = new List<IReadOnlyList<MediaFile>>();
        foreach (var group in groups.Values)
        {
            if (!includeOutputOnly && !group.Exists(file => !file.InOutput)) continue;
            // Prefer an existing, unmarked reference. This never chooses what to delete.
            group.Sort((left, right) => left.InOutput != right.InOutput
                ? (left.InOutput ? -1 : 1) : CompareNames(left, right));
            result.Add(group);
        }
        return result;

        static int CompareNames(MediaFile left, MediaFile right)
        {
            bool leftMarked = left.Name.Contains("_DUPLICATE-", StringComparison.OrdinalIgnoreCase);
            bool rightMarked = right.Name.Contains("_DUPLICATE-", StringComparison.OrdinalIgnoreCase);
            return leftMarked != rightMarked ? (leftMarked ? 1 : -1) : StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
        }

        int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        void Connect(Dictionary<string, int> map, string key, int index)
        {
            if (map.TryGetValue(key, out int other)) parents[Find(index)] = Find(other);
            else map.Add(key, index);
        }
    }
}
