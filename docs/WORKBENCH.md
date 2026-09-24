# RugScale Workbench

`RugScale.Workbench` is the standalone Windows host for manual RugScale development and motif learning.

It intentionally does **not** reuse RugCAD's `DesignCanvas`, drawing tools, selection command stack or WPF resize window. This prevents RugScale experiments from changing Pencil/Curve/Selection behaviour in RugCAD.

## Current workflow

1. Open an uncompressed 8-bit indexed BMP.
2. Choose target pixel size, source/target warp-weft quality and a RugScale mode.
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
