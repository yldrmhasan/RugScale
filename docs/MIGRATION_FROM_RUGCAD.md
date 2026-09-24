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
- Portable motif-memory store and packaged seed previously owned by RugCAD.App
- Curve & Fill ownership, curve-style learning and Pixel-Cord replay
- Leaf / Petal classifier, medial axis, elegant arc, paired-boundary fitter/optimizer/rasterizer/lobe extraction
- Scale-related core tests
- Real-raster fixtures and audit tools/workflows
- Standalone indexed-BMP codec and CLI runner
- RugScale documentation

## Namespace transition

The standalone repository uses `RugScale.Core.*`. This is intentionally a mechanical namespace split from `RugCAD.Core.*`; algorithm behaviour should not be changed merely to complete the migration.

## Technology decision

Keep C# / .NET 8 for the engine for now. The current code, pixel rasterizers, tests and training audits are already native to this stack, and switching languages during extraction would combine migration risk with algorithm risk. If profiling later shows a real hotspot, use targeted optimisation (SIMD, spans, parallel analysis, native interop) behind the stable .NET API rather than rewriting the whole engine.

## RugCAD removal status

The extraction/removal is complete.

- Standalone core build and regression tests are green.
- B163A motif audit is green.
- B163A real-design validation is green.
- B163A drawing self-training is green.
- The four-design curve suite is green.
- The standalone Windows Workbench builds successfully.
- RugCAD cleanup was merged to `chatgpt/rugcad-work-2026-09-22` as
  `a1e3ab88691852969ece5dbbc77b6d829f05e33d`.
- RugCAD `main` never contained the RugScale development branch.

The original migration plan proposed switching RugCAD to a package/reference boundary before
deleting duplicate code. The final separation is stricter: RugCAD's RugScale integration was
removed entirely, and manual RugScale development/training moved to `RugScale.Workbench`.
If RugCAD consumes RugScale again later, it must do so through a versioned boundary rather than
source-copying the engine.

## Host UI boundary

RugCAD's WPF resize window and DesignCanvas input/drawing code are intentionally not copied into the
engine repository. They are host integration code and contain unrelated RugCAD behaviour. Copying
them would recreate the coupling this migration is meant to remove.

RugScale-specific state previously hidden in RugCAD.App (motif-memory store + seed) **has** been
moved. RugCAD's old RugScale WPF integration has now been removed. The standalone
`RugScale.Workbench` owns the manual feedback/training flow.
