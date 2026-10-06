# RugScale Curve / Oval Training Log

**Purpose:** persistent continuation state for Curve & Fill / RugScale oval, spiral and pixel-faithful redraw training.  
**Active training branch:** `chatgpt/curve-oval-training-2026-09-24`  
**Main policy:** do not merge this calibration branch into `main` until explicitly requested.  
**Last documented experiment:** section 4.53 — Wall to Wall: rapport opening (rapor açma) and sharper period detection (B317B, B390A).

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

### 4.12 Root-cause discovery: boundary smoothing is not true curve redraw — ARCHITECTURAL FINDING

User validation of the latest BMP showed that curve geometry was still visibly nonsensical despite
accepted fits and high pixel metrics.

Code inspection exposed the underlying reason:

- `LeafPetalArcRasterizer.Apply` only edits cells near the original source boundary
  (`BoundaryBandRadius = 2.0` source pixels),
- `CurveFillOutlinedRibbonRasterizer.TryApply` also limits both fill and outline edits to local
  source-boundary corridors,
- topology-anchor checks intentionally keep many old staircase pixels.

Therefore even a mathematically good fitted centerline cannot become the authoritative target
shape. The pipeline was still fundamentally **resizing first, then touching up the edge**.

This explains the user's repeated observation that the result looks resized rather than redrawn.

Decision:
**do not continue trying to solve this only by fitter thresholds / smoothing parameters.**
A separate authoritative raster path is required.

### 4.13 Authoritative fitted-ribbon redraw — CURRENT EXPERIMENT

Commits:
- `f2e0b678909221ee4ecac47a41ff910babb56eff` — expose existing outlined-ribbon ownership helpers,
- `85c92d1759a2da629fd025b83ff3518c25bc12b8` — add `CurveFillTrueRibbonRasterizer`,
- `9e3f62ca9794839477e7f6b4a22624b5c56dc87b` — route high-confidence ribbons through true redraw.

New behaviour:
- build the complete fitted fill polygon from centerline + half-width,
- build the dedicated outline polygon from the same geometry expanded by one source pixel,
- inside the fitted sweep's source-bounded tube, the **fit mask is authoritative**:
  - target fill mask -> region color,
  - target outline ring -> dominant dedicated outline color,
  - stale old block-scaled fill/outline outside the fit -> restore nearest source exterior color,
- other protected stroke roles remain hard barriers,
- if true-redraw authority is not available, the old conservative outlined/boundary rasterizers
  remain fallback paths.

Initial authority gate:
- any extracted/scoped main arc, OR
- zero curvature flips with maximum centerline deviation <= **1.75 source px**,
- plus the existing requirement that the source region has one dominant protected outline role.

Important:
this is the first experiment that can genuinely replace the old enlarged staircase with the fitted
curve instead of merely shaving it locally.

Status after commit `6890fe8a18f722ae6cdb62869735e8a7aff89052`:
- RugScale CI: **success**,
- Workbench: **success**,
- B163A real-design validation: **success**,
- B163A drawing self-training: **success**,
- B163A motif audit: **success**,
- C069 curve training: **success**,
- four-design curve suite was still running at the exact documentation point below.

C069 real-raster observation:
- round-trip exact moved to **92.83%** (Nearest **90.52%**),
- ±1 px round-trip agreement remained **99.36%**,
- direct 160% output differs from direct Nearest by about **130,604 px / 5.78%**,
- persistent left-spiral focus crop differs from Nearest by about **6,964 px / 5.67%**,
- visually, several formerly block-scaled green/gold ribbon edges now follow the fitted curve rather
  than the old nearest staircase. This is the first stage where the raster output visibly reflects
  the fitted geometry instead of only boundary touch-up.

Important:
the visual result is improved but **not yet globally correct**. Some compact spiral fills are still
untouched because they never enter the ribbon pipeline. Keep the true-redraw architecture and move
the next work to candidate coverage rather than weakening the fitted redraw itself.

Decision: **KEEP / CONTINUE**. The four-design curve suite also completed **success**, so the first
authoritative true-redraw architecture is now the fully green baseline for subsequent curve work.

### 4.14 Compact spiral ribbon coverage probe — CURRENT DIAGNOSTIC

Commit:
`016b85119a9458518615063f513fddf7f098bffb`

Finding:
some visibly curved C069 filled spirals still fail before centerline analysis because PCA elongation
is low when a curve wraps around itself. Example from the left focus area:

- color 2,
- bbox `99,219 - 148,298`,
- elongation **1.547**,
- boundary ratio **0.181**,
- bounding fill **0.502**,
- old production prefilter: rejected.

This does not prove it is a ribbon; compact filled leaves can have similar PCA statistics.

Diagnostic-only change:
- candidates with elongation >= 1.25,
- bounding fill <= 0.58,
- boundary ratio <= 0.32

are allowed to run through centerline / ribbon-shape / fitter analysis **only in
`AnalyzeCandidateStages`**.
They still receive no production redraw authority and are reported as
`compact-probe-*`.

Goal:
measure skeleton coverage, endpoints, width CV, terminal ratio, bend and safe-fit quality before
creating any production `compact spiral ribbon` class.

Do not convert this probe into a production gate until the C069 artifact proves the targeted spiral
has coherent source evidence and four-design/B163A regressions remain protected.

Probe result for the user-visible green spiral `99,219 - 148,298`:
- centerline: built,
- skeleton pixels: **204**,
- endpoints: **4**,
- principal path coverage: **0.877451**,
- width CV: **0.528132**,
- terminal ratio: **0.449972**,
- normal terminal-ratio threshold: **0.450000**,
- maximum bend: **38.082863**,
- required bend: **2.810325**.

The geometry is therefore rejected by only **0.000028** terminal-ratio difference while every other
measurement strongly indicates a coherent curved ribbon. The right-side mirror gives the same
result (terminal 0.449856, coverage 0.906863).

### 4.15 Compact spiral classifier tolerance — CURRENT DIAGNOSTIC

Commit:
`9f1e4f419370e828bbdb18f9f105089daf8d68af`

A separate `ok-compact-spiral` shape reason is now available when all of these are true:

- terminal ratio >= **0.44**,
- elongation **1.35 .. 1.80**,
- bounding fill **0.42 .. 0.56**,
- boundary ratio <= **0.25**,
- skeleton/path coverage >= **0.84**,
- width CV <= **0.54**.

This does **not** change the production prefilter. It only lets the existing diagnostic compact
probe continue past ribbon-shape rejection so the real fitter quality can be measured.

Reason for this order:
do not enable redraw merely because a threshold is close. First prove that Through-Points / Bezier /
compound geometry can fit the complete compact spiral safely and aesthetically.

Next decision after C069 audit:
- if fitter is safe and smooth, create a separately gated production compact-spiral route;
- if fitter is unsafe/high-deviation, improve spiral-specific fitting rather than loosening safety.

### 4.16 Pixel-Cord outline thickness must be target-grid based — KEEP

Commits:
- `76b1fbafd870909a51bdf297bd242a2cfcb0a62f` — target-space polygon expansion helper,
- `e15bb6d635d75adcc9389989aab87029fa25d5f0` — outlined-ribbon path uses target-pixel expansion,
- `15d0edab81e922c5af82bb4b5b263927b137c972` — true-redraw path uses target-pixel expansion,
- `01ffea3040b3229b97b89005a66b983ceea4e32d` — regression test.

Architectural correction:
a dedicated 1x1 Pixel-Cord outline is a **weave-grid stroke**, not a motif dimension. At the same
warp/weft quality, enlarging the physical design must not turn that one cell into 1.6 or 2 target
cells merely because motif coordinates scale.

Old behaviour:
`HalfWidth + 1 source pixel` was applied before scaling, so a 160% enlargement expanded the
dedicated outline by roughly 1.6 target pixels.

New behaviour:
- the filled ribbon geometry still scales normally,
- its mapped target tangent/normal is calculated,
- the dedicated outer outline is expanded by exactly **1 target pixel** in target space.

The regression test explicitly checks a 2x enlargement and requires the additional outline offset
to remain approximately 1 target pixel rather than 2.

Status:
the later combined CI at `0c12e76...` passes with this change included.

### 4.17 Compact spiral fitter selection — CURRENT DIAGNOSTIC

First classification result at `9f1e4f419370e828bbdb18f9f105089daf8d68af`:

For C069 green spiral `99,219 - 148,298`:
- shape reason: `ok-compact-spiral`,
- generic macro-spline max deviation: **6.192887 px**,
- curvature flips: 0,
- smoothness: **0.312195**,
- result: **unsafe / rejected**.

Right mirror:
- max deviation: **6.616967 px**,
- smoothness: **0.295574**,
- also unsafe.

Decision:
do **not** loosen source-deviation safety. The generic macro spline is the wrong model for a
multi-turn compact spiral.

Commit `0c12e76b9c3a45d093c7f34813e380ed5a146285` changes diagnostic fitter order for
`ok-compact-spiral` only:
1. Spline Through Points,
2. low-frequency compound fitter,
3. generic macro spline only as final diagnostic fallback.

Production prefilter remains closed. The next C069 artifact decides whether either source-bounded
fitter is good enough to justify a production compact-spiral path.

### 4.18 Low-frequency compound candidate exploration — DIAGNOSTIC ONLY

Commits:
- `6d380745ccaea19eec30d4aa054dda81f4b6bd60` — temporarily added 5-10 anchor / extra-smoothing candidates,
- `2096839db341d492eb8531598ee8d25df3a5a4e2` + `740c1ce5ce9c29a7c9a59b6468107d8dff6888e5` + `6778f1c5bb1da5552d8866153d1e453b1690b409` — report the smoothest curvature-valid candidate even when it fails the production source corridor,
- `86790e629736e8b079c667d314e4e2a03b5a3fcd` — moved low-frequency candidates to diagnostic-only so they can no longer change production output.

Result:
- the main C069 green compound spiral `105,181 - 191,313` still selects the existing 18-anchor safe fit,
- the long color-6 curve `105,566 - 156,812` has a much smoother 5-anchor candidate
  (roughness about **0.099**) but it is catastrophically far from source
  (p95 about **28.50 px**, max about **30.06 px**),
- therefore the low-anchor model family is not a legitimate production replacement for that long
  curve.

Decision:
**KEEP AS DIAGNOSTIC ONLY.** Do not re-enable these low-frequency settings as normal production
candidates merely because they look smoother.

### 4.19 Compact spiral compound proof — KEEP

Expanded C069 audit proved the previously probe-only compact spiral is actually fit safely by the
compound model.

Left source region `99,219 - 148,298`:
- path coverage: **0.877451**,
- width CV: **0.528132**,
- terminal ratio: **0.449972**,
- fit kind: `compact-compound`,
- p95 deviation: **0.827152 px**,
- max deviation: **1.660419 px**,
- curvature flips: **0**,
- selected roughness: **0.210694**,
- fit safe: **yes**.

Right mirror `492,219 - 541,298`:
- path coverage: **0.906863**,
- compound max deviation: **1.782634 px**,
- curvature flips: **0**,
- fit safe: **yes**.

These results are materially better than the earlier generic macro-spline attempt (>6 px max
deviation). This is enough source evidence to justify a separately gated production route.

### 4.20 Compact spiral production redraw — CURRENT GREEN C069 RESULT

Commit:
`61a07d1f9ff68c7b285a8b820578c0a83bd93e7d`

Production changes:
- the loose compact prefilter is used only as a doorway to build centerline evidence,
- actual production authority requires the strict existing `ok-compact-spiral` classifier,
- compact spirals use the compound fitter first,
- if compound fitting is unsafe, the region is left on the categorical baseline; no generic macro
  fallback is allowed,
- accepted compact spirals receive authoritative true redraw even when max deviation is slightly
  above the ordinary 1.75 px true-redraw threshold, because the compact classifier + compound
  safety gates are the dedicated authority.

