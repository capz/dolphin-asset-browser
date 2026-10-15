# PSO asset browser implementation design

SDK baseline: `capz/dolphindotnet` commit
`4b0ace6526d99b9e680b26243af5a0d75cf35314` (`sdk-snapshot.txt`).
The project delegates supported binary formats to SDK loaders. SDK source is
unmodified; recipe selection, disc knowledge and user interaction stay here.

## Responsibility split

| Project | SDK |
| --- | --- |
| ISO discovery and GC Loader/DVD selection | mounts, file handles and FST validation |
| Exact PSO revision profiles and content signatures | DiscIdentity header/banner parsing |
| Booma file/member/texture/motion associations | supported GJ/NJ/REL, GVM/GVR, BML/PRS decoding |
| Explicit structural loader choice and source motion rate | transforms, materials, skinning and animation conversion |
| Loading/error/cancel UI, camera and controls | background import, native allocations, model lifetime and rendering |
| Retail-asset integration regressions | binary validation and model-resource format |

No OBJ/FBX intermediate, DNDM package writer or local fallback parser is needed.
The removed managed importer handled only a small static untextured GJ subset;
maintaining it would duplicate format fixes without covering a missing capability.
Its useful synthetic fixture ideas now exercise the actual SDK instead.

## Current project implementation

`PsoAssetRecipe` is immutable. It supplies a model path, optional texture/motion
paths, loader profile, Euler key width, explicit motion rate, native budget and
explicit material-approximation opt-in. Invalid combinations fail before import.
A motion requires a verified frame rate; the application never guesses it from
an extension. Approximation defaults off and is unavailable for rigid GJ.

`PsoModelLoader.LoadAsync` maps recipes directly to:

- `RigidGj`: `Model.LoadPsoAsync`, for the verified rigid GJ/GVM/NMDM profile.
- `NinjaRel`: `Model.LoadPsoEpisode3Async`, for supported extended GJ/NJ/REL,
  PRS/BML and inline texture layouts. This profile is selected by structure,
  not an assumption that all Episode I/II assets match Episode III.

There are no repeated parser attempts or silent untextured fallbacks. SDK errors
propagate to the application. Selectors such as `archive.bml#member.gj` are
passed unchanged to the SDK; this project does not unpack supported containers.

The diagnostic viewer mounts GC Loader storage, loads `preview.gj` and an
optional `preview.gvm`, and renders through the ordinary SDK Renderer. During
loading it draws empty frames, polls B for cancellation and always awaits import
completion before releasing the source. During viewing B/Start exits cleanly.
If a recipe supplies a motion, the returned `Motion` clip is played. Rendering
and instance updates happen on the application executor; the SDK owns background
parsing. The fixed camera is still a diagnostic limitation.

Recipe/adapter sources are compiled into Viewer and the AOT smoke entry assembly
because the pinned compiler discovers SDK-named dependencies only. This keeps
application code local without runtime or compiler modifications.

## Target disc-to-Booma flow (still planned)

1. Attempt GC Loader storage. Discover ISO/GCM candidates under `/games`, bounded
   by depth, file count and path length. Read 0x440-byte candidate headers using
   `DiscIdentity.ParseHeader`; sort matching candidates and offer selection.
2. If GC Loader cannot mount, try `Storage.MountDisc()` for the inserted game.
   Probe results cannot prove GC Loader absence; report inaccessible media
   accurately. Do not use raw GC Loader and inserted/ODE disc modes concurrently.
3. Mount a selected image with `Storage.MountDisc(fullImagePath)`. Both routes
   expose `dvd:/`. Use `Storage.GetMountedDiscIdentity()` and match the exact
   `(GameId, DiscNumber, Revision)`, then verify file/member signatures. Banners
   and titles are display data, not matching criteria.
4. A verified revision profile resolves Booma's model, texture and motion paths
   into a recipe. No enabled retail profiles exist until the actual data is
   inspected. Determine whether Booma is GJ or an embedded NJ/REL variant.
5. Load directly from `dvd:/` with the appropriate SDK API; no extracted package
   or writable storage is required. Validate source motions and material settings.
