#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/ensure-sdk.sh
sdk=.dependencies/dolphindotnet
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
dotnet run --project tests/Pso.Tests -c Release -- "$scratch/fixtures"
# Exercise production GX submission and the shared TEV binder, including alpha,
# sampler state, effect/default restoration and rejection before submission.
cc -pthread -DDND_MODEL_GX_TEST -O1 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -ffunction-sections -fdata-sections -std=c11 -Wall -Wextra -Werror \
  -I"$sdk/tests/model_effect_sdk" -I"$sdk/include" "$sdk/tests/model_effect_test.c" \
  "$sdk/source/managed_runtime.c" "$sdk/source/async_runtime.c" "$sdk/source/model.c" \
  "$sdk/source/model_animation.c" "$sdk/source/model_graphics.c" "$sdk/source/graphics_device.c" \
  "$sdk/source/draw_resources.c" "$sdk/source/performance.c" \
  -Wl,--gc-sections,--wrap=malloc,--wrap=calloc,--wrap=realloc -lm -o "$scratch/model-effects"
"$scratch/model-effects"
# Compile the pinned SDK runtime once; all native and AOT harnesses share it.
mkdir -p "$scratch/objects"
sources=(managed_runtime async_runtime storage aram timing numeric text input
         graphics_device draw_resources graphics_gx performance)
for source in "${sources[@]}"; do
  cc -pthread -O2 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
    -std=c11 -Wall -Wextra -Werror -I"$sdk/include" -c "$sdk/source/$source.c" -o "$scratch/objects/$source.o"
done
for source in "$sdk"/source/model*.c; do
  cc -pthread -O2 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
    -std=c11 -Wall -Wextra -Werror -I"$sdk/include" -c "$source" -o "$scratch/objects/$(basename "${source%.c}").o"
done
ar rcs "$scratch/runtime.a" "$scratch"/objects/*.o
cc -pthread -O2 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" tests/native_pso_test.c "$scratch/runtime.a" -lm -o "$scratch/native-test"
"$scratch/native-test" "$scratch/fixtures"
dotnet build tests/Pso.Smoke/Pso.Smoke.csproj -c Release
dotnet run --project "$sdk/tools/DolphinDotNet.Compiler" -- \
  --aot tests/Pso.Smoke/bin/Release/net8.0/Pso.Smoke.dll "$scratch/pso-smoke.c"
cc -pthread -O0 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" -c "$scratch/pso-smoke.c" -o "$scratch/pso-smoke.o"
cc -pthread -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" "$scratch/pso-smoke.o" "$sdk/tests/model_host.c" \
  "$scratch/runtime.a" -lm -o "$scratch/pso-smoke"
timeout 30s "$scratch/pso-smoke" "$scratch/fixtures"
timeout 30s "$scratch/pso-smoke" "$scratch/fixtures" --gc-stress
DND_ASYNC_MANUAL=1 timeout 30s "$scratch/pso-smoke" "$scratch/fixtures"
dotnet build src/Viewer/Viewer.csproj -c Release -p:DndBuildNative=false
dotnet run --project "$sdk/tools/DolphinDotNet.Compiler" -- \
  --aot src/Viewer/bin/Release/net8.0/Viewer.dll "$scratch/viewer.c"
cc -pthread -O0 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" -c "$scratch/viewer.c" -o "$scratch/viewer.o"
cc -pthread -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" "$scratch/viewer.o" tests/browser_host.c \
  "$scratch/runtime.a" -Wl,--wrap=dnd_model_draw_effect,--wrap=dnd_draw_backend_viewport,--wrap=dnd_canvas_begin -lm -o "$scratch/browser-test"
timeout 30s "$scratch/browser-test" "$scratch/fixtures"
timeout 30s "$scratch/browser-test" "$scratch/fixtures" --gc-stress
timeout 30s "$scratch/browser-test" "$scratch/fixtures/error" --error
