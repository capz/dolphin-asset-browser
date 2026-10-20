# Dolphin asset browser

A GameCube disc/file browser and PSO model inspector using DolphinDotNet's native
loaders. SDK decoding stays in the SDK; this project handles source selection,
asset recipes, inspection controls and error/retry screens.

## Build and debug

Install .NET 8, Git, Python 3, a host C compiler (for tests), and Docker with Linux
containers. From the repository root:

```sh
bash scripts/ensure-sdk.sh
# Native DOL build through the SDK's MSBuild integration:
dotnet build DolphinAssetBrowser.sln -c Release
# Or run all regressions first, then build/validate the DOL:
bash scripts/build.sh
```

Outputs are `src/Viewer/bin/Release/net8.0/Viewer.dol` and `Viewer.elf`. The SDK
owns bundle generation, Docker/native selection, EXI correction and stale-output
cleanup. It uses the toolchain image pinned in `toolchain-image.txt`. The SDK
commit is pinned in `sdk-snapshot.txt`; no SDK source changes are required.

`DEVKITPRO`/`DEVKITPPC` select a compatible native libogc2 toolchain on Linux/macOS;
otherwise the SDK selects Docker. `DOL_BUILD_JOBS` sets wrapper build parallelism.
For a proxy environment, set `GIT_SSL_CAINFO` to the trusted host CA bundle; the
SDK mounts it read-only in the container. The wrapper supplies the Linux system
bundle when no custom path is set. TLS verification remains enabled.

Open `DolphinAssetBrowser.sln` in Visual Studio or Rider. Build **Debug** and use
**Dolphin Debug** for attach preparation, or **Dolphin Play** for ordinary play.
Install the SDK's corresponding IDE debugger extension and attach to the isolated
Dolphin GDB listener (port 2166) with matching ELF/debug maps. `DOLPHIN_PATH` selects
an emulator not on PATH. The SDK creates a separate Dolphin profile; configure
controller bindings and your test disc/SD media there. Creating a native build
alone does not supply test media.

```sh
dotnet run --project src/Viewer -c Release
dotnet build src/Viewer -c Debug -p:DndBuildBackend=Docker
# Managed-only compilation, useful when the toolchain is unavailable:
dotnet build src/Viewer -p:DndBuildNative=false
```

Optional local settings go in ignored `src/Viewer/DolphinDotNet.local.props`:

```xml
<Project><PropertyGroup>
  <DndBuildBackend>Docker</DndBuildBackend>
  <DndBuildJobs>2</DndBuildJobs>
</PropertyGroup></Project>
```

## Sources and browsing

Startup attempts GC Loader SD first. When it mounts, the source menu offers
extracted assets, an explicit inserted-disc mode, and ISO/GCM candidates found
under `gcloader:/games`. Discovery validates 0x440-byte headers, sorts candidates,
and bounds entry count, depth and image count. Invalid/unreadable images are
reported; compressed formats such as RVZ/CISO are not supported.

If GC Loader cannot mount, the app attempts the inserted GameCube/ODE disc.
A failed Loader mount means unavailable, not proven hardware absence. Selecting
inserted-disc mode first releases raw Loader access. Both ISO and inserted-disc
files appear at `dvd:/`. Titles, banners and exact game ID/disc index/revision
are displayed. Disc mounts are released before backing volumes.

- D-pad up/down selects; A opens a source, folder, model or recipe.
- B returns to the parent folder; Z returns to source selection.
- X rescans in the source menu, or switches rigid/extended SDK import mode in
  file browsing. A `.psoasset` recipe chooses its own mode.
- Start exits. Errors persist: X retries the same operation, B returns to sources.

Raw models optionally use a same-stem `.gvm` texture archive. No motion rate is
guessed. Textured models with missing textures fail rather than silently falling
back. Archive member selection, nontrivial texture association and motion settings
belong in a recipe. Unknown files can be inspected in listings but are not executed.

## Asset recipes

Place a `.psoasset` text file beside assets or on the mounted disc. See
[examples/synthetic.psoasset](examples/synthetic.psoasset) for an original-fixture
example. Relative paths resolve from the recipe directory; paths stay on the
same mounted volume. A single SDK member selector is preserved, for example
`model=enemy.bml#enemy.rel` or `motion=motion.rel#0`.

Fields:

