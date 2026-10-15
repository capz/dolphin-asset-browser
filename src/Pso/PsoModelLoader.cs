using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;

namespace DolphinAssetBrowser.Pso;

/// <summary>Thin SDK adapter. No probing by repeated parse attempts, format
/// conversion, or fallback that silently removes textures.</summary>
public static class PsoModelLoader
{
    public static Task<Model> LoadAsync(PsoAssetRecipe recipe, CancellationToken cancellationToken = default)
    {
        if (recipe == null) throw new ArgumentNullException("recipe");
        if (recipe.Profile == PsoLoaderProfile.RigidGj)
            return Model.LoadPsoAsync(recipe.ModelPath, recipe.TexturePath, recipe.MotionPath,
                recipe.MotionFramesPerSecond, recipe.EulerKeyBytes, cancellationToken, recipe.NativeBudgetBytes);
        // The SDK's Episode III adapter also handles verified GJ/NJ/REL/BML
        // layouts. The recipe chooses a structural profile, not a title guess.
        return Model.LoadPsoEpisode3Async(recipe.ModelPath, recipe.TexturePath, recipe.MotionPath,
            recipe.MotionPath == null ? 30 : recipe.MotionFramesPerSecond,
            recipe.EulerKeyBytes, cancellationToken, recipe.NativeBudgetBytes, recipe.AllowMaterialApproximation);
    }
}
