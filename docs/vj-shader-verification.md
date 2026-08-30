# VJ shader verification record

検証日: 2026-08-24
Unity: 6000.5.9f1 (`b57deb96f08d`)
GPU: NVIDIA GeForce RTX 3060 12GB
対象: `C:\Workspaces\Unity\ShitDesigner`

## Acceptance summary

| Gate | Result | Evidence |
|---|---:|---|
| Shader import / C# compile | PASS | Unity batch compile exit 0; C# errors 0, shader errors 0, compilation failures 0, exceptions 0, unexpected compiler warnings 0 |
| VJ EditMode, D3D12 | PASS | 23/23 VJ tests; `Forcing GfxDevice: Direct3D 12` |
| VJ EditMode, Vulkan | PASS | 23/23 VJ tests; `Forcing GfxDevice: Vulkan`; Vulkan 1.1 runtime on RTX 3060 (driver supports 1.4.341) |
| All EditMode, D3D12 | PASS | 281/281 (includes C29 soak) |
| All PlayMode, D3D12 | PASS | 249/249 |
| All PlayMode, Vulkan | PASS | 249/249; Vulkan device log present |
| Standalone build | PASS | Windows Standalone build succeeded with `BuildOptions.None`; explicit graphics APIs D3D12 then Vulkan; shader stripping disabled by the acceptance configuration |
| Standalone acceptance, D3D12 | PASS | Initial, reopen, and recovery scenarios passed; save fingerprint and backup recovery fingerprint matched |
| Standalone acceptance, Vulkan | PASS | Initial, reopen, and recovery scenarios passed; save fingerprint and backup recovery fingerprint matched |
| C29 random graph soak | PASS | 30-minute run; 24/24 tests passed; 1803.0586876 s total; 5,550,953 renders; 69,830 finite probes; temporary/history lease bytes 0 |

The C29 soak used the deterministic seed `0xC290438`, random manifest graphs and chain lengths, pause/reset events, resize events, and source/ping/pong `R16G16B16A16_SFloat` textures. Every render asserted success and zero temporary leases; periodic readbacks asserted finite pixels; teardown asserted `LeasedBytes == 0`. The dedicated run completed in 1803.0586876 s; the final all-EditMode run also completed the embedded soak and finished in 1906.5998949 s with 281/281 tests passed.

## Ledger and generated asset counts

| Artifact | Count / result |
|---|---:|
| Spatial ledger | 246 variants (VJGenerator 48, VJColor 34, VJGeometry 42, VJGlitch 32, VJConvolution 28, VJEdge 38, VJKey 24) |
| Compositing/Temporal ledger | 104 variants (Blend 36, Transition 36, Temporal 32) |
| Audio/Raymarch/Utility ledger | 88 variants (Audio 30, Raymarch 30, Utility 28) |
| Total shader variants | 438 |
| `ShaderNodeManifest.asset` | 441 `typeId` entries |
| `NodeTypeCatalog.asset` | 460 `typeId` entries |
| Documentation ledgers | 438 variant rows in each generated reference |
| P0 references | 162 reference PNGs plus `contact-sheet.png` |
| VJ presets | 10 |

All ledger family variant numbers are contiguous from zero with no gaps or duplicate values. Audio and Raymarch remain `formalPriority: unclassified`; Utility has exactly 12 `phase1Support` entries. The old paths `Assets/ShitDesigner/Shaders/VJ`, `Assets/ShitDesigner/Media/Shaders`, and `Assets/ShitDesigner/Scripts/Modules/Rendering/VJ/Shaders` are absent. The temporary `SHITDESIGNER_TEST_HARNESS` define is absent from `ProjectSettings/ProjectSettings.asset` after acceptance.

## Evidence paths

The Unity runner output was directed to the following absolute temporary paths. These are the exact output locations used by the commands; the machine's temporary-artifact cleanup may remove files after a run.

- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\compile-final\compile-final.log`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\vj-editmode-d3d12-v4.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\vj-editmode-vulkan-v4.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\editmode-all-d3d12-v4.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\editmode-all-d3d12-final\editmode-all-d3d12-final.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\editmode-all-d3d12-final\editmode-all-d3d12-final.log`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\playmode-all-d3d12-v4.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\playmode-all-vulkan-v4.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\c29-v2\c29-d3d12.xml`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\c29-v2\c29-d3d12.log`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\standalone-d3d12-v4\build.log`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\standalone-acceptance-d3d12\`
- `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\standalone-acceptance-vulkan\`

The repository-relative test and implementation evidence is in:

- `Assets/ShitDesigner/Scripts/Tests/Rendering/VJ/VJAllVariantRenderProbeTests.cs`
- `Assets/ShitDesigner/Scripts/Tests/Rendering/VJ/C29RandomGraphSoakTests.cs`
- `Assets/ShitDesigner/Scripts/Modules/Rendering/ShaderPassGraphRuntimeNode.cs`
- `Assets/ShitDesigner/Shaders/Manifests/spatial-variants.json`
- `Assets/ShitDesigner/Shaders/Manifests/compositing-temporal-variants.json`
- `Assets/ShitDesigner/Shaders/Manifests/audio-raymarch-utility-variants.json`

## Warning classification

No VJ shader compile errors, C# errors, test failures, NaN/Inf readbacks, or lease failures were observed on either API. The following existing non-VJ diagnostics were kept separate from the acceptance result:

- Media/HapDecode shader warnings: negative `pow`, signed/unsigned comparison, and integer division diagnostics.
- Media playback `RenderTexture.active` release warning and H.264 color-primary metadata warning.
- UI Toolkit `No Theme Style Sheet` warnings.
- Standalone URP optional `Hidden/Universal Render Pipeline/DBufferClear` unsupported-on-this-GPU diagnostics on both APIs, plus the baseline D3D12 info-queue query message.
- Vulkan Media Foundation hardware-video-decode-disabled message; this is an expected Vulkan platform path and did not fail the acceptance fixture.

These diagnostics are outside the VJ shader families and did not appear as VJ shader errors or test failures.

## Final repository checks

- `git diff --check`: exit 0.
- Full status artifact: `C:\Users\poti\AppData\Local\Temp\ShitDesignerVerification\20260824-final-integration-v4\final-git-status.txt`.
- Unity and UnityShaderCompiler processes: none left running after the final commands.
- Final full status was captured with `git status --short --untracked-files=all`; all listed changes are the planned shader pack, runtime/manifest/catalog integration, tests, documentation, preset, and the standalone harness compatibility fix.
