# SDK proposal: TEV effect overrides for imported model draws

Historical proposal: the pinned SDK now implements the per-draw override first
increment. See [the current assessment](tev-api-assessment.md) for implementation
coverage, browser integration, validation and remaining gaps.

Implementation handoff, based on DolphinDotNet commit
`f040ec327b63006380a01334463dcb58fd1024ea`. All APIs below are proposed, not
present in that snapshot. Implement this in the SDK; the asset browser should
only select effects and call the resulting public API.

## Problem and objective

`Material.Effect` reaches `dnd_mesh_draw_effect` through `Renderer.DrawMesh`.
`Draw2D.SetEffect` also reaches the effect resource system. Both ultimately use
the effect binder in `source/graphics_gx.c`.

Imported models are different. `Renderer.DrawModel` calls `dnd_model_draw` in
`source/model_graphics.c`, which configures GX and emits imported/skinned vertices
directly. It hardcodes one TEV stage: modulate texture/color, or pass color when
untextured. Imported `DndMaterial` stores color, cutoff, texture, mode and sidedness,
but no effect handle. Creating a managed Material with Effect does not alter it.

Objective: apply an existing GxEffect to an entire model for one draw, through the
existing model renderer. Preserve imported geometry, skinning, materials,
samplers, alpha, culling, depth and transparency ordering. Reuse the existing
effect package/compiler and native resource lifetime rules.

Do not export models into public Mesh objects or construct another model renderer.
Do not change source importers or the DNDM/DNEF package formats.

## Public API: first increment

Add these overloads to `Dolphin.Graphics.Renderer`:

```csharp
public void DrawModel(ModelInstance instance, Transform transform,
    GxEffect? effectOverride);

public void DrawModel(ModelInstance instance, float[] transform,
    GxEffect? effectOverride);

public void Draw(GameObject gameObject, GxEffect? effectOverride);
```

Retain all existing overloads. They behave as if effectOverride is null.
The Transform overload uses the existing cached WorldMatrix. Draw(GameObject)
uses its existing instance/world transform. An empty GameObject still submits
nothing and does not consume or validate an unused effect.

Example, with the existing camera, instance and transform supplied by the app:

```csharp
using var toon = GxEffect.ToonBands(65, 165,
    new Color32(12, 24, 64),
    new Color32(30, 90, 158),
    new Color32(70, 220, 248));

// Each frame, on the application executor:
renderer.BeginFrame(camera);
renderer.DrawModel(instance, transform, toon);
renderer.EndFrame();
```

Scope and precedence:

- null means the original imported rendering behavior, with no custom RGB effect.
- A non-null effect replaces RGB combination for every mesh in this draw.
- It is not multiplied on top of the previous RGB combiner automatically.
- It borrows each mesh's original texture, UVs, vertex colors and imported tint.
- It neither changes the Model nor installs persistent state on the instance.
- Two instances of one Model can use different effects, or no effect, in one frame.
- The next draw has its own effect selection. No implicit inherited override.

Do not expose a global Renderer.CurrentEffect or mutable Model.Materials for this
increment. A per-draw argument avoids ownership, restoration and shared-model
mutation problems. Per-material targeting is a separate future feature.

## Color and texture semantics

Use the existing effect language inputs:

1. Compute raster RGBA exactly as the model renderer does today: source vertex
   color times imported material tint, with existing diffuse lighting on RGB
   unless the imported material is unlit.
2. Expose the original texture through TEX0 when the material has one. Preserve
   its UVs, wrap modes, filters, mip count and LOD configuration.
3. Evaluate the custom RGB expression with the same TEV semantics as DrawMesh.
4. Preserve the default alpha: raster alpha times texture alpha if textured,
   otherwise raster alpha. Preserve alpha comparison, blending and depth behavior.

GxEffect.RequiresTexture is a compatibility requirement, not a command to disable
textures. If false, a textured mesh must still retain its texture for alpha even
when the RGB effect ignores it. This is essential for cutout edges.

The existing ColorRamp and ToonBands presets use raster RGB. They can deliberately
replace texture color, but still retain texture alpha. A textured toon effect
must explicitly combine texture RGB with its ramp in its DNEF expression; the
renderer must not guess or add a second modulation.