6. Frame the loaded model, offer orbit/zoom/reset and diagnostics. Dispose the
   instance before model, close streams and finish pending loads, unmount `dvd:/`,
   then unmount its backing GC Loader volume.

The new SDK renamed `MountGameCubeDisc()` to `MountDisc()` and
`DiscFormat.GameCubeFst` to `DiscFormat.Fst`. Only original ISO/GCM is supported;
compressed ISO containers are not. The native image-file cap remains below 2 GiB.

## Memory and API limits

The application no longer holds complete model input, expanded vertex arrays and
DNDM output in the 256 KiB managed heap. SDK native import budgets (default 8 MiB,
maximum 16 MiB) include input, temporary data, texture expansion, package creation
and publication overlap. Instance resources have a separate budget. This reduces
managed-heap pressure; it does not guarantee a lower total peak or frame time.

Ordinary PSO source files are capped at 2 MiB each. The extended loader reads
whole source files with a 16 MiB ceiling, still subject to its complete native
budget. GVM textures expand to tiled RGBA8, with no generated mipmaps on these
PSO import paths. Measure actual Booma import/native/instance peaks before
choosing budgets. No automatic reduction in detail or dropped materials.

The SDK BML adapter currently permits at most 31 entries and rejects multiple
`#` selectors. Complex nested selection may therefore require SDK changes or a
narrow project adapter, but only after demonstrating a real unsupported asset.
Do not implement a second general-purpose archive/geometry stack speculatively.

Native parses run outside the managed gate on a single service executor. Parsing
is not interruptible mid-call; cancellation waits for cleanup and other service
work queues behind it. Final publication/storage can delay a frame. The viewer
shows loading status, not invented percentage progress.

## Validation

`bash scripts/check.sh` checks the SDK pin and runs:

- Project recipe validation and original GJ/GVM/NMDM/BML fixture generation.
- SDK native import tests under ASan/UBSan: mixed index widths, list/strip/fan
  winding, rotated nodes, texture pixels, motion evaluation, inline BML/PRS
  texture loading, invalid pointers/indices, truncation, budgets and cleanup.
- Actual C# recipe/loader adapter through AOT under ASan/UBSan, including forced
  GC, both loader branches, rendering, missing-texture rejection and pre-cancel.
- Viewer build, GameCube AOT bundle and generated-C syntax checking.

`bash scripts/build.sh` additionally builds the PowerPC ELF and Game.dol using the
SDK CI toolchain pinned by digest in `toolchain-image.txt`, then validates DOL
sections. HTTPS verification remains enabled, using host trust in the container.
The SDK's upstream suite covers additional format variants; these project tests
focus on our integration contract rather than reimplementing its format tests.

Synthetic success does not prove retail assets, actual GC Loader/DVD reads,
visual fidelity, hardware memory peaks or loading responsiveness. No physical
hardware or emulator acceptance is claimed.

The migrated snapshot passed the complete `scripts/build.sh` run: 12 recipe
assertions, 163 SDK-native assertions, both AOT smoke runs (the stress run made
82 collections), viewer compilation, PowerPC ELF linking, DOL conversion and
section validation. The SDK checkout has no source modifications.

## Remaining milestones

1. Verify a real PSO revision and Booma asset map; choose the structural recipe.
2. Add source selection, catalog, bounded discovery and mounted-disc checks.
3. Add camera framing/orbit and useful error/retry screens.
4. Compare real Booma textures, winding, normals, pose and motion independently;
   validate GC Loader and original-disc routes separately on hardware.
5. Extend SDK support or add a narrow local adapter only for confirmed gaps.
6. Optional caching and additional games/revisions follow acceptance.

References:
- [Pinned SDK](https://github.com/capz/dolphindotnet/tree/4b0ace6526d99b9e680b26243af5a0d75cf35314)
- [SDK on-device importer contract](https://github.com/capz/dolphindotnet/blob/4b0ace6526d99b9e680b26243af5a0d75cf35314/docs/model-source-import.md)
- [Extended PSO profile](https://github.com/capz/dolphindotnet/blob/4b0ace6526d99b9e680b26243af5a0d75cf35314/docs/pso-episode3-loading.md)
