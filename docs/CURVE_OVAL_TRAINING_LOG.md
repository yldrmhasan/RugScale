# RugScale Curve / Oval Training Log

**Purpose:** persistent continuation state for Curve & Fill / RugScale oval, spiral and pixel-faithful redraw training.  
**Active training branch:** `chatgpt/curve-oval-training-2026-09-24`  
**Main policy:** do not merge this calibration branch into `main` until explicitly requested.  
**Last documented experiment:** `3ad0be4caa8f059b1360f86fc8892a52a580047d` — measured compound smoothness selector with regression tests; CI/C069 audit pending at this exact documentation point.

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

### 4.8 Sparse-taper compound fallback — ADJUST, DO NOT USE UNSCOPED

Commit:
`cdfbf289634933aae5667cfb87571c7ee5fd7992`

Change:
when a region is explicitly classified `ok-sparse-taper`, it may try the low-frequency compound
fitter even without successful mirror-source fusion. Ordinary ribbons do **not** receive this extra
authority.

C069 focus result for `83,146 - 175,348`:
- compound attempted: yes,
- compound reason: `ok`,
- p95 deviation: **1.385463 px**,
- maximum deviation: **3.453857 px**,
- curvature flips: **0**,
- selected smoothness: **0.158869**,
- candidate became accepted.

Visual result:
the intended long tapered curve started receiving geometric redraw, but the source component has
**5 skeleton endpoints**. Applying the compound fit to the whole region created visible white
cut/gap artefacts around secondary structure. Numerical safety alone did not protect multi-endpoint
ownership.

Decision: **ADJUST**. Keep the sparse-taper authority, but compound redraw must not operate on the
whole multi-endpoint region.

### 4.9 Dominant one-sided sweep scope for sparse taper — KEEP SAFETY, NEED LOWER SPECIALIZED FRACTION

Commits:
- `7652987da10784038e124c5e4fce46a33c89c6ba` — expose generic one-sided sweep extraction,
- `40a311974c0daeaf7e140fb87bd39544d402e88e` — require dominant sweep before sparse-taper compound redraw.

Result on C069 `83,146 - 175,348`:
- one-sided extractor found candidate span `98-203`,
- kept fraction: **0.409266**,
- ordinary one-sided minimum: **0.48**,
- extraction reason: `one-sided-kept-fraction`,
- compound fallback was therefore **not attempted**,
- candidate safely returned to the old unsafe macro-spline path and remained unmodified.

Visual result:
- the white cut/gap artefact from the unscoped compound experiment disappeared completely,
- direct output was **pixel-identical to the pre-compound safe baseline** (0 changed pixels).

Decision:
the scoping rule is **KEEP**. Do not return to whole-region compound redraw.
The remaining problem is only that the generic mirror-oriented 0.48 kept-fraction gate is too strict
for this separately classified sparse-taper family.

### 4.10 Sparse-taper-specific dominant-sweep fraction — KEEP

Commits:
- `ef108fa8be7bbc5a3f7c2c7ec96bb61f3111a146` — add sparse-taper-specific extractor gate,
- `f74a9c34ab5ae347b4877860a949625485fb785a` — route sparse taper through that gate.

Change:
- ordinary / mirror-fused one-sided extraction keeps the existing **0.48** minimum,
- `ok-sparse-taper` gets a separate `TryExtractSparseTaperSweep` minimum of **0.38**,
- maximum remains 0.95,
- all existing source/classifier/raster-scope gates still apply.

C069 focus result for `83,146 - 175,348`:
- main arc extracted: **yes**,
- reason: `ok-one-sided`,
- selected sample span: `96-200`,
- kept fraction: **0.405405**,
- after extraction the normal Through-Points geometric fitter became safe, so compound was no
  longer needed for this region,
- fit kind: `through-geometry / SplineThroughPoints`,
- max deviation: **1.570419 px**,
- curvature flips: **0**,
- selected smoothness: **0.019230**,
- candidate accepted: **yes**.

