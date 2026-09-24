# RugScale migration inventory

Source snapshot:

- repository: `yldrmhasan/RugCAD`
- branch: `chatgpt/rugcad-work-2026-09-22`
- commit: `e2abb825be96c3bddc1f8a47d532dd0af963e154`

Destination:

- repository: `yldrmhasan/RugScale`
- standalone engine namespace: `RugScale.Core.*`

## Engine code migrated

### Indexed document primitives

- `DesignDocument`
- `Palette`
- `RugColor`
- `Rasterizer`
- `CurveRasterizer`
- `DesignResizer`

### Motif / topology

- `RugScaleEngine`
- `MotifShrinkEngine`
- `MotifSourceCatalog`
- `MotifRepairEngine`
- `MotifMemory`

### Curve & Fill

- `CurveFillScaleEngine`
- `CurveFillRegionOwnershipGuard`
- `CurveScaleEngine`
- `ToolFaithfulCurveStyleLearner`
- `ToolFaithfulPixelCordOverlay`

### Leaf / Petal Arc

- `LeafPetalRegionExtractor`
- `LeafPetalArcClassifier`
- `LeafPetalMedialAxisBuilder`
- `ElegantArcFitter`
- `LeafPetalArcRasterizer`
- `LeafPetalLobeExtractor`
- `LeafPetalBoundaryCurveBuilder`
- `LeafPetalBoundaryCurveFitter`
- `LeafPetalBoundaryPairOptimizer`
- `LeafPetalBoundaryCurveRasterizer`
- `LeafPetalModels`

The algorithm files were copied mechanically from the RugCAD snapshot and namespaces changed from
`RugCAD.Core.*` to `RugScale.Core.*`. Standalone-only changes are limited to repository
separation, packaging, IO/tooling and removal of stale host naming.

## RugScale state extracted from RugCAD.App

The following items were RugScale functionality even though they lived under RugCAD.App:

- `Services/MotifMemoryStore.cs`
  - moved to `src/RugScale.Core/Services/MotifMemoryStore.cs`
  - runtime folder changed from `%AppData%/RugCAD` to `%AppData%/RugScale`
- `Assets/rugscale-motif-memory.seed.gz.b64`
  - moved to `src/RugScale.Core/Assets`
  - embedded in the core assembly so hosts do not need to deploy a loose seed file

## Validation migrated

Core tests include:

- `DesignResizerTests`
- `MotifMemoryTests`
- `MotifMemoryStoreTests`
- `ToolFaithfulCurveStyleLearnerTests`
- `LeafPetalBoundaryCurveBuilderTests`
- `LeafPetalBoundaryCurveFitterTests`
- `LeafPetalBoundaryCurveRasterizerTests`
- `LeafPetalBoundaryPairOptimizerTests`
- `RugScaleB163ARealDesignValidationTests`
- `IndexedBmpCodecTests`

## Real fixtures migrated

- B163A real indexed BMP fixture, stored in compressed/base64 parts
- B996A curve fixture
- C004A curve fixture
- C069A curve fixture
- C071C curve fixture

The fixture hashes/workflow decode checks remain authoritative.

## Tools migrated / added

- `RugScale.RugScaleAudit`
- `RugScale.CurveScaleAudit`
- `RugScale.Cli` — standalone arbitrary 8-bit indexed BMP runner
- `IndexedBmpCodec` — public lossless indexed BMP IO for tools/hosts

## GitHub Actions migrated

- standalone build/test CI
- B163A motif audit
- B163A real-design validation
- B163A drawing self-training
- four-design curve suite

## Documentation migrated

- `RUGSCALE_ENGINE.md`
- `ARCHITECTURE.md`
- `MIGRATION_FROM_RUGCAD.md`
- `HOST_INTEGRATION.md`
- this inventory

## Intentionally not copied as engine code

Two RugCAD files contain RugScale UI integration mixed with unrelated application behaviour:

- `ResizeDesignWindow.xaml(.cs)`
- `DesignCanvas.DrawingTools.cs`

They remain in RugCAD during the migration gate. RugScale algorithm/state code has been extracted
from them, but WPF preview/selection/input handling is deliberately not made a dependency of this
repository.

This is important: copying those host files would recreate the coupling that previously allowed
RugScale changes to affect unrelated RugCAD drawing tools.

After standalone validation, RugCAD should be changed so those host files call the versioned
RugScale.Core API. Only then should duplicate RugScale engine source be deleted from RugCAD.