C069 visual result:
- persistent left focus crop changed about **2,022 target pixels** versus the previous true-redraw
  baseline (**~1.65%** of the focus crop),
- the previously blocky lower green/cream spiral is visibly more rounded and follows the fitted
  spiral geometry,
- no whole-region white-gap regression reappeared.

C069 audit after enabling production:
- round-trip exact: **92.86%**,
- ±1 px: **99.45%**,
- palette: **SAFE**,
- direct stroke ratio: **0.789**.

Validation at the documentation point:
- RugScale CI: **success**,
- Workbench: **success**,
- C069 curve training: **success**,
- B163A real-design validation: **success**,
- B163A drawing self-training: **success**,
- B163A motif audit: **success**,
- four-design curve suite for this exact commit was still completing.

Decision:
**KEEP while four-design remains green.** This is the first production path specifically for the
compact spiral class.

### 4.21 Compact spiral audit/CI synchronization — CURRENT

Commits:
- `ec43e0ecc781a8b6921eba813d421829f6b1cd57` — `AnalyzeCandidateStages` now reports strict
  compact-spiral production authority as accepted instead of the obsolete `compact-probe-safe`,
- `38cef5fbfe2f35815fd495392a44452a97e3bd78` — C069 workflow regression gate.

The C069 gate requires BOTH known mirrored compact-spiral fixtures to remain:
- `ribbon_shape_reason = ok-compact-spiral`,
- `fit_kind = compact-compound`,
- `fit_safe = 1`,
- `accepted = 1`,
- curvature flips = **0**,
- maximum centerline deviation <= **1.90 px**.

This is a fixture-level regression assertion only. The production algorithm contains no C069
coordinates or design-name hard coding.

### 4.22 Pixel-Cord cadence-aware Curve-style selection — ADJUST

Evidence before enabling selection:
the synthetic Curve-tool self-training audit showed learned curves have substantially better ordered
step cadence than literal graph replay:

- Bezier cadence gain: about **+0.115**,
- Spline cadence gain: about **+0.093**,
- Spline Through Points cadence gain: about **+0.157**.

Commit:
`edb8781768a3d1115ef8dfdc563a3fa1c71a3c2b`

First selection rule:
cadence was blended directly into the raw source-fit score at 10% for Pixel-Cord and 4% for ordinary
curves.

Synthetic result versus the previous baseline:
- Through-Points accepted: **59/60 -> 60/60**,
- Through-Points family correct: **59/59 -> 60/60**,
- Through-Points target exact F1: **93.54% -> 93.61%**,
- Through-Points roundness MAE: **0.023 -> 0.015**,
- Through-Points cadence: **99.37% -> 99.59%**,
- Bezier family correctness: **10/17 -> 11/17**.

Real C069 result:
- learned open curves: **33 -> 29**,
- learned Through-Points: **17 -> 13**,
- whole direct output changed about **3,078 target pixels**,
- persistent left-spiral focus crop changed **0 pixels**,
- round-trip exact moved about **92.86% -> 92.82%**,
- ±1 px moved about **99.45% -> 99.42%**.

Visual inspection found some changed Pixel-Cord areas, but the user-reported left focus geometry was
unchanged. The main concern is architectural: cadence must not influence whether source geometry is
accepted strongly enough to reject otherwise valid learned curves.

Decision:
**ADJUST, do not keep raw-score blending as production authority.**
The cadence metric itself is valuable; move it to a small model-selection/tie-break role while
keeping raw acceptance pure geometry.

### 4.23 Cadence as bounded model-selection bonus — KEEP

Commits:
- `bb80f733e53dc3cfe54b6a960bdcddca451da9a2`,
- `d991785660b342eb956ee8998d11df292640fa32`.

New rule:
- control/roundness optimization may still use cadence to search better raster hypotheses,
- final `Score` and minimum raw acceptance threshold are **pure source geometry** again,
- polyline and Curve models both receive only a small post-geometry cadence bonus,
- maximum bonus:
  - Pixel-Cord: **0.006** model-score points,
  - ordinary curve: **0.002** model-score points,
- existing complexity penalties and model-vs-polyline gain remain intact.

Intent:
cadence decides a near tie; it cannot rescue a geometrically weak model or suppress a geometrically
valid model by changing the raw acceptance score.

Validation/result:
- RugScale CI: **success**,
- Workbench: **success**,
- C069 curve training: **success**,
- B163A real-design validation: **success**,
- B163A drawing self-training: **success**,
- B163A motif audit: **success**,
- four-design curve suite: **success**.

Compared with the earlier 10% raw-score cadence experiment, this bounded version restores the C069
learned open-curve count to **33** and Through-Points count to **17**, while keeping cadence as a
useful tie-break signal. Through-Points training remains **60/60 accepted and 60/60 family-correct**
with roundness MAE about **0.015**.

Decision: **KEEP**. Do not return cadence to the raw geometry acceptance score.

### 4.24 Short tapered-hook coverage probe — SAFE, CONTINUE DIAGNOSTIC

Commit:
`a300c7ce4dcde169c3768ef1c3f13f762b92fc3a`

User-visible focus finding:
the short navy hook near the left C069 spiral remains visually closer to a resized wedge than a
deliberately redrawn curve. Its source candidate is:

- color 4,
- bbox `99,212 - 139,239`,
- elongation **2.071920**,
- bounding fill **0.249129**,
- boundary ratio **0.402098**,
- skeleton endpoints: **2**,
- principal path coverage: **1.000000**,
- width CV: **0.513861**,
- terminal ratio: **0.163016**,
- maximum bend: **11.353804** vs required **1.583281**.

Diagnostic-only probe criteria:
- rejected specifically by `terminal-width-ratio`,
- exactly 2 skeleton endpoints,
- path coverage >= 0.95,
- elongation 1.75 .. 2.40,
- bounding fill 0.15 .. 0.32,
- boundary ratio <= 0.46,
- width CV <= 0.55,
- terminal ratio 0.10 .. 0.25,
- measured bend >= 2x required bend.

C069 probe result:
all four repeated/mirrored navy hooks became **fit-safe** while remaining production-disabled.

Top pair:
- fit kind: compound,
- max deviation: **1.027677 px**,
- curvature flips: **1**,
- smoothness: **0.080480**.

Bottom pair:
- fit kind: Through-Points,
- max deviation: **3.348360 px**,
- curvature flips: **0**,
- smoothness: **0.072020**.

Decision:
the motif class is real enough to continue, but do **not** enable production while repeated copies
choose different fitter families. First prove one common model across all repeats.

### 4.25 Common compound model for tapered hooks — PROVED / KEEP AS EVIDENCE

Commit:
`f4e7875ef207caad2c8a363180475e4182b3e867`

Change:
the diagnostic tapered-hook route tries the same low-frequency compound fitter first for every
repeated copy. It still cannot affect production output.

Result:
all four repeated/mirrored hooks are explained by the same `CompoundSpline` family within the
source safety corridor:
- upper pair p95 **0.386953**, max **1.027677**, roughness **0.080480**,
- lower pair p95 **0.483801**, max **0.919268**, roughness **0.079647**.

Decision: **KEEP AS TRAINING EVIDENCE**. This justified the separately gated production class below.

### 4.26 Strict tapered-hook production redraw — KEEP

Commit:
`d1ce1d12fb2c656a7cffe9ed31544745abcad346`

Evidence:
the common-model diagnostic proved all four repeated C069 navy hooks fit the same
`CompoundSpline` family safely:

- upper pair: p95 **0.386953**, max **1.027677**, roughness **0.080480**, 12 anchors / 1 smoothing pass,
- lower pair: p95 **0.483801**, max **0.919268**, roughness **0.079647**, 12 anchors / 3 smoothing passes.

Production authority is intentionally separate from ordinary ribbons and uses the exact narrow
source-evidence gate measured by the diagnostic probe:
- terminal-ratio rejection from the ordinary ribbon classifier,
- 2 endpoints,
- path coverage >= 0.95,
- elongation 1.75 .. 2.40,
- bounding fill 0.15 .. 0.32,
- boundary ratio <= 0.46,
- width CV <= 0.55,
- terminal ratio 0.10 .. 0.25,
- bend >= 2x required bend.

Runtime behaviour:
- tapered hooks try the compound fitter only,
- if compound safety fails, the categorical baseline is left untouched,
- there is no generic spline / Bezier fallback,
- an accepted hook receives authoritative true redraw,
- other ribbon classes and thresholds are unchanged.

Validation/result:
- the new navy hook visibly follows a clean tapered curve instead of the previous resized wedge,
- direct C069 output changed only about **1,834 pixels / 0.081%** versus the prior production baseline,
- round-trip exact remained about **92.81%**,
- ±1 px remained about **99.44%**,
- palette remained **SAFE**,
- RugScale CI: **success**,
- Workbench: **success**,
- C069 curve training: **success**,
- B163A real-design validation: **success**,
- B163A drawing self-training: **success**,
- B163A motif audit: **success**,
- four-design curve suite: **success**.

Decision: **KEEP**.

### 4.27 Tapered-hook audit/CI synchronization — CURRENT GREEN C069

Commits:
- `8063ac5aab5136b60538926568819d707e749678` — audit now mirrors the production tapered-hook authority,
- `090399eae13d43cc263c245ac9135eff4b61c272` — C069 specialist redraw gate.

Audit now reports the production family as:
- `ribbon_shape_reason = ok-tapered-hook`,
- `fit_kind = tapered-hook-compound`,
- `curve_family = CompoundSpline`,
- `fit_safe = 1`,
- `accepted = 1`,
- `status = accepted-tapered-hook`.

The C069 workflow permanently checks all four repeated/mirrored hook fixtures. It requires:
- the exact tapered-hook family above,
- no more than 1 curvature flip,
- maximum source deviation <= **1.15 px**,
- selected smoothness <= **0.095**.

C069 CI gate: **success**.
The whole production algorithm remains coordinate/design-name agnostic; coordinates exist only in
this fixture-level regression workflow.

### 4.28 Coherent variable-width sweep probe — CURRENT DIAGNOSTIC

Commit:
`55b7494c34612905b388c62aeb5751a28f860df3`

Next user-visible focus issue:
the large gold sweep around source bbox `83,128 - 178,331` has a nearly complete medial path but is
rejected before fitting only because its width variation is much larger than an ordinary constant
ribbon:

- elongation: **3.806856**,
- bounding fill: **0.107894**,
- boundary ratio: **0.245149**,
- skeleton endpoints: **3**,
- principal path coverage: **0.985294**,
- width CV: **0.708539**,
- ordinary maximum width CV: **0.55**.

No production threshold was changed.

Audit-only `probe-variable-width-sweep` requires:
- rejection specifically by `width-variation`,
- 2..4 endpoints,
- principal-path coverage >= 0.90,
- elongation >= 3.0,
- bounding fill <= 0.16,
- boundary ratio <= 0.35,
- mean half-width >= 1.5,
- width CV > 0.55 and <= 0.80.

The probe cannot be production-accepted. It only lets the existing symmetry/main-arc/fitter pipeline
measure whether the designer sweep can be redrawn safely despite genuine low-frequency width change.

First C069 probe result across the four repeated/mirrored gold sweeps:
- three copies extract a dominant one-sided main arc and select `SplineThroughPoints`,
- those three are fit-safe with max deviation about **1.78-1.95 px**, 0 curvature flips and
  smoothness about **0.033-0.041**,
- the fourth copy had two raw endpoints and skipped main-arc extraction, selecting a whole-component
  `CompoundSpline` instead (max **2.2465 px**, roughness **0.2301**).

Decision:
**do not enable production yet.** Repeat copies must not switch model families because a tiny source
branch changes endpoint count.

### 4.29 Repeat-consistent dominant sweep for variable-width family — CURRENT DIAGNOSTIC

