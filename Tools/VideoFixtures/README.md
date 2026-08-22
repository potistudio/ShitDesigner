# Deterministic Unity video fixtures

`generate.ps1` creates the small local test corpus under
`Assets/ShitDesigner/Tests/Media/Fixtures`:

- H.264/MP4, including a two-track H.264 + AAC file. The runtime records the
  audio metadata but `UnityVideoBackend` uses `VideoAudioOutputMode.None`.
- VP8/WebM with `alpha_mode=1` metadata.
- VP9/WebM (must be rejected) and a truncated MP4 (must be rejected).
- The existing Hap MOV corpus is preserved in the same manifest.

The script requires a trusted local `ffmpeg`/`ffprobe` installation. Set
`FFMPEG`/`FFPROBE` to explicit executable paths when the PATH is not trusted;
the generator never downloads or executes repository binaries. Encoding flags,
resolution, frame count, source patterns, and the tool version are recorded in
`manifest.json`. `System.IO.Hashing.XxHash128` is used for the project’s
XXH3-128 integrity value.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VideoFixtures/generate.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VideoFixtures/generate.ps1 -VerifyOnly
dotnet run --project Tools/VideoFixtures/ProbeSmoke.csproj --no-restore
```

`manifest-invalid-hash.json` is intentionally negative data. It must not be
used as the project manifest; the Media contract test proves that its expected
hash differs from the bytes it names.
