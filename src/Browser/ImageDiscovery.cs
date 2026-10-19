using System;
using System.IO;
using System.Collections.Generic;
using Dolphin.Storage;

namespace DolphinAssetBrowser.Browser;

public sealed class ImageCandidate
{
    public string Path { get; }
    public DiscIdentity Identity { get; }
    public ImageCandidate(string path, DiscIdentity identity) { Path = path; Identity = identity; }
}
public sealed class ImageScan
{
    public readonly List<ImageCandidate> Images = new List<ImageCandidate>();
    public bool LimitReached;
    public int Skipped;
    public string Warning = "";
}
public static class ImageDiscovery
{
    // Count every directory entry, not only matching extensions, to bound work.
    public static ImageScan Scan(string root, int maxEntries = 2048, int maxImages = 64, int maxDepth = 6)
    {
        if (maxEntries < 1 || maxImages < 1 || maxDepth < 0) throw new ArgumentException("Invalid scan limits.");
        var result = new ImageScan();
        if (!Directory.Exists(root)) { result.Warning = "No /games directory. Browse extracted files or use DVD."; return result; }
        var directories = new List<string>(); var depths = new List<int>();
        directories.Add(root); depths.Add(0); int entries = 0;
        while (directories.Count != 0 && !result.LimitReached)
        {
            int last = directories.Count - 1; string current = directories[last]; int depth = depths[last];
            directories.RemoveAt(last); depths.RemoveAt(last);
            try
            {
                foreach (var path in Directory.EnumerateFiles(current))
                {
                    if (++entries > maxEntries || result.Images.Count >= maxImages) { result.LimitReached = true; break; }
                    string ext = Path.GetExtension(path);
                    if (!ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".gcm", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        using (var input = File.OpenRead(path))
                        {
                            if (input.Length >= 2147483648L) throw new IOException("Image exceeds native file-position limits.");
                            var header = new byte[DiscIdentity.HeaderSize]; int count = 0;
                            while (count < header.Length)
                            {
                                int n = input.Read(header, count, header.Length - count);
                                if (n == 0) throw new IOException("Truncated disc header.");
                                count += n;
                            }
                            result.Images.Add(new ImageCandidate(path, DiscIdentity.ParseHeader(header)));
                        }
                    }
                    catch (ArgumentException) { result.Skipped++; }
                    catch (IOException error) { result.Skipped++; result.Warning = error.Message; }
                }
                if (result.LimitReached) break;
                foreach (var path in Directory.EnumerateDirectories(current))
                {
                    if (++entries > maxEntries || directories.Count >= 128) { result.LimitReached = true; break; }
                    if (depth >= maxDepth) { result.Warning = "Depth limit reached; deeper images were not scanned."; continue; }
                    directories.Add(path); depths.Add(depth + 1);
                }
            }
            catch (IOException error) { result.Warning = error.Message; }
        }
        result.Images.Sort((a, b) =>
        {
            int order = string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            return order == 0 ? string.CompareOrdinal(a.Path, b.Path) : order;
        });
        return result;
    }
}
