namespace FamilyLibraryTools.Core;

public static class PathSafety
{
    public static (string Input, string Output) ValidateRoots(string input, string output)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Choose both an input folder and an output folder.");
        if (!Directory.Exists(input) || !Directory.Exists(output))
            throw new DirectoryNotFoundException("Both folders must exist. You can create the output folder in the folder picker.");
        using var source = NativeFiles.OpenDirectory(input);
        using var destination = NativeFiles.OpenDirectory(output);
        string sourceIdentity = source.FinalPath(true), destinationIdentity = destination.FinalPath(true);
        if (IsWithin(sourceIdentity, destinationIdentity) || IsWithin(destinationIdentity, sourceIdentity) ||
            source.IsWithin(destination) || destination.IsWithin(source))
            throw new ArgumentException("Input and output must be separate folders; neither can contain the other.");
        return (source.FinalPath(), destination.FinalPath());
    }

    public static string CanonicalDirectory(string path)
    {
        using var directory = NativeFiles.OpenDirectory(path);
        return directory.FinalPath();
    }

    public static bool IsWithin(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void RejectBookkeepingRoot(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if (Path.GetFileName(current).Equals(".family-library-tools", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose a media library, not the application's recovery or bookkeeping folders.");
    }

    public static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Links, junctions, and cloud placeholders are not followed: {current}");
        }
    }
}
