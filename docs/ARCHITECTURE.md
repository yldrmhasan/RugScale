# RugScale standalone architecture

RugScale is now developed as an independent .NET 8 engine. The migration deliberately keeps the core indexed-pixel algorithms separate from any WPF/desktop UI so scaling can be trained, benchmarked and regression-tested without risking RugCAD drawing tools.

## Projects

- `src/RugScale.Core` — indexed design model, rasterizers, resize dispatcher, motif/topology RugScale, Curve & Fill, Leaf / Petal Arc engines.
- `tests/RugScale.Core.Tests` — deterministic unit/regression tests for scale modes, motif memory, curve-family inference, leaf/petal paired-boundary fitting and separator safety.
- `tools/RugScale.RugScaleAudit` — real-raster motif/topology audit.
- `tools/RugScale.CurveScaleAudit` — real-raster Curve & Fill / Leaf-Petal audit.
- `tests/fixtures` — compressed real indexed-BMP fixtures used only for validation/training.
- `.github/workflows` — build/test and real-raster audits.

## Design principles

1. Indexed colour is authoritative. A target pixel is always a palette index from the source design.
2. Quality is physical geometry, not bitmap stretch. Source/target warp and weft densities determine pen/feature behaviour.
3. Motif/topology and Curve & Fill remain separate pipelines.
4. Leaf / Petal Arc is a specialist Curve & Fill refinement, not a replacement for motif RugScale.
5. Every learned redraw has a conservative fallback to source-graph replay.
6. Separator / Pixel-Cord colours are hard barriers; aesthetic fitting may not cross them.
7. RugScale.Core has no dependency on RugScale/RugCAD UI code.

## Migration boundary

The standalone repository was seeded from RugCAD branch `chatgpt/rugcad-work-2026-09-22`, head `e2abb825be96c3bddc1f8a47d532dd0af963e154`.

RugCAD is intentionally left unchanged until this repository is proven green. Removal from RugCAD is a separate later step.
