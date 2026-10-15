#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Run the host checks and generate the bundle from the pinned SDK first.
bash scripts/check.sh

jobs=${DOL_BUILD_JOBS:-2}
case "$jobs" in
  ''|*[!0-9]*|0) echo 'DOL_BUILD_JOBS must be a positive integer.' >&2; exit 1 ;;
esac

if [ -n "${DEVKITPRO:-}" ] && [ -x "${DEVKITPPC:-}/bin/powerpc-eabi-gcc" ]; then
  make -C out/Viewer -j"$jobs"
else
  if ! command -v docker >/dev/null; then
    echo 'Install Docker, or configure DEVKITPRO and DEVKITPPC for libogc2.' >&2
    exit 1
  fi
  image=$(cat toolchain-image.txt)
  mounts=(--mount "type=bind,source=$PWD,target=/project")
  trust=()
  # Preserve TLS verification behind environment-managed HTTPS proxies.
  ca_bundle=${DOL_CA_BUNDLE:-/etc/ssl/certs/ca-certificates.crt}
  if [ -f "$ca_bundle" ]; then
    mounts+=(--mount "type=bind,source=$ca_bundle,target=/tmp/environment-ca.crt,readonly")
    trust+=(--env GIT_SSL_CAINFO=/tmp/environment-ca.crt)
  fi
  docker run --rm --user "$(id -u):$(id -g)" \
    "${mounts[@]}" "${trust[@]}" --env HOME=/tmp --workdir /project \
    "$image" make -C out/Viewer -j"$jobs"
fi

test -s out/Viewer/Game.dol
python3 out/Viewer/scripts/dol_sections.py out/Viewer/Game.dol
echo 'Built out/Viewer/Game.dol'
