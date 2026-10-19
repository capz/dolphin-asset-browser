using System;
using System.IO;
using System.Collections.Generic;
using Dolphin.Storage;
using DolphinAssetBrowser.Pso;

namespace DolphinAssetBrowser.Browser;

/// <summary>A user-reviewable recipe, optionally tied to exact disc identity
/// and a byte signature. It is not a claim of a verified retail Booma profile.</summary>
public sealed class AssetDefinition
{
    public string Name { get; private set; } = "Model";
    public PsoAssetRecipe Recipe { get; private set; } = null!;
    public string GameId { get; private set; } = "";
    public int DiscNumber { get; private set; } = -1;
    public int Revision { get; private set; } = -1;
    public string SignaturePath { get; private set; } = "";
    public string SignatureHex { get; private set; } = "";
    public bool HasBounds { get; private set; }
    public float CenterX { get; private set; }
    public float CenterY { get; private set; }
    public float CenterZ { get; private set; }
    public float Radius { get; private set; } = 1;
    public bool IsDiscBound => GameId.Length != 0;

    public static AssetDefinition ForModel(string path, PsoLoaderProfile profile)
    {
        string? texture = null;
        string file = Path.GetFileName(path); int dot = file.LastIndexOf('.');
        string sidecar = Path.Combine(Path.GetDirectoryName(path) ?? "", (dot < 0 ? file : file.Substring(0, dot)) + ".gvm");
        if (File.Exists(sidecar)) texture = sidecar;
        var result = new AssetDefinition();
        result.Name = Path.GetFileName(path);
        result.Recipe = new PsoAssetRecipe(path, texture, profile: profile);
        return result;
    }
    public static AssetDefinition Parse(string text, string directory)
    {
        if (text.Length > 16384) throw new ArgumentException("Recipe exceeds 16 KiB.");
        var result = new AssetDefinition(); var seen = new List<string>();
        string model = "", texture = "", motion = "";
        var profile = PsoLoaderProfile.RigidGj; float fps = 0; int keys = 16, budget = 8 * 1024 * 1024;
        string[] lines = text.Split(new char[] {'\n'}, StringSplitOptions.None);
        if (lines.Length > 64) throw new ArgumentException("Recipe exceeds 64 lines.");
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int at = line.IndexOf('=');
            if (at < 1) throw new ArgumentException("Recipe lines must use key=value.");
            string key = line.Substring(0, at).Trim().ToLowerInvariant();
            string value = line.Substring(at + 1).Trim();
            if (seen.Contains(key) || value.Length == 0) throw new ArgumentException("Duplicate or empty recipe field: " + key);
            seen.Add(key);
            if (key == "name") result.Name = value;
            else if (key == "model") model = AssetPaths.Resolve(directory, value);
            else if (key == "texture") texture = AssetPaths.Resolve(directory, value);
            else if (key == "motion") motion = AssetPaths.Resolve(directory, value);
            else if (key == "profile")
            {
                if (value == "rigid") profile = PsoLoaderProfile.RigidGj;
                else if (value == "ninja") profile = PsoLoaderProfile.NinjaRel;
                else throw new ArgumentException("Profile must be rigid or ninja.");
            }
            else if (key == "fps") fps = Number(value);
            else if (key == "eulerkeybytes") keys = int.Parse(value);
            else if (key == "nativebudgetbytes") budget = int.Parse(value);
            else if (key == "gameid") result.GameId = value;
            else if (key == "discnumber") result.DiscNumber = int.Parse(value);
            else if (key == "revision") result.Revision = int.Parse(value);
            else if (key == "signaturepath") result.SignaturePath = AssetPaths.Resolve(directory, value);
            else if (key == "signaturehex") result.SignatureHex = value;
            else if (key == "centerx") result.CenterX = Number(value);
            else if (key == "centery") result.CenterY = Number(value);
            else if (key == "centerz") result.CenterZ = Number(value);
            else if (key == "radius") { result.Radius = Number(value); result.HasBounds = true; }
            else throw new ArgumentException("Unknown recipe field: " + key);
        }
        if (model.Length == 0) throw new ArgumentException("Recipe requires a model path.");
        if (!(result.Radius >= .001f && result.Radius <= 10000)) throw new ArgumentException("Radius must be between 0.001 and 10000.");
        if (MathF.Abs(result.CenterX) > 1000000 || MathF.Abs(result.CenterY) > 1000000 || MathF.Abs(result.CenterZ) > 1000000)
            throw new ArgumentException("Camera center exceeds supported range.");
        if (seen.Contains("centerx") || seen.Contains("centery") || seen.Contains("centerz"))
            if (!result.HasBounds) throw new ArgumentException("A supplied center also requires radius.");
        bool anyDisc = seen.Contains("gameid") || seen.Contains("discnumber") || seen.Contains("revision") ||
            seen.Contains("signaturepath") || seen.Contains("signaturehex");
        if (anyDisc && (result.GameId.Length != 6 || result.DiscNumber < 0 || result.DiscNumber > 255 ||
            result.Revision < 0 || result.Revision > 255 || result.SignaturePath.Length == 0 || result.SignatureHex.Length == 0))
            throw new ArgumentException("Disc-bound recipes require gameId, discNumber, revision, signaturePath and signatureHex.");
        if (anyDisc)
        {
            if (result.SignaturePath.IndexOf('#') >= 0 || result.SignatureHex.Length > 128 || result.SignatureHex.Length % 2 != 0)
                throw new ArgumentException("Signature must be 1..64 bytes from a regular file.");
            for (int i = 0; i < result.SignatureHex.Length; i++) Hex(result.SignatureHex[i]);
        }
        result.Recipe = new PsoAssetRecipe(model, texture.Length == 0 ? null : texture,
            motion.Length == 0 ? null : motion, profile, fps, keys, budget);
        return result;
    }
    private static float Number(string value)
    {
        float number = float.Parse(value);
        if (!float.IsFinite(number)) throw new ArgumentException("Recipe numbers must be finite.");
        return number;
    }
    private static int Hex(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        throw new ArgumentException("Invalid signature hex digit.");
    }
    public void ValidateIdentity(DiscIdentity? identity)
    {
        if (!IsDiscBound) return;
        if (identity == null || identity.GameId != GameId || identity.DiscNumber != DiscNumber || identity.Revision != Revision)
            throw new InvalidOperationException("Disc identity does not match this asset recipe.");
    }
    public void ValidateSignature(Stream input)
    {
        for (int i = 0; i < SignatureHex.Length; i += 2)
        {
            int actual = input.ReadByte();
            if (actual != Hex(SignatureHex[i]) * 16 + Hex(SignatureHex[i + 1]))
                throw new IOException("Disc content signature does not match the recipe.");
        }
    }
}
