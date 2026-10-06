# RugScale Workbench

`RugScale.Workbench` is the standalone Windows host for manual RugScale development and motif learning.

It intentionally does **not** reuse RugCAD's `DesignCanvas`, drawing tools, selection command stack or WPF resize window. This prevents RugScale experiments from changing Pencil/Curve/Selection behaviour in RugCAD.

## Current workflow

1. Open an uncompressed 8-bit indexed BMP.
2. Choose target pixel size, source/target warp-weft quality and a RugScale mode
   (curve-heavy, line and classic designs: **RugScale Curve — neutral-first**;
   abstract / distressed designs: **RugScale Texture — abstract / distressed**;
   roll / wall-to-wall designs: **Wall to Wall — rulo / rapor tekrarı**, see below).
3. Run the non-destructive preview.
4. For motif/topology shrink training:
   - choose **Select broken motif** and drag a rectangle,
   - optionally enable **Square selection**,
   - press **Detect motif**,
   - inspect the isolated source motif on the right,
   - choose a target rectangle or **Use detected area**,
   - press **Preview repair**,
   - **Good / learn** accepts the repair and writes motif/style feedback,
   - **Bad / improve** keeps the same immutable source motif and tries the next redraw strategy,
   - **Different source** is only for a genuinely wrong source candidate.
5. Save the current preview as an indexed BMP.

The workbench uses the same portable `MotifMemoryStore` as the engine. Runtime memory is stored under `%AppData%\RugScale`.

## Build

```powershell
dotnet build .\src\RugScale.Workbench\RugScale.Workbench.csproj -c Release
```

The dedicated `RugScale Workbench` GitHub Actions workflow builds it on `windows-latest`.

## Boundary

The workbench is a RugScale-only host. General RugCAD editor responsibilities (Pencil, Airbrush, Curve tools, palette editing, MDI, workspace/docking, editor Undo/Redo) do not belong here.

## Wall to Wall / rulo (rapor tekrarı)

1. Mode: **Wall to Wall — rulo / rapor tekrarı**.
2. **Raporu otomatik bul**: the rapport (repeat unit) is detected and drawn on the source (period
   across / along, drop, technical edge marker columns), or **Rapor alanı seç** and drag the area
   to repeat on the source shown in the canvas.
3. **Tekrar yönü**: *Her ikisi* (tile), *Enden* (repeat across the width; rows come from the source)
   or *Boydan* (repeat along the length; columns come from the source).
4. **Kaydırma (drop, px)**: vertical offset of every next repeat across (half-drop = rapport height / 2).
5. **Dikişsiz birleştir**: joins a rapport whose opposite edges do not meet along the minimum-mismatch
   path (period unchanged when the source has content outside the rapport, otherwise shortened by
   the 12 px band).
6. Enter the target width / height and **Run**. The panel reports the period used and the seam ratio
   per direction (about 1 = invisible; above 1.5 a warning: e.g. a design made to repeat only along
   the length).