Commit:
`6d2ccd77e0e801974f4ccb4117f8cf52f7ef85f2`

For `probe-variable-width-sweep`, one-sided main-arc extraction is now attempted even when the raw
skeleton has only two endpoints. This is diagnostic-only. The goal is to make all four repeated gold
sweeps expose the same dominant path before comparing fit quality.

### 4.30 Source-faithful variable-width profile transfer — CURRENT DIAGNOSTIC

Commits:
- `e05efc99a2c47954d1ef8e188c1e222da55c9cc2` — new
  `CurveFillVariableWidthProfileMapper`,
- `1f1bfa5cead14f5edd84e13e7b0bcce210c9c23c` — deterministic taper/noise regression test,
- `ec34501f406f3622e12ef6f0476af20be4c45c02` +
  `34663012b89a5e829ab08532348cd67ebf46d7e2` — audit diagnostics.

Architectural reason:
`CurveFillRibbonThroughPointsFitter` intentionally emits one robust constant half-width for normal
ribbons. That is correct for raster-phase breathing, but wrong for a genuine variable-width
designer sweep.

The new mapper:
- does **not** change ordinary Through-Points behaviour,
- takes width only from the immutable ordered source centerline,
- removes short local spikes with median + low-pass filtering,
- restores the source mean visual weight,
- clamps to robust 5th/95th source width bounds,
- resamples that low-frequency width profile onto the already-safe geometric centreline.

It remains diagnostic-only until all repeated gold sweeps share one safe dominant path and the
mapped profile proves smooth without collapsing the real taper.

### 4.31 Strict variable-width sweep production redraw — KEEP

Commits:
- `82a7c9b97e7b3035c4bab7bff75cabcb1cc80487` — production trial,
- `dd02ac848eb49beac9526e0dd87c1c54adff18a3` — restrict specialist redraw to >=1.50x enlargement,
- `ce752edb46e6d1526f97f4aa9e9a6bef8288611c` — explicit audit status,
- `d9675ca94d7d368dd7bfe6565527fdab69acb7ef` — permanent C069 specialist gate,
- `e3c92589dbd05d123b7aa91d7570e3621047ed14` — persistent gold-sweep visual artifacts.

All four repeated/mirrored C069 gold sweeps expose the same dominant one-sided
`SplineThroughPoints` centreline:

- top pair max deviation: **1.780271 px**, smoothness **0.041079**, 0 flips,
- bottom pair max deviation: **1.945942 px**, smoothness **0.033188**, 0 flips,
- source width CV: about **0.770-0.772**,
- mapped width CV: about **0.760-0.761**,
- mapped half-width range: about **0.50 .. 7.37 px**,
- maximum adjacent mapped-width delta: about **0.156-0.161 px**.

Production authority:
- exact variable-width source-evidence classifier,
- mandatory one-sided dominant-sweep extraction,
- **Through-Points only**; no Bezier/compound/macro fallback,
- source-safe centreline,
- mapped width CV must stay inside **90% .. 105%** of source CV,
- maximum adjacent half-width delta <= **0.20 px**,
- ordinary constant-width regularizer is bypassed,
- authoritative true redraw is restricted to the extracted main sweep.

First production trial at all enlargement factors:
- direct 160% output changed only **5,309 pixels / 0.235%** versus the previous green baseline,
- visual inspection showed the gold band retained a smooth designer taper without new ownership
  cuts,
- however round-trip exact dropped **92.81% -> 92.30%** and ±1 px **99.44% -> 99.15%** because the
  specialist was also running on the moderate 80%->100% (**1.25x**) return enlargement.

Scale correction:
variable-width specialist redraw now requires **>=1.50x** enlargement. The direct 160% BMP after
this change is **pixel-identical** to the visually accepted production trial (0 changed pixels),
while the round-trip metrics return exactly to the previous green baseline:

- round-trip exact: **92.81%**,
- round-trip ±1 px: **99.44%**,
- palette: **SAFE**.

Decision: **KEEP**.
Do not remove the >=1.50x gate unless a separate moderate-scale training cohort proves that the
variable-width specialist improves rather than over-processes those resizes.

Permanent C069 workflow now protects all four repeats:
- `ribbon_shape_reason = ok-variable-width-sweep`,
- `fit_kind = variable-width-through`,
- `curve_family = SplineThroughPoints`,
- accepted + safe,
- 0 curvature flips,
- max source deviation <= **2.05 px**,
- mapper applied,
- mapped/source CV ratio inside 0.90..1.05,
- max adjacent mapped-width delta <= **0.20 px**.

Persistent visual artifacts:
- `C069_focus_gold_variable_width_source.bmp`,
- `C069_focus_gold_variable_width_rugscale.bmp`,
- `C069_focus_gold_variable_width_nearest.bmp`.

### 4.32 Tapered-hook swept-tube raster — KEEP AS SAFETY, NOT ROOT CAUSE

Commits:
- `7d7a1b18495aa467fb14402ae6e8ed98b4797552` — variable-radius target swept-tube mask,
- `4108451b3b7413bce43dbb6e7c9e662f807b90af` — tapered-hook-only routing,
- `ef025999ef2f569c8451d352ae6964887ca11432` — tight-hook no-hole regression test.

Hypothesis:
the light exterior wedge visible inside the tight navy hook might come from self-intersection of the
paired left/right offset polygon at a hairpin.

Change:
tapered hooks now rasterize fill/outline as a union of variable-radius centreline capsules instead
of relying on polygon winding. Other ribbon families keep the paired-boundary polygon raster.

Result:
- synthetic tight-hook mask contains **0 enclosed background holes**,
- C069 CI and the specialist gates remain green,
- hook focus crop changed about **185 target pixels / 1.95%** versus the prior polygon raster,
- the visible light wedge was **not materially removed**.

Decision:
**KEEP as a topological safety improvement**, but record that polygon self-intersection was not the
main cause of the user-visible hook indentation. Do not keep tuning tube radius blindly.

### 4.33 Tapered-hook source width transfer — KEEP SOURCE AUTHORITY / NOT ROOT CAUSE

Commits:
- `36c54ba13ed515d5049a75290fa428f478e83b4c` — width-transfer diagnostic,
- `c3925fb1fc75e5a42c2e06d027c8b9389ac7634f` — large-scale hook uses source-faithful taper.

Measured source/mapped profiles for all four C069 repeats:
- source width CV: **0.525 .. 0.544**,
- mapped width CV: **0.500 .. 0.513**,
- source max half-width: about **4.47 .. 4.49 px**,
- mapped max: about **4.12 .. 4.16 px**,
- maximum adjacent mapped-width delta: about **0.37 px**.

Production rule:
- source-width transfer is used for tapered hooks only at **>=1.50x** enlargement,
- mapped/source CV must remain 0.90..1.05,
- max adjacent half-width delta <= 0.45,
- mapped max must retain >=85% of source max,
- mapped min may not exceed 125% of source min.

Result:
the source-taper production version changed only **47 target pixels / ~0.50%** of the persistent hook
focus crop relative to the swept-tube-only output. The visible indentation therefore did not come
primarily from compound half-width interpolation.

Decision:
**KEEP the source-faithful width transfer** because it is the correct drawing authority and remains
source-bounded, but do not treat it as the visual-hole solution.

### 4.34 High-roughness long S-sweep alternative probe — CURRENT DIAGNOSTIC

Commit:
`55af95ac4c3858a0965afb0a69917c267a1aeed2`

The worst remaining accepted centreline roughness in C069 is the repeated long color-6 S sweep:

- left bbox `105,566 - 156,812`,
- right bbox `484,566 - 535,812`,
- current fit: `CompoundSpline`,
- selected roughness: about **0.238**,
- max source deviation: about **3.18-3.19 px**,
- curvature flips: **2**.

The existing low-anchor compound diagnostics already proved that extremely simple 5-anchor fits
leave the source corridor by ~30 px, so do not revive that failed approach.

New diagnostic:
when an already-safe compound fit has smoothness >=0.20, audit a geometric
`SplineThroughPoints` alternative and report its safety/deviation/smoothness. Production output is
unchanged.

Decision after C069 audit:
- if Through-Points is source-safe and materially smoother, compare it visually and consider a
  narrow selector,
- if unsafe, keep compound and move to piecewise/curvature-continuity modelling rather than
  loosening the source corridor.

### 4.35 Piecewise C1 S-sweep fitter — REJECT / DO NOT REPEAT

Trigger:
the high-roughness color-6 long S sweeps remain one of the worst accepted centreline families in
C069. Current production compound fit is source-safe but visually too raster-faithful:

- left bbox `105,566 - 156,812`,
- right bbox `484,566 - 535,812`,
- fit: `CompoundSpline`,
- selected roughness about **0.238**,
- max source deviation about **3.18-3.19 px**,
- curvature flips: **2**.

The previous diagnostic `SplineThroughPoints` alternative is **not safe** on either repeat.

Commits:
- `d3642f6301bb7d17d0f3c1e05e8767c1765cc9b3` — piecewise fitter,
- `4ceb180420159984de6a78b100e5b4332a115da3` — audit fields,
- `4cc58acd152f7569be2792482edea5c3b7e07447` — CSV/report,
- `b691b714a1e9292d766e58f215da5e0fea1d4d6c` +
  `a19e4ce5cd56bdabf7a60468544bc5f92b57e926` — synthetic regression,
- `253a26a1ce4289d0e0748b0a1645ba721a579e84` — real join-angle measurement,
- `970be25c01eb3f9a4351954c161a0b1353a981ba` — persistent S-sweep focus artifacts.

Real C069 result:
- left: roughness **0.343533**, max **4.500355**, p95 **1.862468**, **6** curvature flips,
  join angle **30.296°**,
- right: roughness **0.352923**, max **4.077223**, p95 **1.855536**, **6** curvature flips,
  join angle **32.232°**.

This is substantially worse than the existing compound path despite acceptable source distance.

Decision: **REJECT / DO NOT REPEAT**.
Independent lobe fitting destroys the natural coupled curvature of this long ornamental S. Keep the
current compound production fit. The next experiment must smooth/fair the already-safe compound
curve **inside the source corridor**, rather than re-fitting independent halves.

### 4.36 Source-constrained compound fairing — REJECT AS ROOT-CAUSE FIX

Reason:
piecewise independent-lobe fitting failed on the real C069 long S sweep. The next hypothesis was
that the existing safe compound centreline still carried high-frequency sub-pixel curvature noise.

Commits:
- `20c0e223db9640c0cdcb5ac1e4357ed14087c549` — source-constrained Taubin fairing,
- `ed2f579f517ec9e9178a963a352ef86050004065` +
  `63751480e1f5d1b6fc2551214a9499e68481f199` — synthetic regression,
- `e25349684eedcab736740622ea42fd7f7b264a09` — audit,
- `25fc01ea23490e02ac4c0405da88968c2459bd2c` — CSV/report,
- `023c09ffeb8691728fa3d0885b7f8c456a8c63c8` — macro-scale curvature safety count.

Important test correction:
the first dense curvature counter reported **88** flips after fairing even though geometry remained
source-bounded. This was a measurement artifact from counting microscopic sign alternation at dense
sample spacing. Macro-scale stride/threshold counting fixed that false alarm; core CI returned green.

Real C069 result:

Left long S:
- roughness **0.237746 -> 0.237863**,
- p95 deviation **1.693833 px**,
- max deviation **3.191384 px**,
- maximum movement only **0.011534 px**,
- macro curvature flips **2 -> 2**.

Right long S:
- roughness **0.238304 -> 0.238416**,
- p95 deviation **1.693233 px**,
- max deviation **3.182513 px**,
- maximum movement only **0.005458 px**,
- macro curvature flips **2 -> 2**.

Decision: **REJECT AS ROOT-CAUSE FIX / DO NOT INCREASE FAIRING FORCE**.
The accepted compound curve is already locally smooth enough that constrained fairing has almost no
effect. Raising movement limits would merely trade source authority for cosmetic smoothing.

