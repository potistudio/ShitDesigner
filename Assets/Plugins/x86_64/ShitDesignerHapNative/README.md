# ShitDesigner Hap native boundary

`CMakeLists.txt` builds a Windows x64 DLL with the exact C ABI consumed by
`PInvokeHapNativeApi`. The checked-in C implementation parses bounded
QuickTime sample tables and decodes Hap1/Hap5/HapY/HapM frame sections,
including None/Snappy chunks and BC1/BC3/BC4 plane metadata. The native API
exposes a one-frame lease; managed code copies and releases that lease, then
converts straight sRGB RGBA to linear premultiplied RGBA.

Build and run the deterministic fixture test with Ninja:

```text
cmake -S Assets/Plugins/x86_64/ShitDesignerHapNative -B Temp/HapNativeBuild -G Ninja
cmake --build Temp/HapNativeBuild
ctest --test-dir Temp/HapNativeBuild --output-on-failure
```

`HapUnityGraphicsBridge` selects per-plane DirectCompressed, block-expanding
Compute, or CPU upload to an RGBA16F linear premultiplied RenderTexture. The
Windows x64 Release DLL is checked in beside this file with a Standalone
Windows x64 (and Editor Windows) PluginImporter configuration. Production
startup and the Editor validator call the DLL's ABI/version and capability
exports; source/CMake presence alone never enables Hap. Native CTest and the
managed source-direct smoke runner both execute all five checked-in Hap
fixtures against that DLL, including speed/loop/seek/sync and idempotent
managed teardown. HapR and Hap HDR remain explicitly unsupported.