| Field | Meaning |
| --- | --- |
| `model` | Required model path, optionally with SDK selector |
| `name` | Display name |
| `profile` | `rigid` (default GJ) or `ninja` (SDK extended NJ/REL/BML profile) |
| `texture`, `motion` | Optional source paths; textures may be inline in BML |
| `fps` | Required verified source frame rate when motion is supplied |
| `eulerKeyBytes` | 8 or 16 (default 16), selected from the actual motion layout |
| `nativeBudgetBytes` | Default 8 MiB, maximum 16 MiB; instance budget is separate |
| `centerX/Y/Z`, `radius` | Optional framing bounds; radius 0.001..10000 |

For an unmodified retail disc, select **RECIPES from SD2SP2** in the `dvd:/`
root and put recipe files in `sd:/pso-profiles`. Their asset paths resolve against
`dvd:/`, so profiles can live separately from read-only game data. SD2SP2 is
probed only when this option is selected. B returns from that profile root to
the disc listing. No recipe is copied onto or written into the disc.

A recipe can also bind to a disc by providing **all** of `gameId`, `discNumber`,
`revision`, `signaturePath`, and `signatureHex`. The identity must match exactly,
and the first 1..64 bytes of the named regular file must match the hex signature
before importing. This supports reviewed, revision-specific asset maps. It is not
cryptographic provenance or verification of the entire game image.

No verified retail PSO/Booma recipe is bundled: actual Booma files, members,
textures and motion layouts still require inspection against your disc revision.
Manual imports are labeled accordingly. The app never claims a model is Booma
because its filename or disc title looks similar. Original discs remain read-only.

## Model inspector

- Stick: orbit; C-stick: pan; L/R analog triggers: zoom.
- Y: reset to recipe framing; Z: toggle slow automatic model rotation.
- D-pad up/down: choose a clip or rest pose.
- A: pause/resume; left/right: seek by 0.25 seconds; X: cycle playback speed.
- Hold digital L + X: cycle Original, Color ramp and Toon. X alone still changes
  playback speed. Digital L suppresses zoom while held as the effect modifier.
- B: return to files; Start: exit.

The approved inspector layout uses the left two-thirds for text and the right
third for the model, with a matching camera aspect, viewport and scissor. Paths
wrap across two rows (longer tails are truncated). The existing bitmap font,
background, highlight colors and source/banner assets are retained. Statistics
use M/I/B for model bytes, instance bytes and bytes.

Effects are created once when loading a model, outside a frame, and disposed
when leaving it. Original is the default on every load. Overrides are passed per
draw; they do not modify imported materials or shared models. Ramp and Toon use
the existing inspector palette and replace texture RGB with raster-driven color
(Toon thresholds raster red). Imported texture alpha, cutouts, blending, depth,
samplers and skinning remain the SDK's responsibility. These are inspection
views, not faithful reconstructions of authored PSO TEV materials.

The inspector shows source/disc identity, loader profile/budget, model and instance
native bytes, node count, clip time/duration/speed, and asset paths. Models without
recipe bounds use a unit framing guess; orbit/pan/zoom remain available. The SDK
has no public aggregate model bounds API.

A loading frame is drawn and the SDK call is awaited immediately. There is no
pre-await polling loop, fabricated progress percentage, or input-driven cancel
loop. Input resumes after loading. Native parsing, texture expansion and model
publication remain subject to SDK memory and format limits.

## Validation and releases

`bash scripts/check.sh` runs source-session, recipe, path, discovery and camera
regressions; original GJ/GVM/NMDM/BML fixture tests; native ASan/UBSan checks; and
AOT smoke tests including forced GC and manual scheduler mode. It also compiles
and AOT-checks the viewer. It does not run the interactive UI on hardware.

Every push/PR runs validation and produces DOL/ELF artifacts. Successful default
branch pushes and manual runs publish a development prerelease with
`dolphin-asset-browser.dol` and `SHA256SUMS`. Push a `v*` tag for a versioned release.
PRs never publish. Only the release job receives repository write permission.

See [the implementation design](docs/implementation-design.md) for the remaining
retail-asset and hardware acceptance work. A successful build or synthetic test is
not evidence of real Booma compatibility, original-disc access or visual fidelity.
See [the TEV API assessment](docs/tev-api-assessment.md) for SDK coverage and gaps.
