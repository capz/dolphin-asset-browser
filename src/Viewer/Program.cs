using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;
using Dolphin.Timing;
using DolphinAssetBrowser.Pso;
using DolphinAssetBrowser.Browser;

internal enum Screen { Sources, Files, Inspector, Error, Loading }
internal enum Operation { None, Refresh, Physical, Image, Directory, Asset, Profiles }
internal sealed class BrowserItem
{
    internal string Label, Path;
    internal Operation Operation;
    internal BrowserItem(string label, string path, Operation operation)
    { Label = label.Length > 70 ? label.Substring(0, 67) + "..." : label; Path = path; Operation = operation; }
}

internal sealed class BrowserApp : IDisposable
{
    private readonly SourceSession source = new SourceSession(new SdkSourceMounts());
    private readonly BrowserUi ui = new BrowserUi();
    private readonly Renderer renderer = new Renderer();
    private readonly Transform transform = new Transform();
    private readonly OrbitCamera orbit = new OrbitCamera();
    private readonly FrameClock clock = new FrameClock { MaximumDeltaSeconds = .1f };
    private readonly List<BrowserItem> items = new List<BrowserItem>();
    private Screen screen = Screen.Sources;
    private Operation pending = Operation.Refresh, retry;
    private string pendingPath = "", retryPath = "", directory = "", status = "", stage = "", error = "";
    private int selection, top;
    private bool quit;
    private bool profilesMounted;
    private PsoLoaderProfile rawProfile = PsoLoaderProfile.RigidGj;
    private Model? model;
    private ModelInstance? instance;
    private ModelEffects? effects;
    private AssetDefinition? asset;
    private Camera camera = new Camera();
    private int clip = -1, speedIndex = 2;
    private readonly float[] speeds = { .25f, .5f, 1, 2 };
    private bool paused, autoRotate;
    private float clipTime, statsDelay;
    private string statistics = "", playback = "", loadDetails = "", frameMetrics = "";
    private long lastFrame;
    private float frameMilliseconds;
    private string sourceTitle = "No source mounted", discMetadata = "";

