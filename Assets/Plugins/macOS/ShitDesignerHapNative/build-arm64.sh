#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
build="${root}/build-arm64"
cmake -S "${root}" -B "${build}" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES=arm64
cmake --build "${build}" --config Release
test -f "${build}/shitdesigner_hap.dylib"
cp "${build}/shitdesigner_hap.dylib" "${root}/shitdesigner_hap.dylib"
cp "${root}/shitdesigner_hap.dylib.meta.template" "${root}/shitdesigner_hap.dylib.meta"
echo "Built macOS arm64 Hap plugin: ${root}/shitdesigner_hap.dylib"
