using System;
using System.IO;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;
using Dolphin.Timing;
using DolphinAssetBrowser.Pso;

// Diagnostic entry point. A verified disc profile will eventually supply the
// recipe; automatic PSO disc discovery is not implemented by this harness.
internal static class Program
{
    private static async Task<int> Main()
    {
        if (!Storage.Mount(StorageDevice.GcLoader))
        {
            DebugOverlay.WriteLine("GC Loader unavailable. Preview requires gcloader:/preview.gj");
            return 1;
        }
        try
        {
            string? textures = File.Exists("gcloader:/preview.gvm") ? "gcloader:/preview.gvm" : null;
            var recipe = new PsoAssetRecipe("gcloader:/preview.gj", texturePath: textures);
            var camera = new Camera(60, 4f / 3f);
            camera.LookAt(new Vector3(0, 2, 5), Vector3.Zero, new Vector3(0, 1, 0));
            var renderer = new Renderer();
            var transform = new Transform();
            DebugOverlay.WriteLine("Loading PSO through SDK...");
            renderer.BeginFrame(camera);
            renderer.EndFrame();
            // Await immediately: do not poll before giving the load a chance
            // to suspend this application flow and dispatch its completion.
            using (var model = await PsoModelLoader.LoadAsync(recipe))
            using (var instance = model.CreateInstance())
            {
                DebugOverlay.WriteLine("SDK PSO preview. B or Start exits.");
                if (model.TryGetAnimation("Motion", out var motion))
                    instance.Animator.Play(motion.Name, loop: true);
                long previous = Clock.Timestamp;
                while (true)
                {
                    Controller.Update();
                    var state = Controller.GetState();
                    if (state.B.WasPressed || state.Start.WasPressed) break;
                    long now = Clock.Timestamp;
                    float delta = (float)(now - previous) / 1000000f;
                    previous = now;
                    if (delta > .1f) delta = .1f;
                    instance.Update(delta);
                    renderer.BeginFrame(camera);
                    renderer.DrawModel(instance, transform);
                    renderer.EndFrame();
                }
            }
            return 0;
        }
        catch (Exception error) { DebugOverlay.WriteLine(error.Message); return 1; }
        finally { Storage.Unmount(StorageDevice.GcLoader); }
    }
}