New conclusion:
the ordinary `CurveFillRibbonSmoothness` score is over-penalizing legitimate S-curve curvature
transitions. The next work must distinguish:
- real high-frequency curvature jitter within each lobe,
- legitimate low-frequency curvature reversal at the S inflection.

Do not tune the production curve from the raw ~0.238 score until an inflection-aware fairness metric
shows an actual lobe-local defect.

### 4.37 Inflection-aware lobe fairness and fairest-safe compound audit — CURRENT DIAGNOSTIC

Reason:
the long color-6 S sweep's generic smoothness score (~0.238) was initially interpreted as severe
staircase roughness. Two follow-up experiments disproved that interpretation:

- piecewise two-lobe fitting made the real C069 curve much worse,
- source-constrained fairing moved the accepted curve only ~0.006-0.012 source px and did not lower
  generic roughness.

A new S-aware metric was therefore introduced instead of forcing more smoothing.

Commits:
- `b327c81b199b088cd2bff356757af5e7b0cd6253` — `CurveFillRibbonCurvatureFairness`,
- `3205bb3405665f3f44cbf5733df739d2f1033f85` — synthetic clean-S vs jittered-S regression,
- `cc75b555ea8035d22ba66a255f1bd0db2b9ccf19` + `1fda59533e74a2b0c3d524312ccb9c63a403895b` — real-raster diagnostics,
- `5db6d58e89e98bb50d23673cc6f0c220525bcce7` — persistent sign-run inflection detection,
- `d3154a4c1c8eda26a923764cf6eca45eaa171d82` — compound fitter tracks the fairest **already-safe**
  candidate without changing production selection,
- `fb3290c5cccf04c77f843bb597f0857a848f24a3` +
  `659cfdb2d8dc1aae1fcaf477e099f6542f6dda32` — expose/report candidate diagnostics.

Metric behaviour:
- arc-length resampling,
- stable curvature-sign runs identify real S inflections,
- a short neighbourhood around each inflection is excluded,
- fairness is measured only inside same-sign curvature lobes,
- short opposite sign bursts are treated as raster/high-frequency phase rather than new lobes.

Real C069 baseline evidence before candidate comparison:

Left S:
- source lobe fairness: **0.042030**,
- selected compound fairness: **0.049598**,
- inflections: **2 -> 2**,
- source max local variation: **0.067630**,
- fit max local variation: **0.092121**.

Right S:
- source lobe fairness: **0.042549**,
- selected compound fairness: **0.049647**,
- inflections: **2 -> 2**,
- source max local variation: **0.069275**,
- fit max local variation: **0.092233**.

Conclusion:
the old ~0.238 generic score was indeed exaggerated for S geometry, but the fitted compound lobes are
still about **18% less fair than source**, so there is a small measurable target left.

Current diagnostic:
among the normal **selection-eligible + source-safe compound candidates only**, record:
- selected lobe fairness,
- fairest-safe lobe fairness,
- fairest-safe anchor/smoothing configuration,
- its generic roughness,
- p95/max source deviation.

Production selection is unchanged.

Decision gate:
- if another already-safe candidate materially approaches source lobe fairness without meaningfully
  increasing p95/max deviation, consider a narrowly scoped multi-inflection S selector;
- if the improvement is negligible, stop centreline tuning for this family and move to
  boundary/width/Pixel-Cord rendering instead.
- never revive the unsafe 5-anchor candidate (~28-30 px source error) just because its curve looks
  smoother.

Result:
the fairest already-safe candidate is effectively identical to the current production selection.

Left S:
- selected lobe fairness: **0.049598**,
- fairest-safe lobe fairness: **0.049587**,
- fairness gain: **0.000011**,
- selected config: 20 anchors / 2 smoothing passes,
- fairest-safe config: 20 anchors / 3 smoothing passes,
- p95: **1.693541 -> 1.692905 px**,
- max: **3.190097 -> 3.153208 px**.

Right S:
- selected lobe fairness: **0.049647**,
- fairest-safe lobe fairness: **0.049641**,
- fairness gain: **0.000006**,
- selected config: 20 anchors / 2 smoothing passes,
- fairest-safe config: 20 anchors / 3 smoothing passes,
- p95: **1.692942 -> 1.687693 px**,
- max: **3.181240 -> 3.137938 px**.

Decision: **STOP CENTRELINE TUNING FOR THIS FAMILY.**
The remaining visual defect is not meaningfully solvable by another source-safe centreline candidate.
Do not add more anchors, more fairing force, looser deviation thresholds or another independent-lobe
fit. Move to the coupled boundary / Pixel-Cord layers surrounding this same centreline.

### 4.38 Layered ribbon cross-section audit — CURRENT DIAGNOSTIC

Visual/source finding:
the long cyan color-6 S curve is not an isolated filled ribbon. Repeated source cross-sections contain
a stable adjacent protected-band stack, typically:

`cyan fill -> white cord (1) -> navy band (4) -> ...`

This explains why changing only the cyan fitted region cannot make the complete ornament look like
one parallel designer curve: neighbouring white/navy bands can remain on baseline geometry.

Commits:
- `232538e9177c5ebd8aa1fdaeab35601173c4b287` — source cross-section analyzer,
- `84f26406f6476774b6707664d3bff72e18e1496a` — fill-aware scanning,
- `50f9e252c2fc68904d42e984204944f733ae3bba` + `867e6208953f795732a71ce54db1e1fca1db3a1f` — candidate/audit reporting,
- `0e691caa1353f19bc9f5cffb3d23b0a0dd261e81` — synthetic layered-band regression,
- `4cf00fb3212260acee5c8885a860d05dead194ad` — null-safe short-candidate handling,
- `64486c8664b94fe1d90fd6164838619c95f2fe11` — measure band thickness in true source-normal distance rather than crossed-cell count.

First real C069 evidence before the normal-distance refinement:

Left long S:
- detected: **yes**,
- side: **negative**,
- dominant protected sequence: **1 > 4**,
- coverage: **0.884817**.

Right mirror:
- detected: **yes**,
- side: **positive**,
- dominant protected sequence: **1 > 4**,
- coverage: **0.888889**.

The side reversal is exactly what a mirrored repeat should produce, while the colour sequence remains
the same. This is strong evidence that the next redraw unit should be a **layered ribbon**, not three
independently resized colour regions.

Current status:
normal-distance width measurement + full C069/four-design audit are running. No production raster
authority has been added yet.

Next gate:
- confirm the same high sequence coverage after geometric width measurement,
- infer robust white/navy band widths from immutable source evidence,
- create a diagnostic-only layered redraw preview using the already accepted compound centreline,
- only then consider production routing.

### 4.39 Layered-ribbon preview evolution — DIAGNOSTIC / KEEP INFRASTRUCTURE

Reason:
after centreline tuning for the long color-6 S sweep was exhausted, source cross-sections proved the
ornament is not one cyan ribbon. It carries a coupled side stack. Redrawing only cyan leaves the
white/navy neighbouring layers on baseline geometry and makes the complete ornament look non-parallel.

Commits in this diagnostic sequence:
- `3fa371d8e2bf...` — one-sided layered band masks,
- `8105e139d7bc...` — layered target-band geometry tests,
- `60844959a41d...` — diagnostic layered protected-band redraw,
- `26e07aefbdea...` — isolated preview mode,
- `f5119ae5d3ec...` — C069 long-S preview artifact,
- `035e7278b2e4...` / `8c93034f0ed7...` / `98b4a98f9ce2...` / `73a6fc9226d6...` — exterior/source-ownership diagnostics,
- `4c67bdcb6677...` / `5e1383702cd4...` — overlay-only preview path when cleanup authority is absent,
- `4eb01b70e6f4...` / `27a3c8c24eb7...` / `83ac48f5c6c8...` — preview gate diagnostics,
- `5231ab271784...` / `16841da1d8dc...` / `c3bfb721d611...` / `76294c5c992e...` — permit a filled middle band and scan through it to recover full bracketing.

Persistent preview artifact:
- `C069_focus_long_s_sweep_layered_preview.bmp`.

Visual result:
the preview moves the broad navy layer with the accepted cyan compound centreline and visibly
improves parallelism versus leaving navy on nearest/baseline geometry. This validates the
**coupled-layer redraw direction**, but it is still diagnostic-only.

Important source result at commit `76294c5...`:
the recovered cross-section became a four-run stack:

`white(1) -> navy(4) -> white(1) -> background(2)`

Long-S evidence:
- left: exact full-sequence coverage **0.591623**, bracketed coverage **0.884817**,
- right: exact full-sequence coverage **0.603175**, bracketed coverage **0.888889**,
- first white band mean width about **1.26-1.28 source px**,
- navy band mean width about **4.02-4.07 source px**,
- outer white band mean width about **1.27-1.30 source px**,
- following background run about **10.84 source px** in the sampled normal window.

Why exact sequence coverage is lower than bracketed coverage:
the immutable source often reaches different exterior context after the same stable
`1>4>1` bracket. The bracket itself is substantially more stable than any single complete
four-run sequence.

Decision:
**KEEP the layered analyzer/mask/preview infrastructure.**
Do not enable production cleanup until exterior ownership is explicitly proven; overlay-only is safer
than erasing a protected band to an assumed background.

### 4.40 Bracketed exterior ownership + two-separator redraw — CURRENT EXPERIMENT

Commits:
- `dc48eb6fe4994fda9e3dbf4d5bd3027ca28618b6` — choose side using bracketed evidence and parse the
  exterior after `separator > band > separator` correctly,
- `173117488467e2269f221f9780f1fb790fe70296` — preview now draws:
  - inner 1-target-pixel separator,
  - source-scaled broad middle band,
  - outer 1-target-pixel separator,
  and permits stale-layer cleanup only with source-proven exterior ownership,
- `2362b3e1cc183bcfb2c022d0efb1a2447032b384` — regression requires the synthetic
  `1>4>1>2` stack to report exterior colour **2** with high coverage,
- `20bc22d1ecb11839046b0eda9d8cb879b3efd731` — C# definite-assignment fix for the gated exterior
  cleanup branch.

Architectural correction:
the earlier preview could clean stale navy pixels to white because it knew bracketing but had not
proved which colour actually owned the exterior. That is insufficient for production.

New rule:
- strong `1>4>1` bracket evidence may establish the layered side even if one exact complete
  sequence is just below the ordinary 0.60 dominance threshold,
- exterior ownership is read from the **fourth run** in
  `separator > band > separator > exterior`,
- stale white/navy ownership may be erased only when:
  - bracketed coverage >= **0.80**,
  - exterior coverage >= **0.60**,
  - exterior is neither separator, band nor candidate fill,
- cleanup writes the source-proven exterior colour, never an assumed separator colour,
- other protected third-colour roles remain barriers.

Status at documentation point:
- prior layered-preview branch baseline: all CI/workflows green,
- current exterior-aware regression/audit: running after the compile-only definite-assignment fix.

Acceptance gate before any production routing:
1. core test proves exterior parsing,
2. both mirrored C069 S sweeps report the same `1>4>1` bracket with mirrored side direction,
3. both report source-proven exterior ownership strongly enough for cleanup,
4. the new layered preview visually removes stale band doubling without cutting intersections,
5. C069/four-design/B163A gates remain green.

If one mirrored repeat still lacks exterior ownership, remain diagnostic/overlay-only; do not lower
the cleanup evidence thresholds simply to force symmetry.

### 4.41 Local exterior ownership for layered cleanup — CURRENT EXPERIMENT

Problem exposed by real C069:
the long S repeats share a strong `white > navy > white` bracket, but one global exterior colour is
not equally dominant along the complete sweep.

Exterior-aware C069 evidence at commit `20bc22d...`:

