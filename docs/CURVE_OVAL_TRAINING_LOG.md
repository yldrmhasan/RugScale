# RugScale Curve / Oval Training Log

**Purpose:** persistent continuation state for Curve & Fill / RugScale oval, spiral and pixel-faithful redraw training.  
**Active training branch:** `chatgpt/curve-oval-training-2026-09-24`  
**Main policy:** do not merge this calibration branch into `main` until explicitly requested.  
**Last documented experiment:** `cdfbf289634933aae5667cfb87571c7ee5fd7992` — sparse-taper compound fallback; CI/audit result must be recorded below after completion.

This file is intentionally both a progress log and a **do-not-repeat list**. Future work must read it
before changing curve fitting. A visually attractive result is the authority; aggregate pixel F1 is
only a guardrail.

## 1. Current objective

The requested target is not ordinary bitmap enlargement. Curve-heavy carpet artwork must look as if
the designer redrew it at the new size:

- smooth oval / elliptical flow instead of block-scaled staircases,
- source pixel direction and Pixel-Cord drawing language preserved,
- stable ribbon thickness,
- parallel inner/outer ribbon boundaries,
- clean spiral and hook closure,
- palette indexes remain categorical,
- no unsupported colour, bridge, branch or bulge is invented.

The current C069 result is **not yet production-quality**. Metrics are good enough to protect source
geometry, but several large curves are still visibly too raster-faithful or are rejected before the
specialist redraw reaches them.

## 2. Last fully green baseline

Baseline before the current pending sparse-taper compound experiment:

- branch head: `090dd951ad460e7bba69b3c62d0ba4060b6017e0`
- RugScale CI: success
- RugScale Workbench: success
- C069 curve training: success
- four-design curve suite: success
- B163A real-design validation: success
- B163A motif audit: success
- B163A drawing self-training: success

### C069 baseline metrics

- round-trip exact: **93.26%**
- Nearest round-trip exact: **90.72%**
- round-trip ±1 px: **99.56%**
- Through-Points self-training: **59/60 accepted**
- accepted Through-Points family correctness: **59/59**
- Through-Points target exact F1: **93.54%**
- Through-Points roundness MAE: **0.023**
- direct 160% learned open curves: **33**
  - Through Points: 17
  - Bezier: 16
  - Spline: 0

These numbers are regression gates, not aesthetic proof. The user-visible BMP still contains ugly
large spirals / hooks despite high F1.

## 3. C069 focus regions

Coordinates below are source-raster coordinates from
`C069A_CREAM_N69_ribbon_candidates.csv`.

| Focus | Source bbox | Current finding |
|---|---|---|
| Long tapered ornamental sweep | `83,146 - 175,348` | Elongation 3.604, fill 0.135, path coverage 0.676, half-width mean 3.809. Originally rejected by terminal ratio 0.299. Now classified as `ok-sparse-taper`, but the old macro-spline was unsafe: max deviation 4.225, 2 curvature flips. |
| Large upper compound sweep | `105,181 - 191,313` and mirror | Compound fit accepted, but measured smoothness was about 0.190 before the new low-frequency selector weighting. Still a visual quality focus. |
| Long lower compound sweep | `105,566 - 156,812` and mirror | Compound fit accepted but roughness was about 0.234. Inspect actual BMP after every compound-selection change. |
| Narrow Through-Points curls | several color-2 / color-8 regions | Usually source-safe; do not sacrifice their pixel-following behaviour merely to improve the large curves. |

## 4. Experiments and decisions

### 4.1 Robust broad-oval height fit — KEEP

Files:
- `CurveFillBroadOvalArcFitter.cs`

Changes:
- Huber-style robust second pass for ellipse height,
- adaptive continuous sample count instead of fixed 128 samples.

Reason:
thick raster shoulders/caps can contain a few 1–2 px outliers that flatten or inflate the complete
oval under plain least squares.

Decision: **keep**.

