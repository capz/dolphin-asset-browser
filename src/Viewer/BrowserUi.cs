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
    internal float ModelAspect => (device.Width - device.Width * 2 / 3) / (float)device.Height;
    internal void ModelViewport()
    {
        int x = device.Width * 2 / 3;
        device.SetViewport(x, 0, device.Width - x, device.Height);
        device.SetScissor(x, 0, device.Width - x, device.Height);
    }
    internal void FullViewport()
    {
        device.SetViewport(0, 0, device.Width, device.Height);
        device.SetScissor(0, 0, device.Width, device.Height);
    }
    internal void InspectorLine(string value, int row, bool highlighted = false)
    {
        if (value.Length > 48) value = value.Substring(0, 45) + "...";
        Line(value, row, highlighted);
    }
    internal void InspectorPath(string value, int row)
    {
        InspectorLine(value.Length > 48 ? value.Substring(0, 48) : value, row);
        if (value.Length > 48) InspectorLine(value.Substring(48), row + 1);
    }
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