Left long S:
- exact `1>4>1>2` coverage: **0.591623**,
- bracketed `1>4>1` coverage: **0.884817**,
- global exterior colour: **2**,
- global exterior coverage: **0.591623**.

Right mirror:
- exact `1>4>1>2` coverage: **0.603175**,
- bracketed coverage: **0.888889**,
- global exterior colour: **2**,
- global exterior coverage: **0.603175**.

Interpretation:
the bracket is highly stable, but exterior context legitimately changes along the ornament because
other motifs approach it. Lowering the global exterior threshold just to make the left/right pair
symmetric would erase valid neighbouring ownership.

Decision:
**do not lower the global cleanup threshold.**

Commits:
- `38143fcb38af8cde16282560ab1b3bec20b66e3b` — source analyzer exposes a local bracketed-exterior
  profile for every centreline sample,
- `e650abec3db572ea15e7e48c9a6827b19e06263a` — layered preview cleanup resolves exterior from the
  nearest source centreline sample, borrowing only within a tiny +/-3 sample axial neighbourhood,
- `e5a7d98bc17d23be6d007cc95bcdc7f9a84b2d91` — regression proves stale navy cleanup works even
  when global exterior ownership is intentionally absent from the supplied profile.

Local evidence rule:
- require local `separator > band > separator > exterior`,
- exterior may not be candidate fill, separator, band or another protected stroke role,
- if one local sample lacks evidence, borrow only from +/-3 neighbouring source samples,
- if still unknown, leave the stale target pixel untouched,
- a globally proven exterior remains only a fallback, never a reason to override contradictory local
  evidence.

Why this matters:
the coupled layered redraw can now move the white/navy/white stack while respecting nearby leaf,
motif or background ownership changes along the same long S sweep.

Status:
core/C069/four-design/B163A validation pending at this documentation point.

Production rule remains unchanged:
layered-band work is still preview/diagnostic until the local-ownership artifact is visually checked
and all regression suites are green.

### 4.42 Strictly gated production layered-ribbon redraw for the long S sweeps — KEEP

Commits:
- `a78d977feb28fa7b32397e99a093258a0cf71ae5` — `CurveFillRibbonArcRefiner` runs the layered
  analyzer as a production probe for long, sparse, strongly elongated accepted ribbons and
  authorizes `CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview` inside the normal
  CurveFill path only when `LooksLikeProductionLayeredRibbon` holds,
- `7cba042731a75ef4927df7f755117dfaa91f369d` — C069 training workflow gates both mirrored long S
  rows (`(105,566,156,812)` negative side, `(484,566,535,812)` positive side).

Production gate (all required, immutable source evidence only):
- redraw scale >= 1.50,
- accepted safe `CompoundSpline` fit with exactly **2** curvature inflections and max centreline
  deviation <= 3.30 source px,
- candidate elongation 4.0–5.75, major extent >= 120 source px, bounding fill <= 0.20,
- layered profile detected with bracketed `separator > band > separator` coverage >= 0.80,
- the separator colour is a protected Pixel-Cord/outline role,
- inner/outer separator 0.65–1.65 source px, middle band 1.75–8.0 source px.

Evidence (local run of the C069 focused audit, head `7cba042` + diagnostics below):
- production CurveFill now reports **layered = 2/2 [ok]**: exactly the two mirrored long S sweeps are
  authorized and both apply; no other ribbon leaks through the gate,
- long-S focus crop: production vs layered preview differ by **15 px** (the preview applies 34/65
  candidates; the production gate deliberately excludes the other 32),
- long-S focus crop: production vs Nearest differ by **8,833 px**,
- the output is deterministic across two runs (0 px difference),
- C069 round-trip exact **92.83%**, ±1 px **99.43%**, palette SAFE, stroke x 0.781,
- all C069 workflow gates pass locally with the same script CI runs.

Visual result (long-S focus crop, source / Nearest / production):
- the navy band now follows the accepted cyan compound centreline on the whole sweep; the ornament
  reads as one parallel designer curve instead of a redrawn cyan ribbon beside a block-scaled band,
- **remaining defect:** near the top junction where the S meets the spiral, stale white separator
  fragments remain outside the redrawn navy band and a thin stale white line survives inside the
  cyan fill. These are the old separator/band positions that lie beyond the cleanup authority
  mask after the centreline moved (the fit shifts up to ~3.19 source px, while the authority mask
  reaches `separator + band + 2.0` source px from the new fill edge).

Diagnostics added after `7cba042` (this session):
- `ToolFaithfulOverlayReport.RibbonArcLayeredCandidates / RibbonArcLayeredApplied /
  RibbonArcLastLayeredReason` carry the production counters out of the refiner,
- the four-design audit prints `layered=applied/candidates [reason]` and writes
  `toolLayeredRibbonCandidates / toolLayeredRibbonApplied / toolLastLayeredRibbonReason` into
  `curve-suite-report.json`,
- the C069 workflow additionally requires `toolLayeredRibbonApplied == 2` from the JSON report, so a
  gate regression that silently drops production authority (while the CSV rows still look healthy)
  fails CI,
- `ProductionLayeredRibbonGateTests` pins the gate: it accepts the proven class and rejects one
  inflection, unsafe/too-deviating fits, moderate scale, weak bracket coverage, an unprotected
  separator, an unclosed bracket, an out-of-class band width, and short/compact regions.

Decision: **KEEP.** Production routing stays limited to this proven class.

Next unresolved issue: stale separator/band ghosts beyond the authority mask after a large
centreline shift (see section 7, item 1).

### 4.43 Layered cleanup reach follows the measured centreline shift — KEEP

Problem (from 4.42 visual inspection):
after the production layered redraw, stale white separator fragments remained outside the redrawn
navy band at the top of the long S (near the spiral junction) and along the lower hook. Cause: the
cleanup authority mask reached a fixed `separator + band + 2.0` source px beyond the NEW fill edge,
while the accepted compound centreline sits up to 3.19 source px away from the immutable source
medial axis. The old stack therefore partially lay outside cleanup reach.

Change (`CurveFillLayeredRibbonRasterizer.TryApplySecondProtectedBandPreview`):
- the authority mask is widened by `min(fit.MaximumCenterlineDeviation, 3.5)` source px — the
  shift the fit actually measured, never a global constant,
- a new **source-stack guard**: a stale target pixel may be cleaned only when its nearest source
  pixel lies within `half-width + separator + band (+ outer separator) + 1.0` source px of THIS
  ribbon's source centreline. A neighbouring motif using the same separator/band colour is never
  erased even if the wider target authority now overlaps it,
- all previous cleanup conditions remain (bracketed stack, local source-proven exterior, stale
  pixel must currently be band/separator and its source owner must be band/separator),
- `LayeredRibbonPreviewDiagnostics.StaleCleanedPixels` and
  `ToolFaithfulOverlayReport.RibbonArcLayeredStaleCleanedPixels` report how many stale pixels
  were restored; the audit prints `stale-cleaned=` for both the preview and the production run.

Regression `LayeredRibbonCleanup_ReachFollowsMeasuredCentrelineShiftAndStaysInsideOwnSourceStack`:
- scenario A: centreline moved 3 px away from the stack → the old navy/white rows beyond the fixed
  reach are restored to the source-proven exterior,
- scenario B: centreline moved 3 px towards the stack → a foreign white outline 12 source px from
  the centreline, now inside the widened target authority, survives untouched (verified to fail
  when the guard is disabled).

C069 evidence (local focused audit, same binaries as CI):
- production `layered=2/2 [ok] stale-cleaned=2,808` (both mirrored long S sweeps),
- long-S focus crop: **213 px** changed vs 4.42; full 160% output: **403 px** changed,
- diff pixels lie exclusively along the outer navy edge: ghost white dashes outside the band at the
  spiral top-right and on the lower hook are gone; no fill, intersection or neighbouring motif
  pixel changed,
- round-trip exact **92.83%**, ±1 px **99.43%**, palette SAFE, stroke x 0.780 (unchanged),
- all C069 workflow gates pass locally,
- four-design suite (local, exit 0): C071C 90.09 % / B996A 91.89 % / C004A 92.60 % / C069A 92.83 %
  round-trip exact, all palette SAFE, Through-Points self-training 60/60, roundness MAE 0.015;
  production `layered=0/0 [not-attempted]` on the other three designs, so the gated class does
  not leak,
- core tests 136/136.

Still open after this change: the thin stale white line INSIDE the cyan fill at the spiral junction
(section 7, item 1). It is on the fill side, not the layered side, so it is not this pass's owner.

Decision: **KEEP.**

### 4.44 Drawing fidelity: separator gaps, lost teeth and block-scaled cords — KEEP

User feedback on the 160% comparison images (all four designs): line weight is not like the
original, outlines have gaps, and parts of the drawing are damaged. This session measured each
complaint per pipeline stage before changing anything.

New training tool: `RugScale.CurveScaleAudit --stage-diagnostics [--stage-images] [--fixture X]`.
It runs only the direct 160% Curve & Fill enlargement with a thread-static
`CurveFillScaleEngine.StageObserver` hook and reports after every stage:
- **separator breaches** — 4-adjacent pixel pairs of colours that are never 4-adjacent in the
  source (the source always keeps them apart with a Pixel-Cord). Nearest gives 0 by construction;
  every breach is a visible outline gap,
- **cord stats** per protected stroke colour — pixel count, 8-connected parts, tiny "dust" parts
  (<= 4 px) and local run-thickness shares.

Root causes found (before any change, direct 160%):

| Stage | C069 breaches | C071C | B996A | C004A |
|---|---:|---:|---:|---:|
| signed field + ownership | 0 | 0 | 4 | 8 |
| tool overlay (Pixel-Cord replay) | +91 | +192 | +585 | +311 |
| ribbon-arc refiner | **+4,142** | **+10,692** | **+7,661** | **+8,123** |

- The ribbon-arc stage also shattered the cord network (C069 33 -> 351 parts, B996A 5 -> 684,
  C004A 5 -> 1,017) and left hundreds of stray cord specks.
- `CurveFillOutlinedRibbonRasterizer` phase 1 converts EVERY region pixel outside the fitted
  ribbon (near the source boundary) to the outline colour. The cyan acanthus teeth of the C069 long
  S are therefore turned into white blobs — the "white triangles" in the screenshots.
- Cord weight: source cords are 94-95 % one pixel thick in every direction. B996A output was
  correct (cord area ratio 1.65, ideal 1.60), but C069 and C071C stayed at 2.34 (Nearest 2.54):
  `ToolFaithfulPixelCordOverlay` erases block-scaled cord residue only for a component that is
  4-connected AS A WHOLE. The main C069 cord network (86,722 px) is three 4-connected runs joined by
  diagonal joints; C071C's main network has one 1-2 px diagonal appendix. One diagonal joint disabled
  residue cleanup for the whole network, so every outline stayed ~1.5x the source weight.

Changes:
1. `CurveFillRibbonFidelityGuard` (new), applied against a snapshot taken before the stage:
   - `EnforceSeparatorBarriers` after the tool overlay and after the ribbon stage: every breach
     touching a stage-changed pixel is repaired by drawing the source separator colour (the
     protected colour both sides touch in the source) on the locally thicker side, or by reverting
     to the baseline when that is impossible. Thin bands (navy stripes) are not thinned by it,
   - `RestoreLostAppendages` after the ribbon stage: deep (>= 3 target px beyond the new region),
     compact (area <= 2.5 depth^2), narrow-rooted (base <= 2.5 depth + 2), source-backed (>= 50 %)
     lost pieces of a colour are restored with their baseline outline ring and a distance-descent
     stem back to the redrawn region; shallow or long sliver losses (genuine smoothing and
     centreline motion) stay as the stage decided,
   - `RepairColourContinuity` after the ribbon stage, for every colour: bridges pieces the stage
     split by reviving <= 10 px of the old path, and removes stage-stranded dust (<= 4 px) only when
     the replacement is a legal source neighbour.
