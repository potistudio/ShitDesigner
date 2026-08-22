The macOS configuration builds the same C ABI as the Windows decoder for
arm64 (`CMAKE_OSX_ARCHITECTURES=arm64`). It contains the same bounded MOV/Hap
decoder source. On an Apple Silicon machine or macOS arm64 CI runner, run
`./build-arm64.sh`; the script uses the local CMake/Ninja/Xcode toolchain,
checks the produced dylib, and installs its Unity PluginImporter metadata.

No macOS binary is checked in or claimed as run on this Windows host. The
Editor validator requires the resulting dylib to load and pass the ABI and
capability probe before macOS Hap is reported available.
