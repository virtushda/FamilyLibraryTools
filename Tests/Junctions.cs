using System.Diagnostics;
using System.IO;
using FamilyLibraryTools.Core;

static partial class Program
{
    static async Task Junctions()
    {
        var folders = Folders();
        string source = Write(folders.Input, "photo.jpg", "source bytes");
        string external = Path.Combine(Path.GetDirectoryName(folders.Input)!, "elsewhere");
        Directory.CreateDirectory(external);
        string other = Write(external, "other.jpg", "unrelated bytes");
        MakeJunction(Path.Combine(folders.Input, "linked"), external);
        ScanResult scan = await Scan(folders);
        Check(scan.Files.Count == 1 && scan.Issues.Count == 1, "Scanner followed a junction.");
        MakeJunction(Path.Combine(folders.Output, "2026"), external);
        await Throws<IOException>(() => Organize(scan));
        Check(File.Exists(source) && File.ReadAllText(other) == "unrelated bytes" && !Directory.Exists(Path.Combine(external, "4")), "Linked output redirected a move.");
        bool rejected = false;
        try { PathSafety.ValidateRoots(Path.Combine(folders.Input, "linked"), folders.Output); } catch (IOException) { rejected = true; }
        Check(rejected, "A linked input root was accepted.");
    }

    static void MakeJunction(string link, string target)
    {
        Check(Path.GetFullPath(link).StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(target).StartsWith(scratch + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Junction fixture escaped scratch root.");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference = 'Stop'; $null = New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "'");
        using var process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Check(process.ExitCode == 0, "Cannot create junction fixture: " + error);
    }
}