2. `ToolFaithfulPixelCordOverlay.BuildComponents` splits trusted cord networks into their
   4-connected runs, so each run is a genuine Pixel-Cord component with residue cleanup and
   4-connected replay. Diagonal joints are re-closed by the separator guard. The same continuity
   repair (bridges + dust) also runs right after the overlay, which removes single residue cells
   of the old block-scaled cord stranded inside fills.
3. **Local contact rule.** A design-wide adjacency table is too weak: in C004A navy touches the
   pale field at one motif tip, so a missing cord along the whole navy arc was "legal". Gap
   DETECTION now requires two fill colours (neither a cord) to touch in the source within
   `LocalContactRadius` = 2 source px of the mapped location; repair FEASIBILITY keeps the global
   table (requiring local evidence for the repair itself made most separator redraws next to a
   moved boundary impossible: 900-3,900 breaches stayed unresolved). Contacts with the cord itself
   are always legal.
4. `CloseCordNotches`: a fill cell enclosed by the cord on 3-4 sides (a pale hole in a zig-zag cord,
   a navy spur through it) becomes cord unless the source has an enclosed one-cell detail within 2
   source px. This removed the "comb" look of C004A's navy arc edges.
5. Finding: `PreserveExactSourceSymmetry` copies the top half over the bottom half for exactly
   TB-symmetric sources (C004A). It was not the cause of the comb; it copied a damaged top half
   over a clean bottom half. Fix the damage before the mirror, never by changing the mirror.
6. A first attempt — morphological peeling of thick cords — was **rejected**: a synthetic staircase
   probe showed notched, spurred 8-connected lines instead of the source's 4-connected staircase.
   Redrawing cords from the source path (item 2) is the correct owner. Do not reintroduce peeling.
7. The four-design audit now reports and gates drawing fidelity (direct 160%):
   separator breaches <= 25 and cord weight ratio <= 1.90 per design (exit code 5).

Results after the changes (direct 160%, local stage diagnostics; "local" = fill-fill contacts
without source evidence within 2 px):

| Design | Breaches before -> after (local after) | Cord parts (source) before -> after | Cord weight before -> after |
|---|---|---|---|
| C069A | 4,231 -> 0 (0) | (33) 351 -> 48 | 2.34 -> 1.66 |
| C071C | 10,881 -> 0 (0) | (41) 998 -> 95 | 2.34 -> 1.76 |
| B996A | 8,106 -> 2 (1) | (5) 685 -> 25 | 1.65 -> 1.68 |
| C004A | 7,106 -> 4 (2) | (5) 772 -> 24 | 1.85 -> 1.70 |

Visual result: C069 long-S teeth are back and white blobs gone; outlines are continuous one-pixel
4-connected staircases like the source; stray white specks are gone; navy bands keep their width
where the separator is redrawn; the comb on C004A's navy arcs is gone.

Still open:
- the ribbon refiner itself remains the main source of content change; the guard repairs its
  damage after the fact. The narrowest long-term fix is to make the rasterizers appendage-aware
  instead of shaving everything outside the fit,
- B996A navy stripe along the teal petal is thinner than the source (ribbon redraw of the teal
  region takes width from the stripe),
- C004A outer green arcs still show a hatched pattern from the ribbon stage,
- cord parts are still above source counts (diagonal joints and a few unbridged gaps > 10 px).

Decision: **KEEP.**

### 4.45 Rewrite: neutral-first RugScale Curve engine — NEW DIRECTION

User verdict after 4.44 (looking at the comparison images): a neutral (nearest) resize keeps the
design better than the legacy Curve & Fill output; the ribbon/arc refitting path damages the design
and then needs ever more repair guards. The user asked to rewrite the curve mode from scratch if
needed. It was rewritten.

New engine: `NeutralCurveScaleEngine` (`ScaleMode.CurveNeutral`, CLI `--mode curve`, Workbench
"RugScale Curve — neutral-first"). About 900 lines, no fitting:
1. fills — anisotropic exact signed distance field per colour (Felzenszwalb-Huttenlocher EDT),
   bilinear sample at the target pixel centre, nearest colour gets `NeutralBias` = 0.15 source px,
   candidates limited to the 4x4 source neighbourhood;
2. cords — protected stroke colours are not resampled; every source cord cell is mapped to its
   target centre and 4-adjacent cells are joined by straight target runs (diagonal-only joints by an
   L), pen size = target/source quality ratio; genuinely wide cord areas stay in the fill layer;
3. separators — a fill/fill 4-contact without local source evidence (2 px) is repaired by moving
   the pixel to the other side, or drawing the cord;
4. exact source symmetry copy.

Results (four designs, 160% same quality, `CurveScaleAudit` default engine):

| Design | Roundtrip exact nearest / legacy / neutral | ±1 px | Breaches | Cord weight | Cord parts src/out | Direct time |
|---|---|---:|---:|---:|---|---:|
| C071C | 89.54 / 90.21 / **94.98** | 99.93 | 0 | 1.66 | 41 / 41 | 0.72 s |
| B996A | 90.84 / 92.35 / **95.69** | 99.94 | 0 | 1.65 | 5 / 5 | 0.71 s |
| C004A | 92.95 / 93.27 / **96.85** | 99.97 | 0 | 1.63 | 5 / 5 | 0.69 s |
| C069A | 90.59 / 92.81 / **95.31** | 99.90 | 0 | 1.66 | 33 / 33 | ~0.7 s |

Visual: outputs read as the source enlarged; teeth, tips and band widths are where the source has
them (B996A navy stripe keeps its width, C004A comb gone, C069 long-S teeth intact); cords are
continuous one-pixel 4-connected staircases.

Decisions:
- `CurveScaleAudit` now audits the neutral engine by default; `--engine legacy` audits Curve & Fill.
  The C069 training workflow runs with `--engine legacy` because its gates describe the legacy
  ribbon refiner.
- Legacy Curve & Fill, Leaf/Petal and all ribbon/layered training are **frozen**: keep for
  comparison, do not extend. Removing them is the user's call.
- New quality work starts from the neutral engine and must keep it neutral: any change has to stay
  within sub-pixel boundary motion of the nearest resize unless the user asks otherwise.

### 4.46 Neutral engine: cords redrawn as smooth Pixel-Cord curves — KEEP

User reference: the user drew a red Pixel-Cord line over the neutral output (C069 top medallion,
between the green and tan arcs): one pixel, 4-connected, step cadence changing gradually along the
curve. Our cell-by-cell cord replay was continuous and one pixel wide but its cadence jittered
(1,2,1,3,1) because each source stair step maps to one or two target cells depending on rounding.

Change: `PixelCordCurveRedraw` (see `RUGSCALE_ENGINE.md`): chains between junctions, stair-corner
cells dropped (8-connected centre line), [1 2 1] x10 smoothing with a 0.5 source px shift clamp and
pinned ends/corners, target rasterization as a 4-connected line choosing the axis step closest to the
curve. Measured on the sources: stair corners sit on the side closer to the curve 2:1 to 3:1, so the
rasterizer follows the source convention.

Evidence:
- a regression with a 2/5-slope staircase (source runs 2,3,2,3) first FAILED with runs
  3,4,3,3,5 (corner cells kept as curve points left 0.5 px bumps); after dropping corner cells it
  passes (all runs within one cell),
- four designs: breaches 0, cord parts = source, cord weight 1.63-1.65, palette SAFE, audit exit 0,
- round-trip exact drops from 95.7-97.9 % (cell copy) to 91.6-93.7 %; 100 % of the difference is
  cord pixels (cords follow the curve, shifted by one cell vs the source cell layout); ±1 px stays
  99.8-99.9 %. The exact metric rewards cell copying; the visual target is the user's reference.

Decision: **KEEP** (visual reference beats the exact-pixel round-trip proxy for line work).

### 4.47 Neutral engine: clean cord junctions and tips — KEEP

User feedback on the 4.46 archive (white cords recoloured red): "at tips and junctions the lines
double up, the source has nothing like that; some tips have small problems". Measured: every place
where two redrawn chains met (Y junctions, sharp tips, L corners) the pens overlapped into 2x2 cord
knots; source cords that touch (two cells wide at a junction) were traced as two parallel chains
around a one-pixel fill sliver; the cells of a source knot left 1-2 px spikes on the redrawn line;
and the TB symmetry copy cut the C004A V-apex, leaving a 6 px stranded piece (6 cord parts vs 5).

Changes (all in the neutral engine, all topology preserving):
1. `PixelCordCurveRedraw.SkeletonizeTouchingCords`: before chain tracing, source cord cells in a
   solid 2x2 block are peeled (directional sub-passes, (4,8) simple points only, endpoints kept), so
   touching cords are traced as one centre line.
2. `ThickCordMask` (cord colours allowed in the fill layer) needs a run of three cells in all four
   axes; two-cell junction spots are line work, not cord areas.
3. `ThinCordKnots` after the separator repair: a one-pixel pinhole inside a knot, or a fill sliver
   between two cords where the source cell is solid cord, joins the knot; then target cells in a 2x2
   cord block are peeled with the same simple-point rule. Kept over a solid 3x3 source cord area,
   and a peeled cell takes the commonest neighbouring fill that is a legal contact (else stays cord).
4. `PruneCordSpurs`: a cord end whose 4-connected walk reaches a junction within 2 cells is removed,
   unless the source has a cord end within 2 source cells (diamond points, real stubs stay).
5. `BridgeStrandedCordPieces` after symmetry: while a cord colour has more parts than the source, a
   piece of <= 40 px within 2 px of another piece is re-joined with a 4-connected path, then the
   symmetry copy is re-applied.

Evidence:
- new regression `Junctions_AndTips_HaveNoKnotsOrSpikes` (two staircases merging into one line):
  FAILS on the 4.46 engine (one knot), passes now (0 knots, exactly the 3 source ends),
- target 2x2 cord blocks with no source block nearby (1024 px wide outputs):

| Design | 4.46 archive | now |
|---|---:|---:|
| B996A | 227 | 1 |
| C004A | 210 | 0 |
| C069A | 306 | 3 |
| C071C | 664 | 2 |

- four-design audit: breaches 0, cord parts = source on all four (C004A back to 5), cord weight
  1.54-1.57, palette SAFE, exit 0; round-trip exact 90.5-93.2 % (-0.1..-0.3 vs 4.46, cord pixels
  only), +-1 px 99.5-99.8 %; 150 tests pass.

Decision: **KEEP**.

### 4.48 Straight diagonal line designs: exact straight sides — KEEP

User request: a study for designs drawn with straight diagonal lines (nested diamonds and chevrons
outlined with a red Pixel-Cord, quality 34x41). Their Workbench result (default "motif & topology"
mode) showed jagged diagonals: 2x2 knots and irregular steps.

Measured on a synthetic 34x41 diamond/chevron design (`run length balance` = largest difference
between sums of k consecutive per-row runs, k <= 7; a digital straight line has balance 1):

