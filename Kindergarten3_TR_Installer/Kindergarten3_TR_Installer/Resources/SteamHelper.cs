using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

public static class SteamHelper
{
    public static string GetSteamPathFromRegistry()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            if (key != null)
            {
                object path = key.GetValue("SteamPath");
                if (path != null)
                    return path.ToString().Replace('/', '\\');
            }
        }
        return null;
    }

    public static List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string>();

        if (string.IsNullOrEmpty(steamPath))
            return folders;

        // Add default Steam path first
        folders.Add(steamPath);

        string libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersVdf))
            return folders;

        try
        {
            string[] lines = File.ReadAllLines(libraryFoldersVdf);

            bool insideLibraryBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Detect line like "0", "1", etc.
                if (Regex.IsMatch(line, "^\"\\d+\"$"))
                {
                    // Check if next line is "{"
                    if (i + 1 < lines.Length && lines[i + 1].Trim() == "{")
                    {
                        insideLibraryBlock = true;
                        i++; // skip the brace line
                        continue;
                    }
                }

                if (insideLibraryBlock)
                {
                    if (line == "}")
                    {
                        insideLibraryBlock = false;
                        continue;
                    }

                    // Look for "path" line: "path"        "E:\\Steam"
                    var match = Regex.Match(line, "^\"path\"\\s+\"(.+)\"$");
                    if (match.Success)
                    {
                        string path = match.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(path) && !folders.Contains(path))
                            folders.Add(path);
                    }
                }
            }
        }
        catch
        {
            // ignore errors, just return what we have
        }

        return folders;
    }

    // Search for the game folder in steam libraries by exe name or folder name
    public static string FindGameFolderInSteamLibraries(List<string> libraries, string gameExeName)
    {
        foreach (var lib in libraries)
        {
            string steamAppsPath = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamAppsPath))
                continue;

            // Games installed inside steamapps/common/<GameFolder>
            string commonPath = Path.Combine(steamAppsPath, "common");
            if (!Directory.Exists(commonPath))
                continue;

            foreach (var folder in Directory.GetDirectories(commonPath))
            {
                // Check if the exe file exists inside this folder
                string exePath = Path.Combine(folder, gameExeName);
                if (File.Exists(exePath))
                {
                    return folder; // Return the full path of the found game folder
                }
            }
        }
        return null;
    }
}
