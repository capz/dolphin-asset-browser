using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Storage;
using DolphinAssetBrowser.Pso;
using DolphinAssetBrowser.Browser;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static async Task<int> Main()
    {
        if (AssetPaths.Resolve("dvd:/assets/", "../a.bml#model.gj") != "dvd:/a.bml#model.gj") return 6;
        var definition = AssetDefinition.Parse("model=a.gj\nradius=2\ncentery=1", "dvd:/assets/");
        if (definition.Recipe.ModelPath != "dvd:/assets/a.gj" || definition.Radius != 2) return 7;
        var orbit = new OrbitCamera(); orbit.Reset(0, 1, 0, 2);
        orbit.Update(.1f, 1, 100, 0, 0, -1000);
        if (!(orbit.Pitch <= 1.45f && orbit.Distance >= .1f && float.IsFinite(orbit.EyeX))) return 8;
        if (!Storage.Mount(StorageDevice.SdSerialPort2)) return 1;
        try
        {
            var images = ImageDiscovery.Scan("sd:/games");
            if (images.Images.Count != 1 || images.Images[0].Identity.GameId != "TEST01") return 9;
            var manual = AssetDefinition.ForModel("sd:/textured.gj", PsoLoaderProfile.RigidGj);
            if (manual.Recipe.TexturePath != "sd:/textured.gvm") return 10;
            using (var model = await PsoModelLoader.LoadAsync(new PsoAssetRecipe(
                "sd:/textured.gj", "sd:/preview.gvm", "sd:/move.njm", motionFramesPerSecond: 30)))
            using (var instance = model.CreateInstance())
            {
                if (!Scheduler.IsApplicationThread || model.NodeCount != 1 || model.Animations.Count != 1) return 2;
                instance.Animator.Play("Motion"); instance.Update(.5f);
                float[] identity = new float[16]; identity[0] = identity[5] = identity[10] = identity[15] = 1;
                var renderer = new Renderer();
                using (var effects = new ModelEffects())
                {
                    if (effects.Current != null || effects.Name != "ORIGINAL") return 11;
                    var transform = new Transform();
                    renderer.BeginFrame(identity, identity);
                    renderer.DrawModel(instance, transform, effects.Current);
                    effects.Next();
                    if (effects.Current == null || effects.Current.RequiresTexture || effects.Current.StageCount != 2) return 12;
                    renderer.DrawModel(instance, transform, effects.Current);
                    effects.Next();
                    if (effects.Current == null || effects.Current.RequiresTexture || effects.Current.StageCount != 7) return 13;
                    renderer.DrawModel(instance, identity, effects.Current);
                    effects.Next();
                    if (effects.Current != null) return 14;
                    renderer.DrawModel(instance, identity, effects.Current);
                    renderer.EndFrame();
                    effects.Next(); effects.Reset();
                    if (effects.Selection != 0) return 15;
                    effects.Dispose(); effects.Dispose();
                    try { effects.Next(); return 16; }
                    catch (ObjectDisposedException) { }
                }
            }
            using (var extended = await PsoModelLoader.LoadAsync(new PsoAssetRecipe(
                "sd:/preview.bml#preview.gj", profile: PsoLoaderProfile.NinjaRel)))
            using (var instance = extended.CreateInstance())
            using (var effects = new ModelEffects())
            {
                if (extended.NodeCount != 1) return 3;
                effects.Next();
                var renderer = new Renderer(); var camera = new Camera();
                renderer.BeginFrame(camera); renderer.DrawModel(instance, new Transform(), effects.Current); renderer.EndFrame();
            }
            // Both presets must also accept an untextured imported model.
            using (var plain = await PsoModelLoader.LoadAsync(new PsoAssetRecipe("sd:/plain.gj")))
            using (var instance = plain.CreateInstance())
            using (var effects = new ModelEffects())
            {
                var renderer = new Renderer(); var camera = new Camera(); var transform = new Transform();
                renderer.BeginFrame(camera);
                effects.Next(); renderer.DrawModel(instance, transform, effects.Current);
                effects.Next(); renderer.DrawModel(instance, transform, effects.Current);
                renderer.DrawModel(instance, transform, null); renderer.EndFrame();
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try
                {
                    using (var unexpected = await PsoModelLoader.LoadAsync(
                        new PsoAssetRecipe("sd:/plain.gj"), cancellation.Token)) { }
                    return 4;
                }
                catch (OperationCanceledException) { }
            }
            // No automatic downgrade to an untextured preview on missing textures.
            try
            {
                using (var unexpected = await PsoModelLoader.LoadAsync(new PsoAssetRecipe("sd:/textured.gj"))) { }
                return 5;
            }
            catch (System.IO.IOException) { }
            return 0;
        }
        finally { Storage.Unmount(StorageDevice.SdSerialPort2); }
    }
}
