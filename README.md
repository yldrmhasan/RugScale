# RugScale

RugScale is a standalone .NET 8 engine for **8-bit indexed carpet-design resizing and redraw**.

It was extracted from RugCAD so RugScale can be trained, benchmarked and changed without touching
RugCAD drawing tools. The engine keeps palette indexes categorical: scaling never invents blended
RGB colours that do not exist in the source design.

## What lives here

- **Motif & Topology RugScale** — motif families, branches, repeat/rapport, symmetry and topology.
- **Curve & Fill** — source-guided indexed contour/fill reconstruction.
- **Leaf / Petal Arcs** — specialist floral-curve refinement with paired-boundary fitting.
- **Tool-faithful redraw** — RugScale Curve, Pixel Cord and ellipse raster behaviour.
- **Motif learning** — portable motif memory, source catalogue, candidate repair and feedback data.
- **Real-raster training fixtures** — B163A plus the four curve-heavy validation designs.
- **Standalone Workbench** — Windows UI for indexed-BMP preview, source-motif detection, target selection and Good/Bad learning without RugCAD editor dependencies.
- **Audit tools / CI** — deterministic tests and real-design validation.

RugCAD itself is deliberately **not** a project dependency.

## Technology

The standalone engine stays on **C# / .NET 8** for now. The existing rasterizers, indexed document
model, tests and training/audit tools are already native to this stack. Rewriting the engine during
migration would mix algorithm risk with migration risk. Performance work should be targeted behind
the stable API (SIMD, spans, parallel analysis, or native interop only where profiling proves it is
worthwhile).

## Repository layout

```text
src/RugScale.Core
  Drawing/
    DesignResizer.cs
    Rasterizer.cs
    CurveRasterizer.cs
    RugScale/
  IO/
    IndexedBmpCodec.cs
  Models/
  Services/
    MotifMemoryStore.cs
  Assets/
    rugscale-motif-memory.seed.gz.b64

src/RugScale.Workbench
  App.xaml
  MainWindow.xaml
  MainWindow.xaml.cs

tests/RugScale.Core.Tests
tools/RugScale.Cli
tools/RugScale.RugScaleAudit
tools/RugScale.CurveScaleAudit
tests/fixtures
docs
.github/workflows
```

## Build and test

```bash
dotnet restore RugScale.sln
dotnet build RugScale.sln -c Release --no-restore
dotnet test tests/RugScale.Core.Tests/RugScale.Core.Tests.csproj -c Release --no-build
```

## Standalone training workbench

On Windows, RugScale can now be developed and trained without opening RugCAD:

```powershell
dotnet run --project .\src\RugScale.Workbench\RugScale.Workbench.csproj -c Release
```

The workbench opens 8-bit indexed BMPs, runs every RugScale scale mode, provides zoom/pan plus rectangular or square target selection, isolates detected source motifs, and implements the source-first feedback loop:

`Detect motif → inspect source → choose target → Preview repair → Good / learn or Bad / improve`.

**Bad / improve always redraws again from the immutable original source motif.** It does not use the previous failed result as input and it does not switch source candidates. **Different source** is the explicit action for rejecting the detected source motif.

See [Workbench](docs/WORKBENCH.md).

## Run RugScale on any indexed BMP

The standalone CLI accepts uncompressed **8-bit indexed BMP** input and writes an indexed BMP while
preserving palette indexes.

Example — the curve-heavy mode used for the B996 family:

```bash
dotnet run --project tools/RugScale.Cli -- \
  --input source.bmp \
  --output target.bmp \
  --width 800 \
  --height 1320 \
  --mode leaf-petal \
  --source-warp 40 \
  --source-weft 50 \
  --target-warp 40 \
  --target-weft 50
```

Available CLI modes:

| CLI mode | Engine mode | Use |
|---|---|---|
| `motif` | `ScaleMode.RugScale` | motif/topology designs |
| `curve-fill` | `ScaleMode.CurveFill` | outlined curved filled designs |
| `leaf-petal` | `ScaleMode.LeafPetalArcs` | elongated floral leaf/petal curves |
| `nearest` | `NearestNeighbor` | exact categorical nearest baseline |
| `edge-smooth` | `EdgeSmooth` | pixel-art enlargement |
| `dominant` | `Dominant` | majority downscale |
| `preserve-detail` | `PreserveDetail` | rarity/detail-preserving downscale |
| `smooth` | `Smooth` | indexed smooth resample |
| `area-average` | `AreaAverage` | area-based indexed downscale |

## Core API

```csharp
using RugScale.Core.Drawing;
using RugScale.Core.IO;

var source = IndexedBmpCodec.Read("source.bmp");

var target = DesignResizer.Scale(
    source.Document,
    newWidth: 800,
    newHeight: 1320,
    mode: ScaleMode.LeafPetalArcs,
    sourceWarpDensity: 40,
    sourceWeftDensity: 50,
    targetWarpDensity: 40,
    targetWeftDensity: 50);

IndexedBmpCodec.Write(
    "target.bmp",
    target,
    source.XPixelsPerMeter,
    source.YPixelsPerMeter);
```

## Motif memory

Portable learned motif knowledge now belongs to the standalone engine:

- runtime memory: `%AppData%/RugScale/rugscale-motif-memory.json`
- packaged seed: embedded in `RugScale.Core`
- import/export format: JSON
- repeated seed/import merges are idempotent

Host applications can use `RugScale.Core.Services.MotifMemoryStore`.

## Real-raster audits

### B163A motif/topology

```bash
dotnet run --project tools/RugScale.RugScaleAudit -- \
  --input B163A.bmp \
  --output artifacts/b163a
```

### Four-design curve suite

```bash
dotnet run --project tools/RugScale.CurveScaleAudit -- \
  --input-dir fixtures-decoded \
  --output artifacts/curves
```

GitHub Actions contains the reproducible fixture decode, test and audit workflows.

## Migration status

The standalone snapshot was taken from:

- source repo: `yldrmhasan/RugCAD`
- source branch: `chatgpt/rugcad-work-2026-09-22`
- source commit: `e2abb825be96c3bddc1f8a47d532dd0af963e154`

The migration includes engine code, supporting indexed-raster primitives, tests, real fixtures,
audit tools, motif-memory seed/store, the standalone Windows training workbench and documentation.

**RugCAD has not been cleaned yet.** RugScale code should be removed from RugCAD only after this
standalone repository is green and the RugCAD host has been switched to reference this engine.

See:

- [Architecture](docs/ARCHITECTURE.md)
- [Engine notes](docs/RUGSCALE_ENGINE.md)
- [Migration gate](docs/MIGRATION_FROM_RUGCAD.md)
- [Host integration boundary](docs/HOST_INTEGRATION.md)
- [Migration inventory](docs/MIGRATION_INVENTORY.md)
- [Standalone Workbench](docs/WORKBENCH.md)
- [RugCAD development-log snapshot](docs/RUGCAD_DEVELOPMENT_LOG_SNAPSHOT.md)
