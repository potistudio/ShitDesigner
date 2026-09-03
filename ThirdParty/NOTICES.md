# Third-party notices

- `BitonicPixelSorter.compute` is ported from
  [ruccho/BitonicPixelSorter](https://github.com/ruccho/BitonicPixelSorter),
  copyright (c) 2020 ruccho, under the MIT license. The complete license is in
  `BitonicPixelSorter-LICENSE.txt`.
- `System.IO.Hashing` 8.0.0 is used only by `Tools/HapFixtures` to calculate
  the required XXH3-128 fixture fingerprints. It is distributed by Microsoft
  under the MIT license: https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Hashing/LICENSE.txt
- The Hap frame constants and section layout are implemented from the Vidvox
  Hap specification. The repository's reference implementation is BSD-2-Clause:
  https://github.com/Vidvox/hap/blob/master/LICENSE
- Snappy is implemented from its published raw-format description; no Snappy
  binary or source is vendored. The format reference is BSD-style licensed by
  Google: https://github.com/google/snappy/blob/main/COPYING
- The bundled `NotoSans.ttf`, `NotoSansMono.ttf`, and `NotoSansJP.ttf` are
  sourced from [google/fonts](https://github.com/google/fonts) commit
  `ec626514f79f831f1ab848a82114a0ce7e2d6372` and distributed under the SIL
  Open Font License 1.1. The complete license is in `NotoFonts-OFL-1.1.txt`.
  The UI and Mono faces are the specified Noto Sans families; Noto Sans JP is
  their bundled Japanese fallback for user-provided Unicode labels.
