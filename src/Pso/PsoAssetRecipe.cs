using System;

namespace DolphinAssetBrowser.Pso;

public enum PsoLoaderProfile { RigidGj, NinjaRel }

/// <summary>Project-owned asset selection, not a binary format implementation.
/// A verified disc profile will supply these paths and motion settings.</summary>
public sealed class PsoAssetRecipe
{
    public PsoLoaderProfile Profile { get; }
    public string ModelPath { get; }
    public string? TexturePath { get; }
    public string? MotionPath { get; }
    public float MotionFramesPerSecond { get; }
    public int EulerKeyBytes { get; }
    public int NativeBudgetBytes { get; }
    public bool AllowMaterialApproximation { get; }

    public PsoAssetRecipe(string modelPath, string? texturePath = null,
        string? motionPath = null, PsoLoaderProfile profile = PsoLoaderProfile.RigidGj,
        float motionFramesPerSecond = 0, int eulerKeyBytes = 16,
        int nativeBudgetBytes = 8 * 1024 * 1024, bool allowMaterialApproximation = false)
    {
        if (modelPath == null) throw new ArgumentNullException("modelPath");
        if (modelPath.Length == 0 || texturePath != null && texturePath.Length == 0 ||
            motionPath != null && motionPath.Length == 0)
            throw new ArgumentException("Asset paths cannot be empty.");
        if (profile != PsoLoaderProfile.RigidGj && profile != PsoLoaderProfile.NinjaRel)
            throw new ArgumentOutOfRangeException("profile");
        if (nativeBudgetBytes < 1024 || nativeBudgetBytes > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException("nativeBudgetBytes");
        if (eulerKeyBytes != 8 && eulerKeyBytes != 16)
            throw new ArgumentOutOfRangeException("eulerKeyBytes");
        if (motionPath != null && !(motionFramesPerSecond > 0 && motionFramesPerSecond <= 240))
            throw new ArgumentOutOfRangeException("motionFramesPerSecond");
        if (profile == PsoLoaderProfile.RigidGj && allowMaterialApproximation)
            throw new ArgumentException("Rigid GJ import does not support material approximation.");
        Profile = profile; ModelPath = modelPath; TexturePath = texturePath;
        MotionPath = motionPath; MotionFramesPerSecond = motionFramesPerSecond;
        EulerKeyBytes = eulerKeyBytes; NativeBudgetBytes = nativeBudgetBytes;
        AllowMaterialApproximation = allowMaterialApproximation;
    }
}
