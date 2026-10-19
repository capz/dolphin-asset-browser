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