| Mode | diamond sides balance | look |
|---|---:|---|
| motif & topology | 4 | 1-3-2-1-1-3-3 runs, 2x2 knots (the user's screenshot) |
| curve neutral (4.47) | 2 | thin, but cadence drifts (local [1 2 1] smoothing only) |
| curve neutral + straight sides | 1 | exact digital straight line on all four sides |

Change: `PixelCordCurveRedraw.StraightenRuns`, after smoothing. Between two corners (or a corner and
a chain end) a side of at least 6 centre-line points is fitted with a total-least-squares line on
its core (4 points at each end excluded). If every core point is within 0.6 source px and every end
point within 1.0 px (the source raster often bends towards a corner), all points are projected onto
the line. A run of corner cells between two straight sides becomes the intersection of the two lines
(when within 2 source px), so apexes stay sharp.

Evidence:
- new regression `StraightDiagonalSides_KeepAPerfectlyRegularCadence` (34x41 diamond, 1.6x): FAILS
  on the 4.47 engine (balance 2), passes now,
- four N69 curve designs unchanged in quality: breaches 0, cord parts = source, palette SAFE, audit
  exit 0; round-trip exact rises slightly (C071C 90.48 -> 90.90, C069A 91.59 -> 92.04 %); 151 tests.

Decision: **KEEP**. Line designs must be scaled with "RugScale Curve — neutral-first", not with the
motif mode.

### 4.49 Real straight-line design A023A: straight fill edges — KEEP

User design `A023A_160X230_BS.bmp` (511x644, 160x230 cm, about 32x28 quality; Workbench target
34x41 = 544x943). Its red lines are NOT Pixel-Cords: they are thick ruled bands (about four px
across, horizontal runs of 6-7) at 43.9 degrees, a 45-degree staircase with one jog every ~19
rows. `DetectStrokePaletteRoles` therefore finds no cord colour (red 2x2 ratio 0.998), so 4.48
never applied; the bands went through the distance-field fill layer, whose zero contour wobbles
with the sampling phase (runs 3-1-3-2, balance 3).

Change: `StraightFillEdges.Snap`, right after the fill layer. Every boundary between two non-cord
colours is followed on the source as a chain of crack midpoints (mid-points of the separating cell
edges; for a digital straight edge they lie on the true line, max 0.37 px on A023A). Maximal runs
that fit one total-least-squares line within 0.55 px and are at least 10 source px long are mapped
to the target; target pixels of the two colours within 1.5 px of the line take the colour of their
side (only where all 4-neighbours are one of the two colours, one px kept free at each end).
A greedy run stopped at the first jog (runs of 15-38 points, glitches at every split), so growth
looks up to 60 points past a misfit.

Evidence (balance of the per-row runs on the four sides of the central diamond, source = 1):

| Output | motif & topology | curve 4.48 | curve + straight fill edges |
|---|---:|---:|---:|
| 34x41, 544x943 | 3 | 3 | 1 |
| 160 %, 818x1030 | 4-5 | 3 | 1 |

- new regression `ThickStraightBand_EdgesStayDigitallyStraight` (six-cell band, slope 25/26):
  FAILS without the snap (edge wobble 2.96 px), passes (<= 1 px),
- N69 curve suite identical to 4.48 (their fills are always cord-separated); 152 tests pass.

Decision: **KEEP**.

### 4.50 Classic floral design B137A: per-pixel line layer — KEEP

User design `B137A_CREAM_N58.bmp` (960x2250, 8 colours): a classic Persian-style floral field and
borders. Petals, leaves and scrolls are outlined, and the grey tracery is drawn, with one-cell
lines in colours that are also fills (navy outline and navy petal, brown stem and brown bud).
`DetectStrokePaletteRoles` finds no cord colour, so every line went through the fill layer.
Measured at 160 % (1536x3600): about 1.0 M target pixels sit in 2x2 blocks where the source line
was one cell wide (nearest 1.07 M, motif 1.09 M, curve 0.99 M; the source has 0.66 M one-cell
pixels), i.e. lines are irregularly one or two pixels wide.

Change: `LineLayer` (per pixel, independent of palette roles):
1. a line pixel belongs to no solid 2x2 block of its own colour, lies in an 8-connected run of
   >= 6 such pixels, and at least half the run touches a solid area of another colour (a one-cell
   strip of background between two parallel lines touches only lines and stays a fill);
2. `Under`: line pixels replaced by the commonest surrounding fill (edge neighbours weigh double),
   used for the whole fill layer;
3. `Render` after the cords: each line cell becomes the target pixel under its centre, joined to
   its line neighbours (straight for edge neighbours, Bresenham for a diagonal-only neighbour) and
   to the centre of any solid area of its own colour it grows out of.

Rejected on the way (do not repeat):
- redrawing these lines as smoothed chains (`PixelCordCurveRedraw`, 0.5 px shift): small motifs
  were reshaped (leaf caps rounded, a navy body split by its brown outline); classic line work must
  keep its exact cell layout,
- treating every one-cell run as a line: background strips between parallel outlines were drawn
  as lines and border motifs broke up.

Evidence (B137A, 160 %):

| | curve 4.49 | line layer |
|---|---:|---:|
| doubled one-cell line pixels | 0.99 M | 0.33 M |
| extra 8-connected parts (navy / brown / grey) | 0 / 0 / 4 | +385 / +786 / +419 |

The remaining extra parts are tiny one-cell pockets and dots of small border motifs; the old
figure of 0 came from lines fused into blobs. Visual crops: tracery, petal outlines and leaf
shapes match the source cell for cell with one-pixel lines.

- new regression `ClassicOutlines_InAFillColour_StayOnePixelAndConnected`: FAILS without the line
  layer (138 navy 2x2 blocks), passes (0 blocks, one closed outline),
- N69 suite: breaches 0, cord parts = source, audit exit 0 (C069A round-trip 92.04 -> 92.10 %);
  A023A output byte-identical; 153 tests pass.

Decision: **KEEP**.

### 4.51 Abstract / distressed designs: RugScale Texture mode — NEW MODE

User designs `B141C_BLUE_N65`, `B142C_BEIGE_N65`, `B151C_BEIGE_N65` (960x2250, 4-5 colours plus the
blue technical marker column): washed grounds, one-knot speckle, one-row scratch streaks, ragged
splotches; B141C also has a straight frame of ~12 px bands. Here the grain is the drawing. Every
pixel-mapping mode scales it: at 160 % mean run length is 1.58-1.60x the source (speckle and
streaks one or two knots thick), at 80 % 0.83-0.92x; motif mode also shifts colour proportions
when shrinking (2.66 %, B142C).

Metrics (`texstats.py`, kept with the scratch tools): colour-histogram difference; mean horizontal /
vertical run length vs source (grain; ideal 1.0x); structure = mean L1 between per-cell colour
histograms on a 24x56 grid ("fine" on 48x112).

Steps measured on the way (B142C 160 % unless noted):
- plain quilting (blocks copied 1:1 near the scaled position, min-cut seams): grain 1.01x but
  structure 3.6 % (32 px) / 2.1 % (12 px), and visible repetition (the same patch stamped in a grid),
- guided quilting (candidates within 16 px ranked by guide layout): repetition reduced, structure
  2.5 %, but B141C frame bands broke into repeated stripes (blocks cannot scale a 12 px band),
- structure / grain layers (7x7 majority = structure, scaled; grain carried 1:1 through the quilt
  map onto the same structure colour): B141C frame clean, structure 1.8 %,
- grain only from structure interiors: REJECTED (grain lost, histogram error 1.5-3.4 %),
- 5x5 structure window: REJECTED (grain coarsened to 1.6-2.1x),
- fallback to the neutral pixel where the quilt lands on another structure: histogram error
  0.05-0.68 % (was 0.4-1.6 %). KEEP.

Final (`TextureQuiltScaleEngine`, blocks 24/overlap 6, search 16, 7x7 structure):

| Design | 160 % grain (nearest) | 160 % hist / struct | 80 % grain (nearest) | 80 % hist / struct |
|---|---|---|---|---|
| B141C | 1.02-1.05x (1.58-1.60x) | 0.47 % / 1.63 % | 0.76-0.80x (0.90x) | 0.05 % / 2.09 % |
| B142C | 1.02-1.05x (1.60x) | 0.68 % / 1.95 % | 0.86-0.88x (0.89-0.92x) | 0.20 % / 2.50 % |
| B151C | 1.01-1.02x (1.60x) | 0.63 % / 1.74 % | 0.82-0.84x (0.88-0.89x) | 0.15 % / 2.40 % |

Enlarging is where the mode pays off (grain stays one knot; at 1:1 the result reads like the source,
nearest reads coarse). Shrinking is close to nearest: slightly better colour proportions, slightly
finer grain. Structure differences of 1.6-2.5 % come from the 1:1 blocks; frames and bands stay in
place. Runs in 1.5-4.5 s for 960x2250.

Tests: `TextureQuiltScaleEngineTests` (grain ratio < 1.2 while nearest > 1.4, halves stay in place
with their speckle density, technical marker column only on the target edge; shrink keeps colour
proportions); the palette theory covers `ScaleMode.Texture`. 156 tests pass.

### 4.52 Wall to Wall / roll designs: rapport detection and repeat — NEW MODE

User request: a mode for roll / wall-to-wall designs, trained on `des3_brown_hb0_kat3_v2`
(1000x1920), `H312_BEIGE_HB0_yeni` (565x2299) and `M29_HB13A_HB12_0008` (1600x1500), and in the
Workbench a selected area repeated as a rapport across (enden), along (boydan) or both.

On a roll the pattern keeps its size: a wider or longer carpet shows more repeats. So the mode
does not scale; it finds or takes a rapport and repeats it (`RapportDetector`,
`WallToWallRepeat`, `ScaleMode.WallToWall`, CLI `wall-to-wall`, Workbench panel).

Detection (share of equal pixels between x and x + shift on a 3 px grid; a period must score >= 0.75
and beat the median shift by 0.2; the smallest shift within 0.02 of the best is the fundamental):

| Design | across | along | drop | markers |
|---|---|---|---|---|
| des3 | none (whole width 1000) | 1150 (100 %) | 0 | none |
| H312 | none (whole width 565) | none (whole height 2299) | 0 | none |
| M29 | 650 (86 %) | 1239 (96 %) | -3 | 4 + 4 blue columns |

des3 holds one rapport plus 770 rows of the next; M29's 650 x 1239 rapport sits inside technical
blue marker columns, which are kept on the target edges. A half-drop repeat has no straight
horizontal period (it matches straight at twice its width), so half / third / quarter drops are
searched and the fundamental rapport with its drop is preferred (unit test with a 15 px half-drop).

Seams (mismatch across a join / mean mismatch between neighbouring columns inside the rapport;
about 1 = reads like the design):

| Design | plain repeat across / along | seamless across / along |
|---|---|---|
| des3 | 6.03 / 1.00 | 2.31 / 1.00 (warning: made to repeat along only) |
| H312 | 1.88 / 3.26 | 0.98 / 0.88 |
| M29 | 0.65 / 1.52 | 0.40 / 0.98 |

Seamless join: inside a 12 px band each row switches, along the minimum-mismatch path, between the
rapport and the source content that really continues across the join (left / right or above /
below the rapport, read with the drop). Without such content (rapport = whole design) the two ends
are overlapped and the period shortens by the band (H312: 553 x 2287). A join that is exactly the
design's own continuation is judged against the source's transition there (des3 along 3.59 -> 1.00:
a streak edge falls on the rapport border).

Rejected on the way: a join that ignored the drop (M29 across 0.65 -> 4.16), a 24-40 px band for
whole-design joins (des3 cut more visibly, H312 worse).

Tests: `WallToWallRepeatTests` (detection of periods, half-drop and markers; exact continuation and
drop when tiling; across-only and along-only repeats; seamless join lowers the seam; the
WallToWall scale mode repeats instead of scaling). 162 tests pass.

### 4.53 Wall to Wall: rapport opening (rapor açma), sharper periods — KEEP

User designs `B390A_D.BLUE_N71` (400x500) and `B317B_NAVY_N71` (400x400), and a request: some
rapports must be opened (grown across / along) before they can be repeated, e.g. B317B.

Detection fix: B390A (horizontal painterly streaks) gave a false 24 px period (82 % match): streaks
match at any small shift. A period must now be a sharp peak: at least 0.05 above the shifts 4 px
on either side. B390A -> whole width; des3 / H312 / M29 unchanged.

`RapportExpander.Expand` (CLI `--expand WxH [--expanded-output f.bmp] [--expand-block px]`,
Workbench "Raporu aç"): the original rapport stays in the top-left corner; the added area is
filled with blocks copied 1:1 from the design, chosen among the 5 best overlap matches (fixed seed)
and cut in along minimum-mismatch paths in all four bands. The canvas is a torus, so the added
area also matches the rapport's opposite edges and the opened rapport repeats without a join. Block
grids are anchored on the original's edges (first band on its end, last band on its start); the
original may only change inside its edge bands. Block size = rapport's short side / 6 (32-96):
B317B 32 px gave rectangular splotch edges, 64 px organic splotches.

| | B317B 399x400 -> 640x700 | B390A 399x500 -> 600x800 |
|---|---|---|
| direct repeat | the same motif every 400 px, a visible grid | seamless 1.18 / 1.10 |
| opened rapport | organic; repeat every 640 x 700, no visible join (ratio 1.66 / 2.18: single-row measure is noisy on this speckle) | block marks: grey rectangles and straight block edges |

Opening suits stationary textures (B317B, marble / splotch grounds); for painterly gradients with
long streaks (B390A) the block copies show, and the design is better repeated as it is.
Tests: three new (original kept away from its edge bands, own colours in similar proportions,
opened rapport joins far less than a plain repeat, smaller size rejected); 166 tests pass.

### 4.54 Rapport opening rebuilt: strokes lengthened, strips spliced — REPLACES 4.53 blocks

User report (Workbench screenshot, `B390A_COFFE_N70`, 399x500 -> 600x800): the opened rapport
"breaks", and Run brings back the old design.

Run: the target width / height stayed at the source size (400x500), so Run tiled the opened
600x800 rapport into 400x500 and showed only its corner, the old design. "Raporu aç" now raises the
target to at least the opened rapport (plus edge marker columns).

Opening: the 4.53 block synthesis compared candidates by exact pixels. On a dithered painterly
ground that rewards the right dither phase in the wrong tone, so blocks of the wrong tone landed
as grey rectangles. Tried and rejected on B390A:

| attempt | result |
|---|---|
| blocks compared by 4x4 tone, grain-shaped (wide, flat) blocks | still patchy, more fragmented |
| strip splice, 1-px-per-row seam in a 32 px band | organic, but straight vertical seams through the strokes |
| + free-form minimum cut (shortest path in the dual grid), wider band | still straight: every row must cross somewhere |
| + free opening column, both shifts | worse: the original's own width join (wrap ratio 1.29) moved inside |
| + dithered dissolve of the cut | seam becomes a noisy column, still visible |

Kept:
- `GrainOf`: stroke direction from 5x5 tone differences 8 px apart; along > 1.6 x across means
  horizontal strokes (B390A), and vice versa. Raw-pixel run lengths cannot see it: dither changes
  colour at 59 % of B390A's pixels both ways.
- Along the strokes: **stroke lengthening**. A top-to-bottom minimum-energy path (energy = tone
  difference between x - 1 and x - k, i.e. where the row continues smoothly k px further on) gets
  the k px before it inserted once more in every row (k = width / 12, 4-32). Copying a copy again
  repeats one piece side by side (a visible beat): 50x mean penalty; next to an earlier insertion
  4x. The path closes on itself (wraps along).
- Otherwise: **strip splice** at the rapport edge (or the best column when it wraps seamlessly),
  strip chosen by tone, then the 8 best re-ranked by their actual free-form cut cost; band =
  width / 6 (max 64). A strip is shifted across, or the rapport opened elsewhere, only when that
  wrap ratio is < 1.15 (1.5 let B390A's 1.29 join in). The axis whose cross-wrap is cleaner goes
  first; a splice leaves its own axis seamless. On dithered designs (pixel change rate > 0.15) the
  cut dissolves into a dithered transition of up to band / 2.

| | B390A 399x500 -> 600x800 | B317B 399x400 -> 640x700 |
|---|---|---|
| 4.53 blocks | grey rectangles, block edges | rectangular "pillars", straight edges |
| 4.54 | strokes lengthened, ovals longer, no straight lines; seams 1.46 / 1.02 (plain repeat 1.33 / 1.34: the original's own width join) | strip splice keeps the design's shapes; seams 0.94 / 1.03 (plain 2.45 / 3.19) |

CLI: `--expand-band px` (old `--expand-block` still accepted). Tests: stroke design detected as
horizontal grain, every row keeps its own darkness after widening, joins under 1.5; original kept
away from its bands (a sixth of the rapport). 167 tests pass.

### 4.55 Rapport opening: no straight lines or cuts between repeats — KEEP

User report on 4.54: "aralarda kesikler ve düz çizgiler oluşuyor" (cuts and straight lines between).

New measuring tool (scratch probe): per column / row, the mean 3x3-tone break between x - 1 and
x + 1 over the whole rapport, wrapped, relative to the columns around it (straight-line score), plus
perfectly straight colour boundaries >= 12 px.

| B390A 600x800 | vertical line score | where |
|---|---|---|
| source rapport (repeated as is) | 7.40 | x = 0, its own join across |
| 4.54 opened | 7.08 | x = 0: lengthening kept the original join |
| 4.55 | 1.39 | none (inside the design's own range) |

Changes:
- `Grow`: when the strokes are lengthened and the join across is visible (wrap ratio >= 1.15),
  the rapport is lengthened a quarter wider and `CloseWrap` overlaps both ends. On a dithered design
  the two ends are blended linearly over the whole overlap (a soft gradient like the design's own);
  otherwise a free-form cut. A 32 px dissolve read as a noisy column, a hard cut as a line.
- `PatchNoise`: transitions choose between the two sources in patches 40 x 3 px along the strokes
  (6 x 6 without strokes), so a transition reads as stroke ends; single-pixel noise looked like
  salt and pepper, 24 px patches with a smoothstep ramp lined their ends up in one column.
- Lengthening joins (row continues into its copy) are such a transition too: the tone matched,
  but the dither broke along a near-vertical line inside the ovals.
- Splice seams running along the strokes keep a clean cut, softened by 4 px of pixel noise: a wide
  patch transition broke the strokes into white dashes.
- Transitions only on really dithered designs (pixel change rate > 0.3): B317B (0.25, flat
  splotches with outlines) was breaking into speckles.
- The seed varies only among strips within 5 % of the cheapest seam (it picked the 2nd best, which
  cut a B317B splotch straight).

B317B 640x700: line scores 1.69 / 1.46 (source 1.90 / 5.94), straight edges per 10k px 59.0
(source 58.5). Test: strokes ramping across (plain join visible) opened -> join below 1.5 and below
the plain one. 168 tests pass.

### 4.56 Rapport join of stroke designs: one stroke end per row — REPLACES the 4.55 blend

User (screenshot of the 4.55 B390A join, circled): "bu rapor yeri kötü duruyor". The 4.55 join
blended both ends over a 100 px overlap in random 40 x 3 patches: no straight line, but a torn,
comb-like strip where every stroke frays.

`StrokeEnds`: every row switches from the previous repeat's tail (A) to this repeat's start (B)
exactly once, like a stroke ending and the next one starting.
- Unary: tone of A over the 6 px before the switch against B over the 6 px after (3 rows).
- Step between rows y and y + 1 switching at c and d: along |c - d| one row shows A, the other B.
  It costs only what that horizontal edge adds over the rows' own contrast (half weight), so free
  between strokes and expensive inside one (a stroke is not torn into teeth).
- A gentle pull (1.5 tone units / px) towards a place that wanders with the rows
  (sin y/23 x cos y/61, +-40 % of the overlap): with no better match the ends do not line up.
- Exact minimum by dynamic programming (prefix / suffix minima, O(rows x band)), closed along.
- On dithered designs a stroke end tapers over +-12 px of pixel noise.

| attempt | result |
|---|---|
| 4.55 patches over the overlap | torn comb strip |
| one switch per row, full step cost, overlap 99 px | strokes end bluntly on one near-vertical line |
| + half step cost, wander pull, 12 px taper | ends taper, still gathered in a ~50 px strip |
| + overlap target / 3 (200 px) | ends spread; but lengthening 400 px on a 399 px design repeated pieces (ladder) |
| overlap target / 4 (150 px) | **kept**: staggered stroke ends, no ladder |

Lengthening 351 px into a 399 px design then repeated 32 px pieces three or four times side by
side (a ladder): the old "is it a copy?" flag covered half of every row and could not steer any
more. Now every pixel carries its original column; only content that already shows twice within
3 pieces is blocked from being copied again (50x mean energy), and insertions keep away from it.

B390A 600x800: vertical line score 1.45 (source 7.40), straight colour edges 0.0 / 10k px
(source 7.8), no ladders; B317B unchanged (no strokes).
168 tests pass.

### 4.57 H312B_CREAM_BVF: vertical strokes, joins closed at the rapport's own size — KEEP

User design `H312B_CREAM_BVF_yeni` (565x1149, no markers, vertical streaks, pixel change rate
0.40 / 0.36) for Wall to Wall use. Detection: whole design (across 23.5 %, along 28.2 %).

| | joins across / along | look |
|---|---|---|
| plain repeat | 1.97 / 2.02 | visible joins |
| seamless join (12 px band, period 553 x 1137) | 0.93 / 1.00 | the pixel ratio says seamless, but the light top meets the dense purple bottom: a horizontal band at every repeat |
| opened 565x1400 (along only) | line scores 1.92 / 1.24 | bands gone; the width join stays a vertical line |
| **4.57: same size 565x1149** | line scores 1.66 / 1.26 (source 1.79 / 2.16) | no bands, no lines |

Changes:
- `Grow` closes a visible join even when the axis is not grown: along the strokes by lengthening
  a quarter and the stroke-end overlap (4.56); across the strokes by splicing a strip of the
  rapport's own content over the join alone (`Splice(wanted: 0)`, width unchanged; a cut between
  vertical strokes). "Raporu aç" at the rapport's own size therefore makes it seamless.
- The stroke-lengthening axis goes first (it needs no shift and closes its join), so the splices
  across the strokes are free to shift.
- Splice search capped at 64 shifts x 32 openings: opening H312B to 700x1500 took 57 s, now 10 s.

B390A / B317B re-checked: line scores 1.32 / 1.80 vertical, no straight colour edges on B390A.
Test: a stroke design with visible joins opened at its own size keeps its size and repeats below
1.5. 169 tests pass.

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
9. Do not thin Pixel-Cords by morphological peeling; redraw them from the source cord path
   (section 4.44). Peeling produces notched 8-connected lines. The only peeling allowed is the
   (4,8) simple-point knot cleanup of section 4.47, which keeps 4-connectivity and endpoints.
12. Do not judge junction quality by eye on whole images only: count target 2x2 cord blocks that
    have no source block nearby and short spurs without a source end (section 4.47).
10. Judge every redraw stage with `--stage-diagnostics`: a stage that adds separator breaches or
    splits cords is not finished, whatever its pixel-F1.
11. Do not add shape re-fitting (ribbon/arc/oval curve fits) to the neutral-first engine. The user
    judged re-fitted curves as damaging the design (section 4.45).

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

Neutral-first engine (section 4.45):
1. User review of the neutral engine on real production files; collect any spot where it differs
   visibly from a neutral resize in an unwanted way.
2. Quality change (different target warp/weft): verify pen size and fill scaling on a real pair.
3. Heavy shrink: measure thin-feature survival (cords are replayed, thin fills may vanish).
4. Decide with the user whether to retire legacy Curve & Fill / Leaf-Petal code and their
   training workflows.

Legacy Curve & Fill items (frozen): appendage-aware ribbon rasterizers, B996A stripe width,
C004A hatched arcs, C069 doubled inner lines.

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
- `RugScale.CurveScaleAudit` — real-raster diagnostics and artifacts (`--stage-diagnostics` for per-stage breaches/cords)
- `CurveFillRibbonFidelityGuard.cs` — separator barriers, lost appendages, colour continuity (legacy)
- `NeutralCurveScaleEngine.cs` — the neutral-first RugScale Curve engine (current)
