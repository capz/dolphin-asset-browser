using System;
using Dolphin.Graphics;

internal sealed class BrowserUi : IDisposable
{
    private readonly GraphicsDevice device = GraphicsDevice.Default;
    private readonly Draw2D draw;
    private readonly SampleText text;
    private Texture2D? banner;
    internal BrowserUi()
    {
        draw = new Draw2D(device, new Draw2DOptions { Width = 640, Height = 480 });
        try { text = new SampleText(); } catch { draw.Dispose(); throw; }
    }
    internal void SetBanner(byte[]? pixels)
    {
        if (banner != null) { banner.Dispose(); banner = null; }
        if (pixels != null) banner = Texture2D.Create(96, 32, pixels);
    }
    internal void Begin() => device.BeginFrame(new Color32(15, 24, 36));
    internal void BeginText() => draw.Begin();
    internal void Line(string value, int row, bool highlighted = false)
    {
        if (value.Length > 72) value = value.Substring(0, 69) + "...";
        text.Draw(draw, value, 18, 24 + row * 21, 2,
            highlighted ? new Color32(255, 213, 110) : Color32.White);
    }
    internal void Banner()
    {
        if (banner != null) draw.DrawTexture(banner, new RectF(430, 24, 192, 64), Color32.White);
    }
    internal void End() { draw.End(); device.EndFrame(); }
    public void Dispose()
    {
        if (banner != null) banner.Dispose();
        text.Dispose(); draw.Dispose();
    }
}
