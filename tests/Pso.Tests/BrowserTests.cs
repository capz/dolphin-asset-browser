using System.Globalization;
using Dolphin.Storage;
using DolphinAssetBrowser.Browser;
using DolphinAssetBrowser.Pso;

internal static class BrowserTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException) { checks++; return; }
        catch (IOException) { checks++; return; }
        throw new Exception("Expected rejection");
    }
    internal static byte[] Header()
    {
        byte[] b = new byte[DiscIdentity.HeaderSize];
        "TEST01"u8.CopyTo(b); b[7] = 2;
        b[0x1c] = 0xc2; b[0x1d] = 0x33; b[0x1e] = 0x9f; b[0x1f] = 0x3d;
        return b;
    }
    private sealed class Mounts : ISourceMounts
    {
        internal bool Loader = true, Disc = true, FailIdentity, FailUnmount;
        internal readonly List<string> Calls = new();
        public bool MountLoader() { Calls.Add("loader"); return Loader; }
        public bool MountDisc(string? path) { Calls.Add(path ?? "physical"); return Disc; }
        public void UnmountDisc() { Calls.Add("unmount-disc"); if (FailUnmount) throw new IOException("busy"); }
        public void UnmountLoader() { Calls.Add("unmount-loader"); }
        public DiscIdentity Identity()
        { if (FailIdentity) throw new IOException("media"); return DiscIdentity.ParseHeader(Header()); }
    }
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Check(AssetPaths.Resolve("dvd:/models/", "../textures/a.gvm") == "dvd:/textures/a.gvm", "relative path");
        Check(AssetPaths.Resolve("dvd:/models", "/a.bml#actor.gj") == "dvd:/a.bml#actor.gj", "selector preserved");
        Reject(() => AssetPaths.Resolve("dvd:/", "../escape"));
        Reject(() => AssetPaths.Resolve("dvd:/", "sd:/file"));
        Reject(() => AssetPaths.Resolve("dvd:/", "a.bml##entry"));
        var basic = AssetDefinition.Parse("name=Test\nmodel=a.gj\nradius=2\ncentery=1", "dvd:/assets");
        Check(basic.Recipe.ModelPath == "dvd:/assets/a.gj" && basic.Radius == 2 && basic.CenterY == 1, "recipe/bounds");
        Check(!basic.IsDiscBound && basic.HasBounds, "manual import labeling");
        var extended = AssetDefinition.Parse("model=a.bml#entry.gj\nprofile=ninja\nmotion=walk.njm\nfps=30", "dvd:/");
        Check(extended.Recipe.Profile == PsoLoaderProfile.NinjaRel && extended.Recipe.MotionFramesPerSecond == 30, "explicit importer settings");
        Reject(() => AssetDefinition.Parse("model=a\nmodel=b", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\nunknown=yes", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\nradius=NaN", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\nradius=0", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\ncenterx=1", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\nmotion=walk.njm", "dvd:/"));
        Reject(() => AssetDefinition.Parse("model=a\ngameid=TEST01", "dvd:/"));
        Reject(() => AssetDefinition.Parse(new string('a', 16385), "dvd:/"));
        string discFields = "model=a.gj\ngameid=TEST01\ndiscnumber=0\nrevision=2\nsignaturepath=header.bin\nsignaturehex=0102AB";
        var bound = AssetDefinition.Parse(discFields, "dvd:/");
        bound.ValidateIdentity(DiscIdentity.ParseHeader(Header()));
        using (var stream = new MemoryStream(new byte[] {1, 2, 171, 7})) bound.ValidateSignature(stream);
        Check(bound.IsDiscBound, "exact identity and signature accepted");
        Reject(() => bound.ValidateIdentity(null));
        byte[] wrong = Header(); wrong[7] = 3;
        Reject(() => bound.ValidateIdentity(DiscIdentity.ParseHeader(wrong)));
        Reject(() => bound.ValidateSignature(new MemoryStream(new byte[] {1, 2})));
        Reject(() => AssetDefinition.Parse(discFields.Replace("0102AB", "GG"), "dvd:/"));
        var mounts = new Mounts(); var session = new SourceSession(mounts);
        session.Refresh();
        Check(session.LoaderMounted && !session.DiscMounted && mounts.Calls.SequenceEqual(new[] {"loader"}), "GC Loader first");
        session.OpenImage("gcloader:/games/test.iso");
        Check(session.Root == "dvd:/" && session.Identity!.GameId == "TEST01", "unified disc mount");
        session.OpenPhysical();
        Check(mounts.Calls.TakeLast(3).SequenceEqual(new[] {"unmount-disc", "unmount-loader", "physical"}), "dependent cleanup before physical");
        session.Dispose();
        Reject(() => session.OpenImage("gcloader:/games/test.iso"));
        mounts.Loader = false; session.Refresh(); Check(session.DiscMounted, "physical fallback"); session.Dispose();
        mounts.Loader = true; session.Refresh(); mounts.FailIdentity = true;
        Reject(() => session.OpenImage("bad.iso"));
        Check(!session.DiscMounted && session.LoaderMounted, "identity failure cleans disc and retains loader");
        mounts.FailIdentity = false; session.OpenImage("test.iso"); mounts.FailUnmount = true;
        Reject(() => session.Dispose()); Check(session.DiscMounted && session.LoaderMounted, "failed unmount remains owned");
        mounts.FailUnmount = false; session.Dispose();
        var camera = new OrbitCamera(); camera.Reset(1, 2, 3, 2);
        Check(camera.Distance == 6 && camera.TargetY == 2, "camera framing");
        camera.Reset(1, 2, 3, 2, 214f / 480f);
        float halfFov = MathF.Atan(MathF.Tan(MathF.PI / 6) * (214f / 480f));
        Check(camera.Distance * MathF.Sin(halfFov) > 2, "sphere fits narrow model viewport");
        Reject(() => camera.Reset(aspect: 0));
        Reject(() => camera.Reset(aspect: float.NaN));
        camera.Update(.1f, 1, 100, 0, 0, -1000);
        Check(camera.Pitch <= 1.45f && camera.Distance >= .1f && float.IsFinite(camera.EyeX), "camera clamps");
        Reject(() => camera.Reset(radius: float.PositiveInfinity));
        Reject(() => camera.Update(-1, 0, 0, 0, 0, 0));
        string root = Path.Combine(Path.GetTempPath(), "asset-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllBytes(Path.Combine(root, "z.iso"), Header());
            File.WriteAllBytes(Path.Combine(root, "sub", "a.GCM"), Header());
            File.WriteAllBytes(Path.Combine(root, "bad.iso"), new byte[3]);
            File.WriteAllBytes(Path.Combine(root, "ignored.rvz"), Header());
            var result = ImageDiscovery.Scan(root);
            Check(result.Images.Count == 2 && result.Skipped == 1, "discovery validates headers and format");
            Check(result.Images[0].Path.EndsWith("a.GCM"), "deterministic ordering");
            Check(ImageDiscovery.Scan(root, maxEntries: 1).LimitReached, "bounded discovery");
            Check(ImageDiscovery.Scan(root, maxDepth: 0).Images.Count == 1, "depth bound");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine(checks + " browser source/recipe/camera/discovery assertions passed");
    }
}
