using System;
using Dolphin.Graphics;

namespace DolphinAssetBrowser.Pso;

/// <summary>Owns reusable, raster-driven inspection effects. Create between frames
/// on the application executor; drawing borrows the selected effect.</summary>
public sealed class ModelEffects : IDisposable
{
    private readonly GxEffect ramp;
    private readonly GxEffect toon;
    private bool disposed;
    public int Selection { get; private set; }
    public string Name => Selection == 1 ? "COLOR RAMP" : Selection == 2 ? "TOON" : "ORIGINAL";
    public GxEffect? Current
    {
        get { Check(); return Selection == 1 ? ramp : Selection == 2 ? toon : null; }
    }

    public ModelEffects()
    {
        // Match the existing dark background and warm inspector highlight.
        ramp = GxEffect.ColorRamp(new Color32(15, 24, 36), new Color32(255, 213, 110));
        try
        {
            toon = GxEffect.ToonBands(65, 165, new Color32(15, 24, 36),
                new Color32(135, 119, 84), new Color32(255, 213, 110));
        }
        catch { ramp.Dispose(); throw; }
    }

    public void Next() { Check(); Selection = (Selection + 1) % 3; }
    public void Reset() { Check(); Selection = 0; }
    private void Check() { if (disposed) throw new ObjectDisposedException("ModelEffects"); }
    public void Dispose()
    {
        if (disposed) return;
        toon.Dispose(); ramp.Dispose(); disposed = true;
    }
}