### 4.2 Width breathing regularizer — KEEP

Files:
- `CurveFillRibbonWidthProfileRegularizer.cs`

Changes:
- source terminal width anchoring,
- forward/backward adjacent-width slope limiting,
- stable mean band weight retained.

Reason:
ribbon thickness was visibly breathing because skeleton phase alternated by one source pixel.

Decision: **keep**. Strongly tapered sweeps are intentionally not forced through this constant-width
regularizer.

### 4.3 Pre-smoothing the recovered centerline — REVERTED / DO NOT REPEAT

Experiment:
the medial source path was binomial-smoothed before the Curve-tool / geometric fitter.

Observed regression:
the broad sparse oval regression fell from approximately **84.84%** exact target geometry to
**74.50%**.

Conclusion:
the recovered source centerline is source authority. Do **not** modify it globally before fitting.
Aesthetic smoothing belongs in the selected geometric model and rasterization stage.

Decision: **reverted**.

### 4.4 Dense final geometry sampling — KEEP

Files:
- `ElegantArcFitter.cs`
- `CurveFillRibbonThroughPointsFitter.cs`
- `CurveFillRibbonBezierFitter.cs`
- `CurveFillBroadOvalArcFitter.cs`

Changes:
accepted continuous curves are sampled according to source/control-polygon length before paired
boundary polygon rasterization.

Important:
model scoring density remains stable where required; only the final accepted geometry is densified.

Observed effect:
the first C069 comparison changed only about 1.3k target pixels, so sparse sampling was **a real but
not the main** cause of the ugly large spirals.

Decision: **keep**, but do not treat it as the main curve-quality solution.

### 4.5 Compound fitter aesthetic selection — KEEP, CONTINUE CALIBRATING

Files:
- `CurveFillRibbonCompoundFitter.cs`

Previous behaviour:
safe candidates were dominated by source deviation, allowing high-anchor splines to follow residual
one-pixel skeleton staircase.

Current behaviour:
source-distance limits remain hard safety gates, but among safe candidates the score now gives real
weight to:
- `CurveFillRibbonSmoothness`,
- lower anchor count,
- lower smoothing complexity.

Goal:
prefer the low-frequency designer sweep rather than the raster staircase while remaining inside the
source corridor.

Decision: **keep under regression testing**.

### 4.6 Ribbon-shape rejection diagnostics — KEEP

Files:
- `CurveFillRibbonArcRefiner.cs`
- `tools/RugScale.CurveScaleAudit/Program.cs`

Per-candidate audit now records:
- shape rejection reason,
- mean half-width,
- width coefficient of variation,
- terminal width ratio,
- actual bend,
- required bend.

Reasons currently include:
- `ok`
- `ok-sparse-taper`
- `width-variation`
- `terminal-width-ratio`
- `insufficient-bend`

This instrumentation exposed that some visibly important C069 curves never reached the fitter.

Decision: **keep permanently**.

### 4.7 Sparse tapered designer sweep classification — KEEP

Problem:
C069 long ornamental sweep `83,146 - 175,348` had terminal ratio about 0.299, below the ordinary
constant-ribbon threshold 0.45. It was therefore treated like a leaf/petal and never redrawn.

Rejected solution:
do **not** lower the global terminal-width threshold. That would make filled leaves/petals eligible
as ribbons.

Implemented solution:
a separate `ok-sparse-taper` authority requires all of:
- terminal ratio >= 0.25,
- elongation >= 2.50,
- bounding fill <= 0.20,
- broad-arch boundary ratio gate,
- principal skeleton coverage >= 0.64.

Decision: **keep**.

### 4.8 Sparse-taper compound fallback — CURRENT EXPERIMENT

Commit:
`cdfbf289634933aae5667cfb87571c7ee5fd7992`

Change:
when a region is explicitly classified `ok-sparse-taper`, it may try the low-frequency compound
fitter even without successful mirror-source fusion. Ordinary ribbons do **not** receive this extra
authority.

