using System;

namespace DolphinAssetBrowser.Browser;

public sealed class OrbitCamera
{
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public float Distance { get; private set; } = 3;
    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public float TargetZ { get; private set; }
    public float Radius { get; private set; } = 1;
    public float EyeX => TargetX + Distance * MathF.Cos(Pitch) * MathF.Sin(Yaw);
    public float EyeY => TargetY + Distance * MathF.Sin(Pitch);
    public float EyeZ => TargetZ + Distance * MathF.Cos(Pitch) * MathF.Cos(Yaw);

    public void Reset(float x = 0, float y = 0, float z = 0, float radius = 1)
    {
        if (MathF.Abs(x) > 1000000 || MathF.Abs(y) > 1000000 || MathF.Abs(z) > 1000000 || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !(radius >= .001f && radius <= 10000))
            throw new ArgumentException("Invalid camera bounds.");
        TargetX = x; TargetY = y; TargetZ = z; Radius = radius;
        Yaw = 0; Pitch = .2f; Distance = radius * 3;
    }
    public void Update(float dt, float orbitX, float orbitY, float panX, float panY, float zoom)
    {
        if (!(dt >= 0 && dt <= 1)) throw new ArgumentOutOfRangeException("dt");
        Yaw = (Yaw + orbitX * dt * 2) % (MathF.PI * 2);
        Pitch = Math.Clamp(Pitch + orbitY * dt * 2, -1.45f, 1.45f);
        Distance = Math.Clamp(Distance + zoom * Distance * dt * 2, Radius * .05f, Radius * 100);
        float shift = Distance * dt * .5f;
        TargetX += panX * MathF.Cos(Yaw) * shift;
        TargetZ -= panX * MathF.Sin(Yaw) * shift;
        TargetY += panY * shift;
    }
}