    internal async Task<int> RunAsync()
    {
        while (!quit)
        {
            if (pending != Operation.None)
            {
                Operation op = pending; string path = pendingPath; pending = Operation.None;
                retry = op; retryPath = path;
                try { await ExecuteAsync(op, path); }
                catch (Exception failure)
                {
                    error = failure.Message; status = stage + ": " + error;
                    DebugOverlay.WriteLine(status); screen = Screen.Error;
                }
            }
            long frameTime = Clock.Timestamp;
            frameMilliseconds = lastFrame == 0 ? 0 : (float)(frameTime - lastFrame) / 1000f;
            lastFrame = frameTime;
            float dt = clock.Tick();
            Controller.Update();
            Update(Controller.GetState(), dt);
            if (!quit) Present();
        }
        return 0;
    }
    private void Queue(Operation op, string path = "") { pending = op; pendingPath = path; }
    private void ClearItems() { items.Clear(); selection = top = 0; }
    private void UpdateBanner()
    {
        var identity = source.Identity;
        sourceTitle = identity == null ? source.Description : identity.Title;
        discMetadata = identity == null ? "" : $"DISC {identity.GameId}  INDEX {identity.DiscNumber}  REV {identity.Revision}";
        ui.SetBanner(identity?.Banner == null ? null : identity.Banner.Rgba32);
    }
    private async Task ExecuteAsync(Operation op, string path)
    {
        if (op != Operation.Asset) loadDetails = "";
        if (op == Operation.Refresh || op == Operation.Physical)
        {
            ReleaseModel(); ReleaseProfiles(); ui.SetBanner(null); stage = "Mounting source";
            if (op == Operation.Physical) source.OpenPhysical(); else source.Refresh();
            UpdateBanner();
            if (source.DiscMounted) ReadDirectory(source.Root);
            else
            {
                ClearItems(); stage = "Scanning ISO headers";
                items.Add(new BrowserItem("Browse extracted assets", source.Root, Operation.Directory));
                items.Add(new BrowserItem("Use inserted disc (releases GC Loader)", "", Operation.Physical));
                var scan = ImageDiscovery.Scan("gcloader:/games");
                foreach (var image in scan.Images)
                    items.Add(new BrowserItem(image.Identity.GameId + " " + image.Identity.Title + " " + Path.GetFileName(image.Path), image.Path, Operation.Image));
                status = scan.Images.Count.ToString() + " images; " + scan.Skipped.ToString() + " invalid/unreadable skipped. " + scan.Warning;
                if (scan.LimitReached) status = "Scan limit reached. Narrow /games or use file browsing.";
                screen = Screen.Sources; directory = "";
            }
        }
        else if (op == Operation.Image)
        {
            stage = "Mounting selected image"; source.OpenImage(path); UpdateBanner(); ReadDirectory(source.Root);
        }
        else if (op == Operation.Directory) ReadDirectory(path);
        else if (op == Operation.Profiles)
        {
            stage = "Opening SD2SP2 disc recipes";
            if (!profilesMounted) profilesMounted = Storage.Mount(StorageDevice.SdSerialPort2);
            if (!profilesMounted) throw new IOException("No readable SD2SP2 card for /pso-profiles.");
            ReadDirectory("sd:/pso-profiles");
        }
        else if (op == Operation.Asset)
        {
            stage = "Resolving asset recipe";
            AssetDefinition definition;
            if (path.EndsWith(".psoasset", StringComparison.OrdinalIgnoreCase))
            {
                using (var file = File.OpenRead(path))
                {
                    if (file.Length > 16384) throw new IOException("Recipe exceeds 16 KiB.");
                    // Disc recipes can live on SD2SP2 without modifying a retail
                    // disc. Their asset paths resolve against the mounted disc.
                    string assetDirectory = source.DiscMounted && path.StartsWith("sd:/", StringComparison.OrdinalIgnoreCase)
                        ? "dvd:/" : Path.GetDirectoryName(path) ?? source.Root;
                    using (var reader = new StreamReader(file)) definition = AssetDefinition.Parse(reader.ReadToEnd(), assetDirectory);
                }
            }
            else definition = AssetDefinition.ForModel(path, rawProfile);
            definition.ValidateIdentity(source.Identity);
            if (definition.IsDiscBound)
                using (var signature = File.OpenRead(definition.SignaturePath)) definition.ValidateSignature(signature);
            loadDetails = (definition.Recipe.Profile == PsoLoaderProfile.RigidGj ? "RIGID GJ" : "NINJA/REL/BML") + $"  BUDGET {definition.Recipe.NativeBudgetBytes:N0} BYTES";
            stage = "Loading " + definition.Recipe.ModelPath; status = stage;
            screen = Screen.Loading; Present();
            // Await immediately. No IsCompleted polling or synchronous busy wait.
            Model loaded = await PsoModelLoader.LoadAsync(definition.Recipe);
            ModelInstance created;
            try { created = loaded.CreateInstance(); }
            catch { loaded.Dispose(); throw; }
            ModelEffects createdEffects;
            try { createdEffects = new ModelEffects(); }
            catch { created.Dispose(); loaded.Dispose(); throw; }
            ReleaseModel(); model = loaded; instance = created; asset = definition; effects = createdEffects;
            orbit.Reset(definition.CenterX, definition.CenterY, definition.CenterZ, definition.Radius, ui.ModelAspect);
            camera = new Camera(60, ui.ModelAspect, MathF.Max(.001f, definition.Radius * .001f), MathF.Max(100, definition.Radius * 200));
            clip = -1; clipTime = 0; speedIndex = 2; paused = autoRotate = false;
            transform.LocalRotation = Quaternion.Identity;
            clock.Reset(); lastFrame = 0; statsDelay = 0; screen = Screen.Inspector;
            status = definition.IsDiscBound ? "Disc identity and content signature matched." : "Manual import; no verified disc profile.";
        }
    }
    private void ReadDirectory(string path)
    {
        stage = "Listing " + path; ClearItems();
        if (source.DiscMounted && path.TrimEnd('/') == source.Root.TrimEnd('/'))
            items.Add(new BrowserItem("RECIPES from SD2SP2 /pso-profiles", "", Operation.Profiles));
        var folders = new List<string>(); var files = new List<string>(); int count = 0;
        bool limited = false;
        foreach (var child in Directory.EnumerateDirectories(path))
        { if (++count > 256) { limited = true; break; } folders.Add(child); }
        foreach (var child in Directory.EnumerateFiles(path))
        { if (++count > 512) { limited = true; break; } files.Add(child); }
        folders.Sort(StringComparer.OrdinalIgnoreCase); files.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var child in folders) items.Add(new BrowserItem("DIR " + Path.GetFileName(child), child, Operation.Directory));
        foreach (var child in files)
        {
            string ext = Path.GetExtension(child).ToLowerInvariant();
            Operation action = ext == ".iso" || ext == ".gcm" ? Operation.Image :
                ext == ".psoasset" || ext == ".gj" || ext == ".nj" || ext == ".rel" || ext == ".bml" || ext == ".prs" ? Operation.Asset : Operation.None;
            items.Add(new BrowserItem((action == Operation.Asset ? "MODEL " : "FILE ") + Path.GetFileName(child), child, action));
        }
        directory = path; screen = Screen.Files;
        status = limited ? "Listing capped at 512 entries. Use smaller folders." :
            source.DiscMounted && path.StartsWith("sd:/", StringComparison.OrdinalIgnoreCase)
                ? "Recipe asset paths resolve against dvd:/. Disc identity is shown above."
                : "Select a recipe/model. No retail Booma profile is bundled.";
    }
    private void Update(ControllerState pad, float dt)
    {
        if (pad.Start.WasPressed) { quit = true; return; }
        if (screen == Screen.Error)
        {
            if (pad.X.WasPressed) Queue(retry, retryPath);
            if (pad.B.WasPressed) Queue(Operation.Refresh);
            return;
        }
        if (screen == Screen.Inspector && model != null && instance != null && asset != null)
        {
            if (pad.B.WasPressed) { ReleaseModel(); screen = Screen.Files; return; }
            if (pad.Y.WasPressed)
            {
                orbit.Reset(asset.CenterX, asset.CenterY, asset.CenterZ, asset.Radius, ui.ModelAspect);
                transform.LocalRotation = Quaternion.Identity;
            }
            if (pad.Z.WasPressed) autoRotate = !autoRotate;
            if (autoRotate) transform.Rotate(0, dt * 20, 0);
            // Reserve L as the effect modifier; no zoom while it is held.
            orbit.Update(dt, pad.Stick.X, -pad.Stick.Y, pad.CStick.X, pad.CStick.Y,
                pad.L.IsHeld ? 0 : pad.LeftTrigger - pad.RightTrigger);
            camera.LookAt(new Vector3(orbit.EyeX, orbit.EyeY, orbit.EyeZ),
                new Vector3(orbit.TargetX, orbit.TargetY, orbit.TargetZ), new Vector3(0, 1, 0));
            if (pad.DPadUp.WasPressed || pad.DPadDown.WasPressed)
            {
                int count = model.Animations.Count + 1;
                clip = (clip + 1 + (pad.DPadUp.WasPressed ? 1 : count - 1)) % count - 1;
                clipTime = 0; paused = false;
                if (clip < 0) instance.Animator.Stop();
                else { instance.Animator.Play(model.Animations[clip].Name, loop: true); instance.Animator.SetSpeed(speeds[speedIndex]); }
            }
            if (pad.A.WasPressed && clip >= 0)
            { paused = !paused; if (paused) instance.Animator.Pause(); else instance.Animator.Resume(); }
            if (pad.X.WasPressed)
            {
                if (pad.L.IsHeld) effects?.Next();
                else { speedIndex = (speedIndex + 1) % speeds.Length; instance.Animator.SetSpeed(speeds[speedIndex]); }
            }
            if (clip >= 0)
            {
                float duration = model.Animations[clip].DurationSeconds;
                if (pad.DPadLeft.WasPressed || pad.DPadRight.WasPressed)
                {
                    clipTime = Math.Clamp(clipTime + (pad.DPadRight.WasPressed ? .25f : -.25f), 0, duration);
                    instance.Animator.Seek(clipTime);
                }
                instance.Update(dt);
                if (!paused && duration > 0) clipTime = (clipTime + dt * speeds[speedIndex]) % duration;
            }
            statsDelay -= dt;
            if (statsDelay <= 0)
            {
                statistics = $"NODES {model.NodeCount} M {model.NativeBytes:N0} I {instance.NativeBytes:N0} B";
                playback = clip < 0 ? "REST POSE" : $"{model.Animations[clip].Name}  {clipTime:F2}/{model.Animations[clip].DurationSeconds:F2}S  {speeds[speedIndex]:F2}X" + (paused ? " PAUSED" : "");
                frameMetrics = $"FRAME {frameMilliseconds:F1} MS";
                statsDelay = .25f;
            }
            return;
        }
        if (screen != Screen.Files && screen != Screen.Sources) return;
        if (pad.DPadDown.WasPressed && selection + 1 < items.Count) selection++;
        if (pad.DPadUp.WasPressed && selection > 0) selection--;
        if (selection < top) top = selection;
        if (selection >= top + 9) top = selection - 8;
        if (pad.A.WasPressed && items.Count != 0)
        {
            var item = items[selection];
            if (item.Operation != Operation.None) Queue(item.Operation, item.Path);
            else status = "Choose a model or .psoasset recipe. Texture/archive internals use recipes.";
        }
        if (screen == Screen.Sources && pad.X.WasPressed) Queue(Operation.Refresh);
        if (screen == Screen.Files)
        {
            if (pad.X.WasPressed) rawProfile = rawProfile == PsoLoaderProfile.RigidGj ? PsoLoaderProfile.NinjaRel : PsoLoaderProfile.RigidGj;
            if (pad.B.WasPressed)
            {
                if (source.DiscMounted && (directory.TrimEnd('/') == "sd:/pso-profiles" || directory.TrimEnd('/') == "sd:")) Queue(Operation.Directory, source.Root);
                else if (directory.TrimEnd('/') == source.Root.TrimEnd('/')) Queue(Operation.Refresh);
                else Queue(Operation.Directory, Path.GetDirectoryName(directory.TrimEnd('/')) ?? source.Root);
            }
            if (pad.Z.WasPressed) Queue(Operation.Refresh);
        }
    }
    private void Present()
    {
        ui.Begin();
        if (screen == Screen.Inspector && instance != null)
        {
            ui.ModelViewport();
            renderer.Begin3D(camera);
            try { renderer.DrawModel(instance, transform, effects?.Current); }
            finally { renderer.End3D(); ui.FullViewport(); }
        }
        ui.BeginText();
        ui.Line("DOLPHIN ASSET BROWSER", 0, true);
        if (screen != Screen.Inspector) ui.Banner();
        if (screen == Screen.Inspector)
        {
            ui.InspectorLine(sourceTitle, 2);
            if (discMetadata.Length != 0) ui.InspectorLine(discMetadata, 3);
        }
        else
        {
            ui.Line(sourceTitle, 3);
            if (discMetadata.Length != 0) ui.Line(discMetadata, 4);
        }
        if (screen == Screen.Error)
        {
            ui.Line("OPERATION FAILED", 6, true); ui.Line(loadDetails, 7); ui.Line(stage, 8); ui.Line(retryPath, 9);
            for (int i = 0; i < 5 && i * 70 < error.Length; i++)
                ui.Line(error.Substring(i * 70, Math.Min(70, error.Length - i * 70)), 11 + i);
            ui.Line("X RETRY   B SOURCES   START EXIT", 20, true);
        }
        else if (screen == Screen.Loading)
        { ui.Line("LOADING - AWAITING SDK", 8, true); ui.Line(status, 10); ui.Line("SOURCE REMAINS MOUNTED UNTIL COMPLETION", 14); }
        else if (screen == Screen.Inspector && asset != null)
        {
            ui.InspectorLine(asset.Name, 4, true); ui.InspectorLine(statistics, 5); ui.InspectorLine(playback, 6);
            ui.InspectorLine(loadDetails, 7);
            ui.InspectorLine(frameMetrics, 8); ui.InspectorLine(status, 9);
            ui.InspectorLine("EFFECT: " + (effects?.Name ?? "ORIGINAL"), 10, true);
            ui.InspectorPath(asset.Recipe.TexturePath ?? "NO EXTERNAL TEXTURE ARCHIVE", 11);
            ui.InspectorPath(asset.Recipe.MotionPath ?? "NO EXTERNAL MOTION", 13);
            ui.InspectorPath(asset.Recipe.ModelPath, 15);
            ui.InspectorLine("STICK ORBIT  C-STICK PAN", 17);
            ui.InspectorLine("L/R ZOOM  Y RESET  Z ROTATE", 18);
            ui.InspectorLine("UP/DOWN CLIP  A PAUSE  X SPEED", 19);
            ui.InspectorLine("LEFT/RIGHT SEEK  HOLD L + X EFFECT", 20);
            ui.InspectorLine("B FILES  START EXIT", 21, true);
        }
        else
        {
            ui.Line(screen == Screen.Sources ? "SOURCES" : directory, 6, true);
            for (int row = 0; row < 9 && top + row < items.Count; row++)
                ui.Line(items[top + row].Label, 8 + row, top + row == selection);
            ui.Line(status, 17);
            ui.Line(screen == Screen.Sources ? "A OPEN  UP/DOWN SELECT  X RESCAN  START EXIT" : "A OPEN  B PARENT  Z SOURCES  START EXIT", 19);
            if (screen == Screen.Files) ui.Line(rawProfile == PsoLoaderProfile.RigidGj ? "X LOADER: RIGID GJ   RECIPES OVERRIDE THIS" : "X LOADER: NINJA/REL/BML   RECIPES OVERRIDE THIS", 20, true);
        }
        ui.End();
    }
    private void ReleaseModel()
    {
        if (effects != null) { effects.Dispose(); effects = null; }
        if (instance != null) { instance.Dispose(); instance = null; }
        if (model != null) { model.Dispose(); model = null; }
        asset = null;
    }
    private void ReleaseProfiles()
    { if (profilesMounted) { Storage.Unmount(StorageDevice.SdSerialPort2); profilesMounted = false; } }
    public void Dispose() { ReleaseModel(); source.Dispose(); ReleaseProfiles(); ui.Dispose(); }
}
internal static class Program
{
    private static async Task<int> Main()
    {
        using (var app = new BrowserApp()) return await app.RunAsync();
    }
}