Why:
the C069 focus sweep now reaches geometric fitting, but its fallback macro-spline was unsafe
(max deviation ~4.225, two curvature flips), so no specialist redraw was applied.

Status:
**pending C069/four-design audit at the time this entry was written.**
After CI finishes, update this section with:
- accepted/rejected result for `83,146 - 175,348`,
- compound p95/max deviation,
- selected smoothness,
- actual BMP visual result,
- decision KEEP / REVERT / ADJUST.

## 5. Do-not-repeat rules

1. Do not globally pre-smooth the recovered source centerline.
2. Do not globally lower ribbon terminal-ratio or width-CV gates to rescue one C069 motif.
3. Do not use exact F1 as the only definition of a good curve. A staircase can score highly.
4. Do not optimize against the target/output bitmap. All geometric authority comes from immutable
   source evidence; target output is used only for validation.
5. Do not allow a smoother candidate outside the existing source-deviation corridor.
6. Do not make C069 coordinates, palette indexes or design names production algorithm rules.
   Coordinates belong only in audits/tests.
7. Do not merge the active training branch to `main` until explicitly requested.
8. Do not remove or weaken palette/topology regression gates to improve aesthetics.

## 6. Required continuation workflow

Every meaningful training experiment should follow this order:

1. Read this file and the latest `docs/RUGSCALE_ENGINE.md`.
2. Identify one measured failure, not a vague global smoothing goal.
3. Change the narrowest responsible layer.
4. Add/update a deterministic unit or regression test where practical.
5. Run / wait for:
   - RugScale CI,
   - C069 curve training,
   - four-design curve suite.
6. For structural changes also require B163A validation/audit to stay green.
7. Inspect the **actual generated BMP**, not only summary metrics.
8. Record in this file:
   - hypothesis,
   - files/commit,
   - before/after metrics,
   - visual result,
   - KEEP / REVERT / ADJUST,
   - next unresolved issue.
9. Only then start the next experiment.

If an experiment fails, document it before reverting so a future session does not repeat it.

## 7. Next technical priorities

After the current sparse-taper experiment is resolved:

1. verify whether the C069 long tapered sweep is now actually redrawn;
2. compare large compound sweeps using visual curvature continuity, not only source deviation;
3. add a specific smooth-spiral / hook quality metric:
   - arc-length resampling,
   - curvature derivative / jerk,
   - abrupt radius-change penalty;
4. investigate Pixel-Cord / graph fallback volume without weakening source safety;
5. make pixel-step cadence measurable so "pixel faithful" means more than ±1 px distance;
6. add focused crop artifacts for the major C069 problem regions so whole-image metrics cannot hide
   local ugly curves.

## 8. Key implementation files

- `CurveFillRibbonArcRefiner.cs` — candidate gates and fitter routing
- `CurveFillRibbonCenterlineBuilder.cs` — immutable source medial evidence
- `CurveFillRibbonToolFitter.cs` — inverse RugScale Curve family
- `CurveFillRibbonThroughPointsFitter.cs` — geometric Through-Points recovery
- `CurveFillBroadOvalArcFitter.cs` — half-ellipse / broad oval
- `CurveFillRibbonBezierFitter.cs` — cubic fallback
- `CurveFillRibbonCompoundFitter.cs` — low-frequency multi-bend sweep
- `CurveFillRibbonWidthProfileRegularizer.cs` — stable ribbon thickness
- `CurveFillOutlinedRibbonRasterizer.cs` / `LeafPetalArcRasterizer.cs` — target paired-boundary raster
- `ToolFaithfulPixelCordOverlay.cs` — source Pixel-Cord / curve replay
- `CurveFillRibbonSmoothness.cs` — aesthetic low-frequency roughness metric
- `CurveOvalTrainingTests.cs` — focused training regression tests
- `RugScale.CurveScaleAudit` — real-raster diagnostics and artifacts
