using System;
using System.Collections.Generic;

namespace DolphinAssetBrowser.Browser;

public static class AssetPaths
{
    public static string Resolve(string directory, string value)
    {
        if (value.Length == 0 || value.Length > 1024 || value.IndexOf('\0') >= 0)
            throw new ArgumentException("Invalid asset path.");
        int volumeEnd = directory.IndexOf(":/", StringComparison.Ordinal);
        if (volumeEnd < 1) throw new ArgumentException("A mounted-volume directory is required.");
        string volume = directory.Substring(0, volumeEnd + 2);
        string path = value.Replace('\\', '/');
        int hash = path.IndexOf('#');
        string selector = hash < 0 ? "" : path.Substring(hash);
        if (hash >= 0)
        {
            if (selector.Length < 2 || selector.IndexOf('#', 1) >= 0)
                throw new ArgumentException("One nonempty SDK member selector is supported.");
            path = path.Substring(0, hash);
        }
        if (path.IndexOf(':') >= 0)
        {
            if (!path.StartsWith(volume, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Recipe assets must stay on the source volume.");
            path = path.Substring(volume.Length);
        }
        else if (path.StartsWith("/", StringComparison.Ordinal)) path = path.Substring(1);
        else path = directory.Substring(volume.Length).TrimEnd('/') + "/" + path;
        var parts = new List<string>();
        foreach (var part in path.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new ArgumentException("Path escapes the volume root.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        string result = volume + string.Join("/", parts) + selector;
        if (result.Length > 1024) throw new ArgumentException("Asset path is too long.");
        return result;
    }
}
