# Dolphin asset browser

On-device PSO asset loading on DolphinDotNet. The project is at its first
diagnostic milestone; automatic disc-to-Booma extraction is not implemented yet.
See [the implementation design](docs/implementation-design.md) for scope,
SDK contracts, memory constraints and remaining milestones.

## Build Game.dol

Install .NET 8, a host C compiler, Python 3, Git and Docker, then run:

```sh
bash scripts/build.sh
```

Output: `out/Viewer/Game.dol` (with `Game.elf` for debugging).
The command checks out the pinned SDK if missing, runs the tests, generates C,
builds with the SDK's libogc2/devkitPPC container and validates the DOL sections.
The container is pinned by digest in `toolchain-image.txt` (SDK CI image
`ghcr.io/extremscorner/libogc2:20260805`). Docker must be running; the initial
build needs network access for the image, .NET restore and verified EXI sources.
Build output stays in `out/`, owned by the invoking user.

If `DEVKITPRO` and `DEVKITPPC` identify an installed libogc2 toolchain, the script
uses it directly. `DOL_BUILD_JOBS` defaults to 2. For container builds,
`DOL_CA_BUNDLE` can select an absolute path to a trusted CA bundle; Linux's
`/etc/ssl/certs/ca-certificates.crt` is used by default. TLS verification stays
enabled for dependency fetching.

The PowerPC ELF link, ELF-to-DOL conversion and DOL section validation have been
run successfully. This establishes a buildable diagnostic viewer, not real
Booma compatibility or successful hardware rendering.

## Host checks and manual bundle generation

Requires .NET 8. Check out the SDK at the exact commit in `sdk-snapshot.txt`:

```sh
git clone https://github.com/capz/dolphindotnet.git .dependencies/dolphindotnet
git -C .dependencies/dolphindotnet checkout --detach "$(cat sdk-snapshot.txt)"
dotnet run --project tests/Pso.Tests -c Release -- /tmp/pso-fixtures
dotnet build src/Viewer/Viewer.csproj -c Release
```

`bash scripts/check.sh` validates project-owned recipe settings, generates original
GJ/GVM/NMDM/BML fixtures, and exercises the SDK native importers under ASan/UBSan.
It also executes the actual C# recipe adapter through AOT (normal and forced GC),
builds the viewer and generates its GameCube bundle. SDK revisions are pinned and
tracked SDK edits are refused.

## Preview assets

The viewer loads `gcloader:/preview.gj` directly through `Model.LoadPsoAsync`.
If `gcloader:/preview.gvm` exists it is supplied as the texture archive. A textured
model without its textures fails; there is no untextured fallback. The viewer
shows a loading frame and immediately awaits the SDK load, without polling
`IsCompleted`. B or Start exits after loading. The source remains mounted until
loading and cleanup complete. The camera is still fixed.

`src/Pso/PsoAssetRecipe.cs` records model, texture and motion paths, structural
loader profile, explicit motion frame rate and native budget. `PsoModelLoader`
selects `LoadPsoAsync` for rigid GJ or `LoadPsoEpisode3Async` for verified
Ninja/REL/BML layouts. A future verified disc profile will provide the recipe;
the diagnostic entry point currently supplies an extracted GJ path.
There is no project-owned geometry parser, texture decoder or DNDM encoder.

To try the generated synthetic textured fixture, copy `textured.gj` to
`preview.gj` and `preview.gvm` from `/tmp/pso-fixtures/sd/` to the GC Loader
card root. This is a diagnostic triangle, not Booma game data.

To generate a GameCube bundle (requires SDK compiler dependencies):

```sh
dotnet run --project .dependencies/dolphindotnet/tools/DolphinDotNet.Compiler -- \
  --bundle src/Viewer/bin/Release/net8.0/Viewer.dll out/Viewer --platform gamecube
make -C out/Viewer
```

The manual make step requires devkitPPC/libogc2 and the pinned SDK's documented
native libraries; `scripts/build.sh` supplies these through Docker when needed.
A host C# build alone does not validate AOT or hardware behavior.

SDK importers allocate source data, scratch space and model resources natively.
The default import budget is 8 MiB (maximum 16 MiB), with a separate instance
budget. Limits and format subsets still apply; neither a successful build nor
synthetic fixture tests prove retail Booma compatibility or hardware performance.

## GitHub releases

The build workflow runs tests and produces DOL/ELF artifacts on pushes and pull
requests. Successful pushes to the repository's default branch also publish a
prerelease tagged `build-<run-id>-<attempt>` containing
`dolphin-asset-browser.dol` and `SHA256SUMS`. A manual workflow run produces the
same kind of prerelease. Push a `v*` tag (for example `v0.1.0`) for a normal
versioned release. Publishing requires the build job to pass; pull requests never
publish. Only the release job receives repository write permission.