If RequiresTexture is true and any submitted mesh lacks an imported texture,
reject the entire model draw before emitting geometry. Do not skip that mesh,
substitute white, or draw earlier meshes before discovering the mismatch.
Unreferenced material records do not participate in this check.

TEV has no direct position/normal inputs in this interface. A normals view,
wireframe mode, selection outline, or UV heatmap requiring new vertex inputs
is outside this API. ToonBands thresholds raster.r, not a geometric normal.

## Preserve the model renderer's responsibilities

Keep these operations in `model_graphics.c` unchanged:

- Model readiness, instance validity and frame/transform validation.
- Rigid node transforms and the instance's existing skinned buffers.
- CPU normal transformation and lighting.
- Mirrored-transform culling adjustment and imported double-sided state.
- Opaque-before-translucent ordering and existing translucent depth sorting.
- Authored blend factors, depth writes, alpha-test threshold and mesh submission.
- Texture caching, sampler state, mipmaps and triangle accounting.

Do not route model meshes through dnd_draw_backend_vertices: its public mesh
material flags, texture setup and lighting differ from the imported model path.
Share TEV setup only; leave geometry/material semantics in their existing paths.

## Native implementation contract

### Entry points and managed binding

Add to `include/dnd_model.h`:

```c
int dnd_model_draw_effect(int instance_token, DndArray *world, int effect_token);
```

Keep dnd_model_draw as a wrapper calling the new function with effect_token=0.
Add `NativeModel.ModelDrawEffect(int token, float[] matrix, int effect)` and map it
in `NativeStorageBindings` to `dnd_model_draw_effect` with argument shape `iai`.
Follow the existing ModelDraw bridge/AOT registration pattern completely.

Suggested result codes (document in the native header):

- 0: success.
- -1: existing invalid instance/model/frame/transform conditions.
- -2: effect token is invalid, disposed/pending release, or not an effect.
- -3: texture-requiring effect is incompatible with a submitted mesh.

The managed overload checks instance, transform and effect with existing managed
Check methods. A disposed effect throws ObjectDisposedException. Map -3 to
ArgumentException identifying effectOverride. Preserve existing error behavior
for original overloads; do not silently fall back. Require the application
executor, as with other graphics operations. Reject override incompatibility
before sorting, triangle counters or any model GX submissions.

### Resource resolution

Add a narrow internal helper in `draw_resources.c`, declared in `dnd_draw.h`:

```c
int dnd_effect_resolve(int token, const DndDrawEffect **result);
```

Zero resolves successfully to NULL. A nonzero token uses the resource table's
existing kind/generation/pending checks, returning -2 if invalid. Do not expose
the Resource struct or duplicate handle bit manipulation in model_graphics.c.
The returned pointer is borrowed for the current native call only.

Resolve the effect and preflight referenced mesh texture compatibility once per
draw. Do not allocate a managed array, native vertex buffer or material copy per
mesh. No effect pointer/handle is persisted on DndModel or DndInstance.

### Shared TEV binder

Extract the existing GX combiner setup into an internal backend helper:

```c
void dnd_draw_backend_tev(const DndDrawEffect *effect, bool texture_bound);
```

Place its declaration with other draw backend functions. Its effect branch must
use the same uniform/stage implementation already used by DrawMesh/Draw2D.
Its NULL branch restores the original one-stage modulate/pass-color behavior.
Call it from both dnd_draw_backend_vertices and dnd_model_draw_effect, after their
own texture/raster setup and before vertex submission.

The helper owns TEV stage count, active-stage direct mode, swap modes, orders,
konst selectors/register values, RGB inputs/operations/destinations, and default
alpha inputs/operations. It does not bind textures or change projection, vertex
formats, lighting, blend, cull, alpha comparison, depth, viewport or scissors.
It must configure every active stage deterministically; previous effects must
not leave hidden TEV register dependencies. Reuse the existing DNEF validation.

Every mesh draw reapplies its chosen combiner because existing backend restore
operations may reset GX state. An effect draw followed by an ordinary model,
mesh, Draw2D pass or debug overlay must recover that path's default appearance.

## Ownership, mutations, ARAM and performance

