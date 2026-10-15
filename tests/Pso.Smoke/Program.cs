using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.Storage;
using DolphinAssetBrowser.Pso;
using Scheduler = Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static async Task<int> Main()
    {
        if (!Storage.Mount(StorageDevice.SdSerialPort2)) return 1;
        try
        {
            using (var model = await PsoModelLoader.LoadAsync(new PsoAssetRecipe(
                "sd:/textured.gj", "sd:/preview.gvm", "sd:/move.njm", motionFramesPerSecond: 30)))
            using (var instance = model.CreateInstance())
            {
                if (!Scheduler.IsApplicationThread || model.NodeCount != 1 || model.Animations.Count != 1) return 2;
                instance.Animator.Play("Motion"); instance.Update(.5f);
                float[] identity = new float[16]; identity[0] = identity[5] = identity[10] = identity[15] = 1;
                var renderer = new Renderer();
                renderer.BeginFrame(identity, identity); renderer.DrawModel(instance, identity); renderer.EndFrame();
            }
            using (var extended = await PsoModelLoader.LoadAsync(new PsoAssetRecipe(
                "sd:/preview.bml#preview.gj", profile: PsoLoaderProfile.NinjaRel)))
            using (var instance = extended.CreateInstance())
                if (extended.NodeCount != 1) return 3;
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
