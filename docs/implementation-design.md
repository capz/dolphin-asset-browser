# Asset browser implementation

Baseline: DolphinDotNet `330138646de7fa57a20863b5bd5116963a2df50e`, pinned in
`sdk-snapshot.txt`. Binary format decoding remains in the unmodified SDK.
The earlier [TEV effect proposal](sdk-model-effects-proposal.md) is implemented
by the SDK. The browser uses its per-draw override for Original/Color ramp/Toon;
see [the API assessment](tev-api-assessment.md). It does not create another renderer.

## Build and debug

Viewer imports `DolphinDotNet.Sdk` props/targets from the pinned checkout. Its
MSBuild build produces `Viewer.dol`, ELF and, for Debug, matching managed/native
source maps. The solution and launch profiles support Visual Studio/Rider and
Dolphin Play/Debug. The SDK build driver owns native bundle generation, Docker
proxy/CA forwarding, toolchain selection and stale native-output removal.

`ensure-sdk.sh` initializes the checkout and checks the pin. `check.sh` runs host
and AOT regressions with the native SDK compiled once into a sanitizer archive.
`build.sh` adds the actual MSBuild native build and validates DOL sections.
The release workflow publishes the new SDK output paths. No SDK edits or copied
compiler code are required. Font rendering links the pinned sample's generated
bitmap atlas helper; the UI otherwise uses public graphics APIs.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| SourceSession / ISourceMounts | source policy, mount dependencies and cleanup |
| ImageDiscovery | bounded header-based ISO/GCM discovery, deterministic sorting |
| AssetPaths | mounted-volume path resolution and selector preservation |
| AssetDefinition | explicit import settings, optional disc binding and framing |
| PsoModelLoader | direct dispatch to rigid or extended SDK PSO import |
| OrbitCamera | bounded camera state using supported MathF/Math APIs |
| BrowserApp / BrowserUi | screens, input, retry, playback and rendering |
| SDK | FST/ISO mounting, identity/banner parsing, binary import and rendering |

## Source policy and screens

Startup first attempts GC Loader storage. If usable, Sources offers extracted
files, an explicit inserted-disc transition, and candidates from `/games`.
If unavailable, it attempts the inserted disc. A failed Loader mount is reported
as unavailable; it does not prove hardware absence. Transitioning to inserted
media releases the image and raw Loader volume first. Both disc routes use `dvd:/`.

The disc root offers an explicit SD2SP2 recipe directory. Profiles under
`sd:/pso-profiles` resolve model/texture/motion/signature paths against `dvd:/`,
allowing exact revision profiles for unmodified physical discs. The app mounts
that optional card only on request and releases it after imports finish. This
is configuration storage, not a second geometry/archive decoding path.

SourceSession owns flags only for successful mounts and clears them only after
successful unmount. Identity-read failure unmounts the partial disc. It keeps
ownership when unmount fails so cleanup can retry. No model load is left pending
when mounts are released; instances are disposed before models.

Discovery scans at most 2048 entries, 64 valid images, six directory levels, and
128 pending directories. Every entry counts, including unrelated files. Reads
handle short transfers and inspect 0x440-byte GameCube headers. ISO/GCM extension
matching is case-insensitive; RVZ/CISO and images at least 2 GiB are unsupported.
Malformed/unreadable images and scan limits are reported. Header validation is
not proof that the full filesystem or game data is valid.

File listings bound enumeration and show directories first. Raw models use the
user-selected rigid/extended import mode; recipes override it. There is no
repeated-parser probing or silent texture loss. A same-stem GVM may accompany a
manual file, but motion settings are never guessed from filenames.

Screens are Sources, Files, Loading, Inspector and Error. Operations preserve
stage/path/error context. X retries the same failed operation; B returns to
source selection. A loading frame is presented followed by a direct SDK await,
without IsCompleted polling. Input resumes after loading. The source remains
owned throughout await and cleanup. No invented percentage progress is shown.

## Recipe validation

