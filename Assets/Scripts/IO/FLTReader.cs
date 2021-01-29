using SFB;
using System.IO;

namespace FLT.IO
{
    public class FLTReader
    {
        public bool PickFolder(out string newFolderPath, string startingPath = null)
        {
            string[] paths = StandaloneFileBrowser.OpenFolderPanel("Select a Folder", startingPath != null ? startingPath : "C:\\", false);

            if (paths == null || paths.Length == 0)
            {
                newFolderPath = "";
                return false;
            }

            if (Directory.Exists(paths[0]))
            {
                newFolderPath = paths[0];
                return true;
            }
            else
                newFolderPath = "";

            return false;
        }

        public void AnalyzeFiles()
        {
            //Directory.
        }
    }
}