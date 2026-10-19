#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
bash scripts/check.sh
# The SDK owns native generation, Docker/native selection, symbols and cleanup.
# Forward trusted host CA roots; the SDK mounts this path read-only in Docker.
if [ -z "${GIT_SSL_CAINFO:-}" ] && [ -f /etc/ssl/certs/ca-certificates.crt ]; then
  export GIT_SSL_CAINFO=/etc/ssl/certs/ca-certificates.crt
fi
# Managed sessions can define this alias only in the outer container's hosts
# file. Forward the same proxy by its resolved address, never a different route.
if [ -f /etc/codex/network-policy.json ]; then
  task_proxy_ip=$(getent ahostsv4 proxy | awk 'NR==1 {print $1}' || true)
  if [ -n "$task_proxy_ip" ]; then
    for task_proxy_name in HTTP_PROXY HTTPS_PROXY http_proxy https_proxy; do
      if [ "${!task_proxy_name:-}" = 'http://proxy:8080' ]; then
        printf -v "$task_proxy_name" '%s' "http://$task_proxy_ip:8080"
        export "$task_proxy_name"
      fi
    done
  fi
fi
dotnet build src/Viewer/Viewer.csproj -c Release -p:DndBuildJobs="${DOL_BUILD_JOBS:-2}"
python3 .dependencies/dolphindotnet/scripts/dol_sections.py src/Viewer/bin/Release/net8.0/Viewer.dol
echo 'Built src/Viewer/bin/Release/net8.0/Viewer.dol'