The caller owns GxEffect and may reuse it across models/instances/draws. No
ownership transfer, clone, package patch or additional per-instance allocation.
The effect remains valid throughout each draw submission.

Preserve the existing graphics resource contract: disposal during a device frame
marks the resource pending, making it unavailable for new draws; already encoded
draws remain valid until frame completion. Physical reclamation follows GX
completion. Use the existing device-end/collect protocol, not a new refcount
system. Never keep a borrowed effect pointer across calls or collection.

Uniform changes remain forbidden during an active device frame. SetColor between
frames affects later draws. Do not add per-instance parameter dictionaries; use
separate GxEffect objects when simultaneous models need different parameters.

Model.UnloadToAramAsync/PrepareAsync remain independent: the override is not part
of the package or ARAM snapshot. Drawing a cold model still fails; after Prepare,
the caller can use any valid effect again. Shader resources remain in the graphics
resource budget, not the model/instance budgets. After initialization, repeated
override draws allocate no managed or native memory.

## Required tests

1. Legacy and explicit-null draws have identical native behavior and reference
   pixels. Test both matrix/Transform overloads and GameObject forwarding.
2. An imported multi-material model receives the effect on every mesh. Include
   a rigid model and an animated/skinned model using the same renderer.
3. Two instances share a Model but use different effects; subsequent default
   draws remain unchanged. Imported material/texture data is never mutated.
4. Textured and untextured models accept raster-only effects. Texture-requiring
   effects succeed with valid textures and reject a mixed model before any draw.
5. A raster-only effect preserves texture alpha, cutout edges, translucent order,
   authored blend factors, mirrored culling, samplers and mipmaps.
6. A 7-stage effect followed by default, a shorter effect, public mesh, Draw2D and
   overlay drawing leaves no stale stages/registers or other state pollution.
7. Disposed/wrong-kind/stale tokens fail; disposal after one draw does not break
   that draw, and a new draw with the pending resource is rejected.
8. Uniform updates obey frame boundaries. Repeated draws do not grow allocation
   counts, native budgets or managed heap usage.
9. Cold models reject drawing, and ARAM restore permits override/default draws
   again without storing an effect in the snapshot.
10. Managed AOT tests exercise the new bridges, exceptions and overloads under
    forced GC; native tests run under ASan/UBSan. Build the GameCube DOL.

Host model draws currently compile out much of the GX submission block. A host
test merely returning success from dnd_model_draw_effect does not prove that the
effect was bound. Add an appropriate backend test seam or compile the shared
combiner binding against the project's existing graphics/GX test backend. Assert
effect selection, call order and failure-before-submission explicitly.

Add deterministic Dolphin visual acceptance with original synthetic geometry:
compare default/effect/default frames, alpha cutouts and an ordinary UI pass.
Use tolerances appropriate to existing GX tests. Do not require retail game data.
Physical console visual/performance checks are separate acceptance evidence.

## File checklist and delivery order

SDK managed: Renderer.cs, NativeModel.cs.
SDK bridge: tools/DolphinDotNet.Compiler/NativeStorageBindings.cs and any compiler
registration/tests required by the existing ModelDraw binding.
SDK native: include/dnd_model.h, include/dnd_draw.h, source/model_graphics.c,
source/draw_resources.c, source/graphics_gx.c; host backend counterparts.
SDK docs/tests: graphics/effects/API reference, native tests, AOT smoke and
Dolphin visual fixtures.

1. Extract and test the shared binder without changing existing rendered output.
2. Add native effect resolution, preflight and model override entry point.
3. Add managed overloads and AOT bindings; test lifecycle/error behavior.
4. Run native/AOT/visual checks and cross-build.
5. Pin the completed SDK in the asset browser and add a default/ramp/toon selector.
   Create effects outside frames and pass the selected effect per draw.

## Explicit follow-up boundary

Per-material overrides need stable public material identities, metadata and
instance/draw-scoped selection semantics; those do not exist in this proposal.
Do not invent array-index material IDs in the browser as a persistent format.
Likewise, texture extraction, material editing, normal-debug vertex inputs and
wireframe rendering should be designed separately rather than hidden inside
this RGB-effect override. The first increment is complete when imported models
can use existing effects safely without a second renderer or package changes.
