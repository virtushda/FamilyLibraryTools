using System.Security.Cryptography;

namespace FamilyLibraryTools.Core;

public static class FileContents
{
    public static FileStream OpenRead(string path) => NativeFiles.OpenRead(path);

    public static async Task<string> HashAsync(Stream stream, CancellationToken token)
    {
        stream.Position = 0;
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    public static async Task ValidateAsync(Stream stream, MediaFile file, CancellationToken token)
    {
        if (stream.Length != file.Length || await HashAsync(stream, token) != file.Hash)
            throw new IOException($"File changed since scanning. Scan again before continuing: {file.Path}");
    }

    public static async Task<bool> EqualStreamsAsync(Stream first, Stream second, CancellationToken token)
    {
        if (first.Length != second.Length) return false;
        first.Position = 0;
        second.Position = 0;
        byte[] left = new byte[128 * 1024];
        byte[] right = new byte[left.Length];
        long remaining = first.Length;
        while (remaining > 0)
        {
            int count = (int)Math.Min(left.Length, remaining);
            await first.ReadExactlyAsync(left.AsMemory(0, count), token);
            await second.ReadExactlyAsync(right.AsMemory(0, count), token);
            if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
            remaining -= count;
        }
        return true;
    }
}
