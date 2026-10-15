#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
sdk=.dependencies/dolphindotnet
expected=$(cat sdk-snapshot.txt)
if [ ! -d "$sdk/.git" ]; then
  git clone https://github.com/capz/dolphindotnet.git "$sdk"
  git -C "$sdk" checkout --detach "$expected"
fi
if [ "$(git -C "$sdk" rev-parse HEAD)" != "$expected" ]; then
  echo "SDK checkout differs from sdk-snapshot.txt; preserve local work and select the pinned commit." >&2
  exit 1
fi
if [ -n "$(git -C "$sdk" status --porcelain --untracked-files=no)" ]; then
  echo "SDK has tracked changes; validation requires the unmodified pinned snapshot." >&2
  exit 1
fi
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
dotnet run --project tests/Pso.Tests -c Release -- "$scratch/fixtures"
cc -pthread -O2 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" \
  tests/native_pso_test.c "$sdk/source/managed_runtime.c" \
  "$sdk/source/async_runtime.c" "$sdk/source/storage.c" "$sdk"/source/model*.c \
  "$sdk/source/graphics_device.c" "$sdk/source/draw_resources.c" \
  "$sdk/source/graphics_gx.c" "$sdk/source/performance.c" -lm -o "$scratch/native-test"
"$scratch/native-test" "$scratch/fixtures"
dotnet build tests/Pso.Smoke/Pso.Smoke.csproj -c Release
dotnet run --project "$sdk/tools/DolphinDotNet.Compiler" -- \
  --aot tests/Pso.Smoke/bin/Release/net8.0/Pso.Smoke.dll "$scratch/pso-smoke.c"
# Compile generated C separately without optimization, as in SDK CI, to keep
# sanitizer builds of generated control flow practical.
cc -pthread -O0 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" -c "$scratch/pso-smoke.c" -o "$scratch/pso-smoke.o"
cc -pthread -O2 -g -fsanitize=address,undefined -fno-sanitize-recover=all \
  -std=c11 -Wall -Wextra -Werror -I"$sdk/include" \
  "$scratch/pso-smoke.o" "$sdk/tests/model_host.c" \
  "$sdk/source/managed_runtime.c" "$sdk/source/async_runtime.c" \
  "$sdk/source/storage.c" "$sdk/source/aram.c" "$sdk/source/timing.c" "$sdk"/source/model*.c \
  "$sdk/source/graphics_device.c" "$sdk/source/draw_resources.c" \
  "$sdk/source/graphics_gx.c" "$sdk/source/performance.c" -lm -o "$scratch/pso-smoke"
timeout 30s "$scratch/pso-smoke" "$scratch/fixtures"
timeout 30s "$scratch/pso-smoke" "$scratch/fixtures" --gc-stress
dotnet build src/Viewer/Viewer.csproj -c Release
dotnet run --project "$sdk/tools/DolphinDotNet.Compiler" -- \
  --bundle src/Viewer/bin/Release/net8.0/Viewer.dll out/Viewer --platform gamecube
cc -std=c11 -Wall -Wextra -fsyntax-only -Iout/Viewer/include \
  out/Viewer/source/generated_program.c