Visual result:
- the rectangular/white ownership damage from the unscoped experiment is gone,
- only about **500 target pixels** differ from the old safe baseline, concentrated in the intended
  sweep,
- the change is a localized improvement, not a whole-component rewrite.

Aggregate C069 round-trip exact moved from about **93.26% to 93.24%** while ±1 px stayed **99.56%**.
This tiny exact-F1 change is accepted because exact F1 is not the aesthetic objective and the source
corridor/topology gates remain intact.

Decision: **KEEP**.

### 4.11 Regression and visual-audit infrastructure — KEEP

Commits:
- `f7995b25c18110eaf21327d729f278a74b4fc4fa` — one-sided sweep extraction regression test,
- `b64ab4b57e42370ba470afa41ef8a86b6eb382cc` — persistent C069 focus artifacts.

The test protects dominant-arc extraction from accidentally keeping an opposite-curvature terminal
hook.

Every C069 audit now also emits:
- `C069_focus_left_spiral_source.bmp`
- `C069_focus_left_spiral_rugscale.bmp`
- `C069_focus_left_spiral_nearest.bmp`

These focus files are the preferred visual continuation artifacts for the user-reported left
spiral/oval problem.

### 4.12 Compound spiral configuration diagnostics — KEEP

Commits:
- `c6e400c59e4e19458e6a25de96d9ecce51b3c48b`
- `707b393984eac96c766b8e7089458af415fbf019`
- `03a77a23f7c25e05d0220c47898002225e9e3589`
- `f7746329dbc11527425804398cfb13efe6cc53de`
- `cbf5fb5ec44d793213d59446a5865459ca1d03af`
- `6d404accb098de16770ce0f1660e3fa637b22454`

Finding:
the compound score was consistently selecting **20 anchors**. The smoothest-safe audit proved that
lower-anchor candidates already exist inside the hard source corridor:

| C069 region | selected | smoothest safe | source cost |
|---|---|---|---|
| `105,181 - 191,313` | 20a/2s, rough 0.190283, p95 0.249, max 0.339 | 18a/2s, rough **0.168047**, p95 0.371, max 0.923 | small |
| `105,1065 - 191,1197` | 20a/2s, rough 0.184167, p95 0.262, max 0.448 | 16a/2s, rough **0.173056**, p95 0.525, max 1.340 | bounded |
| `105,566 - 156,812` | 20a/2s, rough 0.234319 | 18a/2s, rough 0.227841 | aesthetic gain too small |
| `240,168 - 317,288` | 20a/2s, rough 0.121662, p95 0.815, max 1.893 | 10a/2s, rough 0.064239, p95 2.368, max 3.281 | too much drift |

Conclusion:
a new spiral model is **not yet justified**. The existing candidate family contains useful smoother
solutions; selection policy was the immediate bottleneck.

### 4.13 Bounded smoother-safe compound selector — CURRENT EXPERIMENT

Commits:
- `a8fc4e4ba72c38bcb28350cc80e270619cb3bede` — selector,
- `a04d9a3729f51d27e94be0887587d4ec79885893` — fitter integration,
- `3ad0be4caa8f059b1360f86fc8892a52a580047d` — measured regression tests.

Selection rules:
- alternative anchor count may not exceed the current selection,
- roughness must improve by at least **5%**,
- p95 deviation may regress by at most **0.65 px**,
- maximum deviation may regress by at most **1.00 px**,
- both candidates already passed the compound fitter's hard safety gates.

Measured expectations encoded in tests:
- upper C069 green spiral: **select** 18-anchor smoother candidate,
- lower mirrored green family: **select** 16-anchor smoother candidate,
- gold 10-anchor underfit: **reject** because source drift is too large,
- long color-6 curve: **reject** because roughness gain is too small.

Status:
**CI / C069 / four-design validation pending at the time this entry was written.**
Actual BMP must be inspected before this selector is declared KEEP.

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
