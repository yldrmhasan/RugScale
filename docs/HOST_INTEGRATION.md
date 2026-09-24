# RugCAD host integration boundary

RugScale must remain independently testable. RugCAD is a host, not the owner of RugScale algorithms.

## Dependency direction

Allowed:

```text
RugCAD.App
   |
   v
RugScale.Core
```

Not allowed:

```text
RugScale.Core -> RugCAD.App
RugScale.Core -> WPF
RugScale.Core -> RugCAD drawing-tool state
```

This boundary is the reason the standalone repository does not copy
`ResizeDesignWindow.xaml(.cs)` or `DesignCanvas.DrawingTools.cs` as engine source. Those files mix
general RugCAD UI/drawing behaviour with RugScale hosting. Copying them would recreate the coupling
that caused unrelated drawing tools to regress while RugScale was being developed.

## What moved out of RugCAD UI

RugScale-specific non-UI state that previously lived under RugCAD.App has been extracted:

- portable motif-memory persistence -> `RugScale.Core.Services.MotifMemoryStore`
- packaged motif-memory seed -> embedded `RugScale.Core` resource
- indexed BMP development/test IO -> `RugScale.Core.IO.IndexedBmpCodec`

## Optional future RugCAD integration

RugCAD currently has no RugScale integration. If it is reintroduced, the resize host should call
only the public standalone API:

```csharp
var resized = DesignResizer.Scale(
    source,
    targetWidth,
    targetHeight,
    mode,
    sourceWarp,
    sourceWeft,
    targetWarp,
    targetWeft);
```

The host remains responsible for:

- choosing a `ScaleMode`,
- displaying preview/progress,
- selection UX for motif feedback,
- committing/cancelling the preview,
- converting RugCAD's document to/from `RugScale.Core.Models.DesignDocument` if the model is later
  decoupled further.

RugScale.Core remains responsible for:

- all resize/redraw algorithms,
- motif catalogue/memory/repair logic,
- quality-aware geometry,
- indexed palette guarantees,
- Curve / Pixel-Cord raster rules used by RugScale,
- training diagnostics and deterministic fallbacks.

## Recommended integration if RugCAD consumes RugScale again

Prefer a versioned package/reference boundary rather than copying RugScale source back into RugCAD.

Development options, in order:

1. local project/package reference while both repos are developed together,
2. local/private NuGet package for reproducible RugCAD builds,
3. tagged/released package once API stability is sufficient.

The old RugCAD RugScale implementation has been removed. Do not copy the standalone source back
into RugCAD; consume a versioned project/package API if future product integration is required.


## Current repository state

RugScale is now independent. RugCAD cleanup was merged as
`a1e3ab88691852969ece5dbbc77b6d829f05e33d`. Manual RugScale development and Good/Bad motif
learning are performed in `RugScale.Workbench`, so RugCAD's general drawing tools no longer share
that experimental surface.
