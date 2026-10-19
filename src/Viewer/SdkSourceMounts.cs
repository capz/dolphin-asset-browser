using Dolphin.Storage;
using DolphinAssetBrowser.Browser;

internal sealed class SdkSourceMounts : ISourceMounts
{
    public bool MountLoader() => Storage.Mount(StorageDevice.GcLoader);
    public bool MountDisc(string? image) => image == null ? Storage.MountDisc() : Storage.MountDisc(image);
    public void UnmountDisc() => Storage.Unmount(StorageDevice.DvdDrive);
    public void UnmountLoader() => Storage.Unmount(StorageDevice.GcLoader);
    public DiscIdentity Identity() => Storage.GetMountedDiscIdentity();
}
