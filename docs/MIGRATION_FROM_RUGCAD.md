# Migration from RugCAD

## Source snapshot

- Source repository: `yldrmhasan/RugCAD`
- Source branch: `chatgpt/rugcad-work-2026-09-22`
- Source head used for migration: `e2abb825be96c3bddc1f8a47d532dd0af963e154`
- Destination repository: `yldrmhasan/RugScale`
- Destination branch: `main`

## What is moved

- `DesignDocument`, indexed `Palette`, `RugColor`
- `Rasterizer` and `CurveRasterizer`
- `DesignResizer` and all RugScale scale modes
- Motif memory/catalog/shrink/repair/topology engine
- Curve & Fill ownership, curve-style learning and Pixel-Cord replay
- Leaf / Petal classifier, medial axis, elegant arc, paired-boundary fitter/optimizer/rasterizer/lobe extraction
- Scale-related core tests
- Real-raster fixtures and audit tools/workflows
- RugScale documentation

## Namespace transition

The standalone repository uses `RugScale.Core.*`. This is intentionally a mechanical namespace split from `RugCAD.Core.*`; algorithm behaviour should not be changed merely to complete the migration.

## Technology decision

Keep C# / .NET 8 for the engine for now. The current code, pixel rasterizers, tests and training audits are already native to this stack, and switching languages during extraction would combine migration risk with algorithm risk. If profiling later shows a real hotspot, use targeted optimisation (SIMD, spans, parallel analysis, native interop) behind the stable .NET API rather than rewriting the whole engine.

## RugCAD removal gate

Do **not** delete RugScale code from RugCAD until all of these are true:

1. Standalone core builds on clean CI.
2. Unit/regression tests pass.
3. B163A motif/topology validation passes.
4. Four-design curve suite passes.
5. B996 Leaf / Petal output is manually accepted.
6. RugCAD integration is replaced by a package/reference boundary.

Only after that gate should the old RugCAD RugScale implementation be removed.
