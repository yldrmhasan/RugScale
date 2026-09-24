# RugScale curve / enlargement real-raster fixtures

These four files are losslessly reconstructed from the indexed 8-bit BMP designs supplied for
Curve/RugScale calibration. They are test fixtures, not golden output images.

| Fixture | Raster | Quality used by audit | SHA-256 |
|---|---:|---:|---|
| C071C_BEIGE_N69.bmp | 640×1380 | 40×60 | `795b1cb0a3223624304d4ab8454955fa369dfaa0110d5d62d4ecfac3546a6775` |
| B996A_BEIGE_N69.bmp | 640×1150 | 40×50 | `a9735397f70d09b44e9f02d1e6db41cc7a9488f0ced55e58d9f48920a3df4e5c` |
| C004A_BEIGE_N69.bmp | 640×1150 | 40×50 | `5b63035e5e65bff99c63d5e1e915b5ee43a53efb58035914ab91264472c361de` |
| C069A_CREAM_N69.bmp | 640×1380 | 40×60 | `d22e56ba9a5d9cf43c8ff0e7dea599246490b6ff394be00dfade39ea5930c456` |

The CI workflow reconstructs chunked fixtures by concatenating `part*.txt`, then base64-decodes
and XZ-decompresses them. C004A is small enough to remain as one base64 file.

## Contract under test

- Output geometry follows the requested physical target dimensions.
- At the same warp/weft quality, thin curve/stroke pixel thickness must **not** multiply with
  physical size.
- When quality changes, stroke thickness follows the target/source quality ratio rather than the
  target/source physical-size ratio.
- Curve/arc topology is rebuilt from source indexed geometry; RGB interpolation is forbidden.
- No palette index absent from source may be introduced.
- Exact designer symmetry present in source must remain exact after enlargement.
- Shrink → enlarge round-trip is measured against the original raster, with Nearest included only
  as a baseline rather than as the desired drawing model.
