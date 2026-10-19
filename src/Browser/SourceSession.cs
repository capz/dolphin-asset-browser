using System;
using Dolphin.Storage;

namespace DolphinAssetBrowser.Browser;

public interface ISourceMounts
{
    bool MountLoader();
    bool MountDisc(string? image);
    void UnmountDisc();
    void UnmountLoader();
    DiscIdentity Identity();
}

public sealed class SourceSession : IDisposable
{
    private readonly ISourceMounts mounts;
    public bool LoaderMounted { get; private set; }
    public bool DiscMounted { get; private set; }
    public DiscIdentity? Identity { get; private set; }
    public string Root => DiscMounted ? "dvd:/" : LoaderMounted ? "gcloader:/" : "";
    public string Description { get; private set; } = "No source mounted";
    public SourceSession(ISourceMounts mounts) { this.mounts = mounts; }

    public void Refresh()
    {
        Dispose();
        LoaderMounted = mounts.MountLoader();
        if (LoaderMounted) { Description = "GC Loader SD"; return; }
        OpenDisc(null);
    }
    public void OpenImage(string path)
    {
        if (!LoaderMounted) throw new InvalidOperationException("Mount GC Loader before selecting an image.");
        OpenDisc(path);
    }
    public void OpenPhysical()
    {
        Dispose(); // Never leave raw GC Loader access active in inserted-disc mode.
        OpenDisc(null);
    }
    private void OpenDisc(string? path)
    {
        if (DiscMounted) { mounts.UnmountDisc(); DiscMounted = false; Identity = null; }
        if (!mounts.MountDisc(path))
            throw new InvalidOperationException(path == null
                ? "GC Loader unavailable and no readable GameCube disc. X retries."
                : "The selected ISO/GCM could not mount.");
        DiscMounted = true;
        try { Identity = mounts.Identity(); }
        catch { mounts.UnmountDisc(); DiscMounted = false; throw; }
        Description = path == null ? "Inserted/ODE disc" : path;
    }
    public void Dispose()
    {
        // Only clear flags after a successful unmount, so failed cleanup can retry.
        if (DiscMounted) { mounts.UnmountDisc(); DiscMounted = false; Identity = null; }
        if (LoaderMounted) { mounts.UnmountLoader(); LoaderMounted = false; }
        Description = "No source mounted";
    }
}
