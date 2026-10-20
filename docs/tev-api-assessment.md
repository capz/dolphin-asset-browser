# TEV API assessment

Reviewed SDK main at `330138646de7fa57a20863b5bd5116963a2df50e` on
2026-10-20, replacing `f040ec327b63006380a01334463dcb58fd1024ea`.
This is a source snapshot pin, not a NuGet version. No SDK files are modified.

## Coverage against the browser proposal

The per-draw Transform/matrix `Renderer.DrawModel` overloads and
`Renderer.Draw(GameObject, effectOverride)` are implemented. The AOT bridge maps
`NativeModel.ModelDrawEffect` to `dnd_model_draw_effect`. The native path resolves
the effect and preflights texture requirements before sorting or submissions;
it retains imported geometry, skinning, lighting, alpha tests, blend/depth/cull
state, UVs, samplers and transparency ordering. Both model and mesh rendering
use the shared `dnd_tev_backend.inc` binder. Null selects original rendering.

`GxEffect.ColorRamp` and `ToonBands` need no texture and work on mixed models.
They still retain texture alpha. The browser owns two preset effects per loaded
model, cycles by changing only a selection integer, and borrows the effect for
each draw. Constructor failure releases any partially created effect. Reload,
back and shutdown release effects along with model resources.

The existing proposal in `sdk-model-effects-proposal.md` is historical; the first
increment now exists. There is no need for a second model renderer, model export
to Mesh, raw GX manipulation or a persistent material mutation in this app.

## Validation

`scripts/check.sh` now runs the pinned SDK's recording GX model-effect regression
under ASan/UBSan, covering alpha/sampler/material/geometry preservation,
default/effect/default state restoration, incompatible or stale effects before
submission, skinning and repeated draws without allocation. This executes the
production GX path and binder, unlike host-only successful draw return codes.

Browser AOT regression input cycles through all three effects and verifies
selected native effect stages, model viewport and restoration before the text
pass. Smoke tests exercise both overloads, textured and untextured imports,
extended BML import, selection wrap/reset, idempotent disposal and use after
disposal. Existing forced-GC and manual-scheduler runs also execute these tests.
Camera tests verify sphere framing in the narrow model viewport.

These checks do not prove pixel fidelity or physical console performance.
The SDK has Dolphin shader-demo acceptance, but the browser's new two-column
inspector still needs interactive Dolphin/console visual acceptance with actual
asset data. No verified retail Booma recipe or retail test data is added.

## Remaining API gaps

- No stable public imported material identities/metadata or per-material effect
  targeting; whole-model overrides cannot reconstruct distinct authored TEV
  materials. Importer's explicit approximation restrictions still apply.
- Effect inputs are raster and TEX0; no public normal/position inputs for normal
  visualization, geometric lighting shaders, outlines or UV debug overlays.
- No wireframe/outline model mode or public texture/material editing/extraction.
- No aggregate imported model bounds; recipes still provide framing bounds.
- Presets replace texture RGB. Textured ramp/toon requires an explicitly compiled
  DNEF graph combining TEX0 with the ramp; the browser does not invent that mix.

These gaps do not block the implemented Original/Color ramp/Toon inspection modes.
