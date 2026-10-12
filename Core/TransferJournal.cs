using System.Text;
using System.Text.Json;

namespace FamilyLibraryTools.Core;

public sealed class TransferJournal : IDisposable
{
    readonly FileStream sessionLock;
    readonly FileStream stream;
    readonly NativeFiles.DirectoryHandle directory;
    public string Path { get; }

    public TransferJournal(string outputRoot)
    {
        directory = NativeFiles.OpenDirectory(System.IO.Path.Combine(outputRoot, ".family-library-tools"), true);
        try
        {
            sessionLock = NativeFiles.OpenSessionLock(directory);
            try
            {
                Path = System.IO.Path.Combine(directory.FinalPath(), $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
                stream = NativeFiles.CreateNewFile(directory, System.IO.Path.GetFileName(Path), FileAccess.Write, FileShare.Read);
            }
            catch { sessionLock.Dispose(); throw; }
        }
        catch { directory.Dispose(); throw; }
    }

    public void Record(string action, string source = "", string destination = "", string hash = "", string detail = "")
    {
        string line = JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, Action = action, Source = source, Destination = destination, Hash = hash, Detail = detail });
        lock (stream)
        {
            stream.Write(Encoding.UTF8.GetBytes(line + "\n"));
            stream.Flush(true);
        }
    }

    public void Dispose() { stream.Dispose(); sessionLock.Dispose(); directory.Dispose(); }
}
