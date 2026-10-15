using System.Buffers.Binary;
using DolphinAssetBrowser.Pso;

static class Tests
{
    static int passed;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); passed++; }
    static void U(byte[] b, int p, uint value) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p), value);
    static void S(byte[] b, int p, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(p), value);
    static uint Read(byte[] b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p));
    static byte[] Fixture(bool mixed = false, bool strip = false, bool texture = false)
    {
        byte[] payload = new byte[400];
        U(payload, 0, 7); U(payload, 4, 52);
        U(payload, 52, 88); U(payload, 60, 160); S(payload, 68, 1);
        payload[88] = 1; payload[89] = 12; S(payload, 90, 4);
        U(payload, 96, 256); U(payload, 100, 48);
        if (mixed)
        {
            payload[104] = 5; payload[105] = 4; S(payload, 106, 4);
            U(payload, 112, 304); U(payload, 116, 16); payload[120] = 255;
            S(payload, 308, 255); S(payload, 314, 255);
        }
        else payload[104] = 255;
        U(payload, 160, 176); U(payload, 164, texture ? 2u : 1u);
        U(payload, 168, 208);
        int n = strip ? 4 : 3;
        U(payload, 172, (uint)(3 + n * (mixed ? 3 : 1)));
        payload[176] = 1; U(payload, 180, mixed ? 0xc08u : 8u);
        if (texture) { payload[184] = 8; U(payload, 188, 0); }
        payload[208] = strip ? (byte)0x98 : (byte)0x90; S(payload, 209, (ushort)n);
        for (int i = 0; i < n; i++)
        {
            int p = 211 + i * (mixed ? 3 : 1); payload[p] = (byte)i;
            if (mixed) S(payload, p + 1, (ushort)i);
        }
        U(payload, 268, 0x3f800000); // vertex 1 X = 1
        U(payload, 284, 0x3f800000); // vertex 2 Y = 1
        U(payload, 292, 0x3f800000); U(payload, 296, 0x3f800000);
        byte[] file = new byte[408];
        "GJCM"u8.CopyTo(file); BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), payload.Length);
        payload.CopyTo(file, 8); return file;
    }
    static byte[] Texture()
    {
        byte[] archive = new byte[94];
        "GVMH"u8.CopyTo(archive); BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(4), 6);
        S(archive, 10, 1); "GVRT"u8.CopyTo(archive.AsSpan(14));
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(18), 72);
        archive[25] = 6; S(archive, 26, 4); S(archive, 28, 4);
        for (int i = 0; i < 16; i++)
        { archive[30 + i * 2] = 255; archive[31 + i * 2] = 230; archive[62 + i * 2] = 40; archive[63 + i * 2] = 10; }
        return archive;
    }
    static byte[] Prs(byte[] source)
    {
        var bytes = new List<byte>(); int control = -1, bits = 8;
        void Bit(int value)
        {
            if (bits == 8) { control = bytes.Count; bytes.Add(0); bits = 0; }
            bytes[control] |= (byte)(value << bits++);
        }
        foreach (byte b in source) { Bit(1); bytes.Add(b); }
        Bit(0); Bit(1); bytes.Add(0); bytes.Add(0); // PRS terminator
        return bytes.ToArray();
    }
    static byte[] Bml(byte[] model, byte[] texture)
    {
        byte[] m = Prs(model), t = Prs(texture);
        int textureOffset = (2048 + m.Length + 31) & ~31;
        byte[] archive = new byte[(textureOffset + t.Length + 31) & ~31];
        U(archive, 4, 1); "preview.gj"u8.CopyTo(archive.AsSpan(64));
        U(archive, 96, (uint)m.Length); U(archive, 104, (uint)model.Length);
        U(archive, 108, (uint)t.Length); U(archive, 112, (uint)texture.Length);
        m.CopyTo(archive, 2048); t.CopyTo(archive, textureOffset); return archive;
    }
    static void InvalidRecipe(Action action)
    {
        try { action(); }
        catch (ArgumentException) { passed++; return; }
        throw new Exception("Invalid recipe accepted");
    }
    static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply fixture output directory.");
        var recipe = new PsoAssetRecipe("dvd:/model.gj", "dvd:/model.gvm");
        Check(recipe.Profile == PsoLoaderProfile.RigidGj && recipe.NativeBudgetBytes == 8 * 1024 * 1024, "strict defaults");
        Check(!recipe.AllowMaterialApproximation && recipe.MotionPath == null, "no implicit texture loss/motion");
        var extended = new PsoAssetRecipe("dvd:/npc.bml#preview.gj", profile: PsoLoaderProfile.NinjaRel);
        Check(extended.ModelPath == "dvd:/npc.bml#preview.gj", "preserve SDK selector");
        InvalidRecipe(() => new PsoAssetRecipe(""));
        InvalidRecipe(() => new PsoAssetRecipe("a", texturePath: ""));
        InvalidRecipe(() => new PsoAssetRecipe("a", profile: (PsoLoaderProfile)99));
        InvalidRecipe(() => new PsoAssetRecipe("a", nativeBudgetBytes: 0));
        InvalidRecipe(() => new PsoAssetRecipe("a", nativeBudgetBytes: 17 * 1024 * 1024));
        InvalidRecipe(() => new PsoAssetRecipe("a", eulerKeyBytes: 12));
        InvalidRecipe(() => new PsoAssetRecipe("a", motionPath: "motion"));
        InvalidRecipe(() => new PsoAssetRecipe("a", motionPath: "motion", motionFramesPerSecond: float.NaN));
        InvalidRecipe(() => new PsoAssetRecipe("a", allowMaterialApproximation: true));
        string sd = Path.Combine(args[0], "sd"); Directory.CreateDirectory(sd);
        void Save(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(sd, name), bytes);
        Save("plain.gj", Fixture()); Save("mixed.gj", Fixture(mixed: true));
        Save("strip.gj", Fixture(strip: true));
        byte[] fan = Fixture(strip: true); fan[8 + 208] = 0xa0; Save("fan.gj", fan);
        Save("textured.gj", Fixture(mixed: true, texture: true)); Save("preview.gvm", Texture());
        Save("preview.bml", Bml(Fixture(mixed: true, texture: true), Texture()));
        byte[] motion = new byte[60]; "NMDM"u8.CopyTo(motion);
        BinaryPrimitives.WriteInt32LittleEndian(motion.AsSpan(4), 52);
        U(motion, 8, 12); U(motion, 12, 30); S(motion, 16, 1); S(motion, 18, 1);
        U(motion, 20, 20); U(motion, 24, 2); U(motion, 44, 30); U(motion, 48, 0x40000000);
        Save("move.njm", motion);
        byte[] rotated = Fixture(); U(rotated, 8, 0); U(rotated, 8 + 28, 16384);
        U(rotated, 8 + 32, 0x3f800000); U(rotated, 8 + 36, 0x3f800000); U(rotated, 8 + 40, 0x3f800000);
        Save("rotated.gj", rotated);
        byte[] bad = Fixture(); U(bad, 8 + 4, 399); Save("bad-pointer.gj", bad);
        bad = Fixture(); bad[8 + 211] = 9; Save("bad-index.gj", bad);
        bad = Fixture(); U(bad, 8 + 256, 0x7fc00000); Save("nan.gj", bad);
        bad = Fixture(); bad[8 + 208] = 0xff; Save("bad-primitive.gj", bad);
        bad = Fixture(); U(bad, 8 + 172, 4); Save("short-stream.gj", bad);
        bad = Fixture(); U(bad, 8 + 48, 320); U(bad, 8 + 320, 7); U(bad, 8 + 320 + 48, 320); Save("cycle.gj", bad);
        Save("truncated.gvm", Texture()[..^1]);
        for (int i = 0; i < 8; i++) Save("short-" + i + ".gj", Fixture()[..i]);
        Console.WriteLine(passed + " recipe assertions passed; generated original SDK integration fixtures.");
    }
}