Recipes are bounded UTF-8 text files with key=value fields: at most 16 KiB and
64 lines. Duplicate, empty and unknown fields fail. Model, texture and motion
paths normalize dot segments, remain on the source volume, and permit one SDK
member selector. Model profile, Euler key size, native budget and source motion
frame rate are explicit and validated.

Optional framing consists of center XYZ and radius. Radius is 0.001..10000,
center magnitude is bounded, and non-finite values fail. Without bounds, the
viewer uses a unit guess plus manual controls; it cannot obtain aggregate bounds
from the SDK's current public Model API.

Disc-bound recipes require all of GameId, disc index, revision, signature path
and signature hex. Identity must match exactly and a 1..64-byte file prefix must
match before import. This is a revision/profile guard, not a full-disc hash or
cryptographic provenance check. Unbound recipes are labeled manual imports.

No enabled retail Booma profile is included. Real model/member/texture/motion
paths and signatures still need verification from a user's supported PSO disc.
There is no filename/title-based claim that an imported model is Booma.

## Inspector

The left stick orbits; C-stick pans; analog triggers zoom. Y restores framing
and model rotation, Z toggles automatic rotation. Camera math clamps pitch and
distance. FrameClock bounds delta and resets after loading.

D-pad up/down selects clips or rest pose, A pauses/resumes, left/right seeks by
0.25 seconds, and X cycles 0.25/0.5/1/2 speed. Playback time is tracked locally
because the SDK does not expose time readback; all control changes are mirrored
to Animator. B disposes the model and returns to the existing listing.

The overlay shows identity, recipe name, source paths, importer/budget, model and
instance native bytes, node count and playback state. Numeric text is refreshed
at four Hz; static source labels are cached. Graphics remain on the application
executor with explicit 3D/2D pass boundaries. Authored materials are untouched.

## Memory and limits

Imports use SDK native allocations and their existing budgets (default 8 MiB,
maximum 16 MiB), with a separate instance budget. Temporary input, decompression,
texture expansion and publication overlap all count. Ordinary PSO source files
are limited to 2 MiB each; extended input can reach 16 MiB only within its complete
budget. No automatic geometry reduction or material approximation is enabled.

The SDK still has format restrictions, whole-file imports, a 31-entry BML limit
and one-selector restrictions. The browser surfaces failures and permits explicit
recipes; it does not implement another archive/geometry stack speculatively.

## Validation boundaries

The host suite covers source policy, unmount order/failure, path containment,
exact disc/content checks, recipe rejection, bounded discovery and camera math.
Synthetic GJ/GVM/NMDM/BML fixtures exercise the actual SDK's native importer under
ASan/UBSan, including invalid data, textures, motion and cleanup. The managed
adapter and browser core also run through AOT under normal/forced GC and manual
scheduler mode.

A host-driven interactive AOT test navigates Sources to Files, loads a recipe,
uses camera/playback controls, returns to Files and exits, checking model/file/
graphics resource and mount cleanup. Its graphics backend is headless: it proves
control flow and ownership, not pixels. The native GameCube build verifies the
actual PowerPC toolchain and DOL layout. Physical drive access and visual fidelity
still require Dolphin/console acceptance with representative assets.

The implementation passed 38 browser assertions, 12 recipe assertions and 163
SDK-native assertions. AOT adapter tests passed normally, under forced GC and
with manual scheduling. The interactive host tests passed navigation/playback,
forced GC and persistent error/retry/back/exit scenarios. Release and Debug DOL
builds passed, and Debug generated matching ELF/source-map artifacts. IDE attach
itself and physical-console visuals were not exercised.

## Remaining acceptance work

- Verify Booma asset maps and source motion settings for actual PSO revisions.
- Compare real geometry, textures, normals, alpha and motion independently.
- Validate GC Loader firmware/card access and original DVD reads separately.
- Measure real import/frame/memory costs; exercise media removal and long paths.
- Add automatic bounds/material metadata only through a deliberate SDK API.
