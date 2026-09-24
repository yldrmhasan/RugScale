using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RugScale.Core.Drawing;
using RugScale.Core.IO;
using RugScale.Core.Models;
using RugScale.Core.Services;

namespace RugScale.Workbench;

public partial class MainWindow : Window
{
    private enum SelectionPurpose
    {
        Detection,
        Target,
    }

    private sealed record AcceptedRepair(
        MotifSelection Selection,
        MotifRepairCandidate Candidate);

    private IndexedBmpImage? _sourceImage;
    private string? _sourcePath;
    private DesignDocument? _basePreview;
    private DesignDocument? _preview;
    private MotifSourceCatalog? _catalog;
    private IReadOnlyList<MotifRepairCandidate> _candidates = [];
    private int _candidateIndex = -1;
    private MotifSelection? _detectionSelection;
    private MotifSelection? _targetSelection;
    private MotifRepairCandidate? _previewedRepair;
    private MotifSelection? _previewedSelection;
    private MotifRepairStyle[] _styles = [];
    private int _styleIndex;
    private readonly List<AcceptedRepair> _accepted = [];
    private SelectionPurpose _selectionPurpose = SelectionPurpose.Detection;
    private Point? _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        MemoryText.Text =
            $"Memory: {MotifMemoryStore.Memory.Families.Count} families\n{MotifMemoryStore.StorePath}";
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open RugScale indexed BMP",
            Filter = "8-bit indexed BMP (*.bmp)|*.bmp|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            StatusText.Text = "Loading indexed BMP...";
            var loaded = await Task.Run(() => IndexedBmpCodec.Read(dialog.FileName));
            _sourceImage = loaded;
            _sourcePath = dialog.FileName;
            _catalog = null;
            _basePreview = loaded.Document;
            _preview = loaded.Document;
            _accepted.Clear();
            ResetMotifSession(clearSelections: true);

            WidthBox.Text = loaded.Document.Width.ToString();
            HeightBox.Text = loaded.Document.Height.ToString();
            FileText.Text = dialog.FileName;
            SourceImage.Source = RenderDocument(loaded.Document);
            RenderPreview();
            StatusText.Text =
                $"Loaded {loaded.Document.Width}×{loaded.Document.Height} indexed BMP. Choose target size/mode and Run.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Open failed: {ex.Message}";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_preview is null || _sourceImage is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save RugScale preview",
            Filter = "8-bit indexed BMP (*.bmp)|*.bmp",
            DefaultExt = ".bmp",
            AddExtension = true,
            FileName = _sourcePath is null
                ? "rugscale-output.bmp"
                : $"{System.IO.Path.GetFileNameWithoutExtension(_sourcePath)}-rugscale.bmp",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            IndexedBmpCodec.Write(
                dialog.FileName,
                _preview,
                _sourceImage.XPixelsPerMeter,
                _sourceImage.YPixelsPerMeter);
            StatusText.Text = $"Saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (_sourceImage is null)
        {
            StatusText.Text = "Open a source BMP first.";
            return;
        }

        if (!TryPositiveInt(WidthBox.Text, out var width) ||
            !TryPositiveInt(HeightBox.Text, out var height) ||
            !TryPositiveInt(SourceWarpBox.Text, out var sourceWarp) ||
            !TryPositiveInt(SourceWeftBox.Text, out var sourceWeft) ||
            !TryPositiveInt(TargetWarpBox.Text, out var targetWarp) ||
            !TryPositiveInt(TargetWeftBox.Text, out var targetWeft))
        {
            StatusText.Text = "Size and quality values must be positive integers.";
            return;
        }

        var mode = SelectedMode();
        try
        {
            IsEnabled = false;
            StatusText.Text = $"Running {mode}...";
            var result = await Task.Run(() =>
                DesignResizer.Scale(
                    _sourceImage.Document,
                    width,
                    height,
                    mode,
                    sourceWarp,
                    sourceWeft,
                    targetWarp,
                    targetWeft));

            _basePreview = result;
            _preview = result;
            _accepted.Clear();
            ResetMotifSession(clearSelections: true);
            RenderPreview();

            StatusText.Text =
                $"Preview ready: {_sourceImage.Document.Width}×{_sourceImage.Document.Height} → {width}×{height}, mode {mode}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scale failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "RugScale failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private ScaleMode SelectedMode()
    {
        var tag =
            (ModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

        return tag switch
        {
            "curve-fill" => ScaleMode.CurveFill,
            "leaf-petal" => ScaleMode.LeafPetalArcs,
            "nearest" => ScaleMode.NearestNeighbor,
            "preserve-detail" => ScaleMode.PreserveDetail,
            "dominant" => ScaleMode.Dominant,
            "edge-smooth" => ScaleMode.EdgeSmooth,
            "smooth" => ScaleMode.Smooth,
            "area-average" => ScaleMode.AreaAverage,
            _ => ScaleMode.RugScale,
        };
    }

    private static bool TryPositiveInt(string? text, out int value) =>
        int.TryParse(text, out value) && value > 0;

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomText is null)
            return;

        ZoomText.Text = $"{e.NewValue * 100:0}%";
        RenderPreviewSize();
        UpdateSelectionVisual(
            _selectionPurpose == SelectionPurpose.Target
                ? _targetSelection
                : _detectionSelection);
    }

    private void RenderPreview()
    {
        if (_preview is null)
        {
            PreviewImage.Source = null;
            return;
        }

        PreviewImage.Source = RenderDocument(_preview);
        RenderPreviewSize();
        UpdateSelectionVisual(
            _selectionPurpose == SelectionPurpose.Target
                ? _targetSelection
                : _detectionSelection);
    }

    private void RenderPreviewSize()
    {
        if (_preview is null || PreviewHost is null)
            return;

        var zoom = Math.Clamp(ZoomSlider.Value, 0.05, 16);
        var width = Math.Max(1, _preview.Width * zoom);
        var height = Math.Max(1, _preview.Height * zoom);

        PreviewHost.Width = width;
        PreviewHost.Height = height;
        PreviewImage.Width = width;
        PreviewImage.Height = height;
        PreviewOverlay.Width = width;
        PreviewOverlay.Height = height;
    }

    private static BitmapSource RenderDocument(DesignDocument document)
    {
        var stride = checked(document.Width * 4);
        var pixels = new byte[checked(stride * document.Height)];

        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var color = document.Palette[document.GetPixel(x, y)];
                var offset = y * stride + x * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            document.Width,
            document.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void OnSelectMotif(object sender, RoutedEventArgs e)
    {
        _selectionPurpose = SelectionPurpose.Detection;
        UpdateSelectionVisual(_detectionSelection);
        StatusText.Text = "Drag a rectangle around the broken motif, then press Detect motif.";
    }

    private void OnSelectTarget(object sender, RoutedEventArgs e)
    {
        _selectionPurpose = SelectionPurpose.Target;
        UpdateSelectionVisual(_targetSelection);
        StatusText.Text = "Drag the area where the repaired source motif must fit.";
    }

    private void OnUseDetectedTarget(object sender, RoutedEventArgs e)
    {
        if (_detectionSelection is null)
            return;

        _targetSelection = _detectionSelection;
        _selectionPurpose = SelectionPurpose.Target;
        UpdateSelectionVisual(_targetSelection);
        RepairButton.IsEnabled = CurrentCandidate() is not null;
        StatusText.Text =
            $"Target restored to detected area: {_targetSelection.Width}×{_targetSelection.Height}.";
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_preview is null)
            return;

        _dragStart = e.GetPosition(PreviewOverlay);
        PreviewOverlay.CaptureMouse();
        ShowDragRectangle(_dragStart.Value, _dragStart.Value);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        ShowDragRectangle(_dragStart.Value, e.GetPosition(PreviewOverlay));
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null || _preview is null)
            return;

        var end = e.GetPosition(PreviewOverlay);
        var box = NormalizeDrag(_dragStart.Value, end);
        _dragStart = null;
        PreviewOverlay.ReleaseMouseCapture();

        if (box.Width < 2 || box.Height < 2)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        var x0 = Math.Clamp(
            (int)Math.Floor(box.X / Math.Max(1, PreviewOverlay.ActualWidth) * _preview.Width),
            0,
            _preview.Width - 1);
        var y0 = Math.Clamp(
            (int)Math.Floor(box.Y / Math.Max(1, PreviewOverlay.ActualHeight) * _preview.Height),
            0,
            _preview.Height - 1);
        var x1 = Math.Clamp(
            (int)Math.Ceiling((box.X + box.Width) / Math.Max(1, PreviewOverlay.ActualWidth) * _preview.Width),
            x0 + 1,
            _preview.Width);
        var y1 = Math.Clamp(
            (int)Math.Ceiling((box.Y + box.Height) / Math.Max(1, PreviewOverlay.ActualHeight) * _preview.Height),
            y0 + 1,
            _preview.Height);

        var selection = new MotifSelection(
            x0,
            y0,
            x1 - x0,
            y1 - y0);

        if (_selectionPurpose == SelectionPurpose.Detection)
        {
            _detectionSelection = selection;
            _targetSelection ??= selection;
            ResetCandidatesOnly();
            DetectButton.IsEnabled = true;
            StatusText.Text =
                $"Broken motif area: {selection.X},{selection.Y} {selection.Width}×{selection.Height}. Press Detect motif.";
        }
        else
        {
            _targetSelection = selection;
            _previewedRepair = null;
            _previewedSelection = null;
            GoodButton.IsEnabled = false;
            BadButton.IsEnabled = false;
            RepairButton.IsEnabled = CurrentCandidate() is not null;
            StatusText.Text =
                $"Target area: {selection.X},{selection.Y} {selection.Width}×{selection.Height}.";
        }

        UpdateSelectionVisual(selection);
    }

    private Rect NormalizeDrag(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;

        if (SquareSelectionCheck.IsChecked == true)
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            end = new Point(
                start.X + Math.Sign(dx == 0 ? 1 : dx) * side,
                start.Y + Math.Sign(dy == 0 ? 1 : dy) * side);
        }

        var left = Math.Clamp(Math.Min(start.X, end.X), 0, PreviewOverlay.ActualWidth);
        var top = Math.Clamp(Math.Min(start.Y, end.Y), 0, PreviewOverlay.ActualHeight);
        var right = Math.Clamp(Math.Max(start.X, end.X), 0, PreviewOverlay.ActualWidth);
        var bottom = Math.Clamp(Math.Max(start.Y, end.Y), 0, PreviewOverlay.ActualHeight);

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private void ShowDragRectangle(Point start, Point end)
    {
        var box = NormalizeDrag(start, end);
        Canvas.SetLeft(SelectionRect, box.X);
        Canvas.SetTop(SelectionRect, box.Y);
        SelectionRect.Width = box.Width;
        SelectionRect.Height = box.Height;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void UpdateSelectionVisual(MotifSelection? selection)
    {
        if (_preview is null || selection is null)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SelectionInfoText.Text = "";
            return;
        }

        var sx = PreviewHost.Width / _preview.Width;
        var sy = PreviewHost.Height / _preview.Height;
        Canvas.SetLeft(SelectionRect, selection.X * sx);
        Canvas.SetTop(SelectionRect, selection.Y * sy);
        SelectionRect.Width = selection.Width * sx;
        SelectionRect.Height = selection.Height * sy;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionInfoText.Text =
            $"{selection.Width}×{selection.Height} @ {selection.X},{selection.Y}";
    }

    private async void OnDetectMotif(object sender, RoutedEventArgs e)
    {
        if (_sourceImage is null ||
            _basePreview is null ||
            _detectionSelection is null)
        {
            return;
        }

        if (!TryPositiveInt(SourceWarpBox.Text, out var sourceWarp) ||
            !TryPositiveInt(SourceWeftBox.Text, out var sourceWeft) ||
            !TryPositiveInt(TargetWarpBox.Text, out var targetWarp) ||
            !TryPositiveInt(TargetWeftBox.Text, out var targetWeft))
        {
            StatusText.Text = "Quality values must be positive integers.";
            return;
        }

        try
        {
            DetectButton.IsEnabled = false;
            StatusText.Text = "Detecting the selected motif in the immutable original source...";
            var preview = ComposeAcceptedPreview();
            var result = await Task.Run(() =>
            {
                var catalog =
                    _catalog ??
                    MotifSourceCatalog.Build(
                        _sourceImage.Document,
                        sourceWarp,
                        sourceWeft);
                var candidates =
                    MotifRepairEngine.FindCandidates(
                        _sourceImage.Document,
                        preview,
                        _detectionSelection,
                        sourceWarp,
                        sourceWeft,
                        targetWarp,
                        targetWeft,
                        MotifMemoryStore.Memory,
                        maxCandidates: 12,
                        sourceCatalog: catalog);
                return (catalog, candidates);
            });

            _catalog = result.catalog;
            _candidates = result.candidates;
            _candidateIndex = _candidates.Count > 0 ? 0 : -1;
            _preview = preview;
            _targetSelection ??= _detectionSelection;

            if (_candidateIndex < 0)
            {
                MotifImage.Source = null;
                CandidateText.Text = "No source motif candidate found. Adjust the selection and detect again.";
                RepairButton.IsEnabled = false;
                StatusText.Text = "No candidate found.";
                RenderPreview();
                return;
            }

            ShowCurrentCandidate();
            StatusText.Text = "Source motif detected. Inspect it, choose target area, then Preview repair.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Motif detection failed: {ex.Message}";
        }
        finally
        {
            DetectButton.IsEnabled = _detectionSelection is not null;
        }
    }

    private MotifRepairCandidate? CurrentCandidate() =>
        _candidateIndex >= 0 &&
        _candidateIndex < _candidates.Count
            ? _candidates[_candidateIndex]
            : null;

    private void ShowCurrentCandidate()
    {
        var candidate = CurrentCandidate();
        if (candidate is null || _sourceImage is null)
            return;

        _previewedRepair = null;
        _previewedSelection = null;
        _styleIndex = 0;
        _styles = BuildRefinementStyles(candidate);
        _preview = ComposeAcceptedPreview();
        RenderPreview();

        MotifImage.Source = RenderSourceMotif(_sourceImage.Document, candidate);
        CandidateText.Text =
            $"{candidate.Kind} • match {candidate.Similarity:P0}\n" +
            $"Source {candidate.SourceX + 1},{candidate.SourceY + 1} • " +
            $"{candidate.SourceWidth}×{candidate.SourceHeight}px\n" +
            $"Transform {candidate.Transform}\nCandidate {_candidateIndex + 1}/{_candidates.Count}";
        RedrawText.Text = "Redraw: not previewed yet";
        RepairButton.IsEnabled =
            (_targetSelection ?? _detectionSelection) is not null;
        GoodButton.IsEnabled = false;
        BadButton.IsEnabled = false;
        DifferentButton.IsEnabled = _candidates.Count > 1;
    }

    private static BitmapSource RenderSourceMotif(
        DesignDocument source,
        MotifRepairCandidate candidate)
    {
        var width = Math.Max(1, candidate.SourceWidth);
        var height = Math.Max(1, candidate.SourceHeight);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var local = y * width + x;
                var offset = y * stride + x * 4;

                if (local >= candidate.SourceMask.Length ||
                    !candidate.SourceMask[local])
                {
                    continue;
                }

                var sx = Math.Clamp(candidate.SourceX + x, 0, source.Width - 1);
                var sy = Math.Clamp(candidate.SourceY + y, 0, source.Height - 1);
                var color = source.Palette[source.GetPixel(sx, sy)];
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void OnPreviewRepair(object sender, RoutedEventArgs e) =>
        PreviewRepair();

    private void PreviewRepair()
    {
        if (_sourceImage is null || _basePreview is null)
            return;

        var candidate = CurrentCandidate();
        var target = _targetSelection ?? _detectionSelection;

        if (candidate is null || target is null)
            return;

        _styles = _styles.Length > 0
            ? _styles
            : BuildRefinementStyles(candidate);
        _styleIndex = Math.Clamp(_styleIndex, 0, _styles.Length - 1);
        var style = _styles[_styleIndex];

        var rendered =
            MotifRepairEngine.RetargetCandidate(
                _sourceImage.Document,
                target,
                candidate,
                style);

        _preview =
            MotifRepairEngine.ApplyCandidate(
                ComposeAcceptedPreview(),
                target,
                rendered);
        _previewedRepair = rendered;
        _previewedSelection = target;
        RenderPreview();

        var attempt = _styleIndex + 1;
        RedrawText.Text =
            $"Redraw {attempt}/{_styles.Length}: {FriendlyStyle(style)}";
        GoodButton.IsEnabled = true;
        BadButton.IsEnabled = _styleIndex < _styles.Length - 1;
        DifferentButton.IsEnabled = _candidates.Count > 1;
        StatusText.Text =
            _styleIndex < _styles.Length - 1
                ? "Preview uses the ORIGINAL masked source motif. Bad / improve redraws it again; it never redraws the failed result."
                : "Last redraw style for this source motif. Adjust target area or choose Different source if needed.";
    }

    private void OnGood(object sender, RoutedEventArgs e)
    {
        if (_sourceImage is null ||
            _previewedRepair is null ||
            _previewedSelection is null)
        {
            return;
        }

        var candidate = _previewedRepair;
        _accepted.Add(new AcceptedRepair(_previewedSelection, candidate));

        var sourceWarp = TryPositiveInt(SourceWarpBox.Text, out var sw) ? sw : 1;
        var sourceWeft = TryPositiveInt(SourceWeftBox.Text, out var sf) ? sf : 1;
        var descriptor =
            MotifDescriptor.FromPatch(
                _sourceImage.Document,
                candidate.SourceX,
                candidate.SourceY,
                candidate.SourceWidth,
                candidate.SourceHeight,
                selectionMask: null,
                sourceWarp,
                sourceWeft);

        var family =
            MotifMemorySerializer.LearnPositive(
                MotifMemoryStore.Memory,
                descriptor,
                _sourcePath is null
                    ? "RugScale Workbench"
                    : System.IO.Path.GetFileName(_sourcePath),
                candidate.SourceWidth,
                candidate.SourceHeight,
                sourceWarp,
                sourceWeft);
        MotifMemorySerializer.LearnRepairStyle(
            MotifMemoryStore.Memory,
            family.Id,
            candidate.Style,
            success: true);
        MotifMemoryStore.Save();

        _preview = ComposeAcceptedPreview();
        RenderPreview();
        ResetMotifSession(clearSelections: true);
        MemoryText.Text =
            $"Memory: {MotifMemoryStore.Memory.Families.Count} families\n{MotifMemoryStore.StorePath}";
        StatusText.Text = "Motif accepted and learned. Select the next broken motif.";
    }

    private void OnBad(object sender, RoutedEventArgs e)
    {
        var sourceCandidate = CurrentCandidate();
        if (sourceCandidate is null || _previewedRepair is null)
            return;

        MotifMemorySerializer.LearnRepairStyle(
            MotifMemoryStore.Memory,
            sourceCandidate.MemoryFamilyId,
            _previewedRepair.Style,
            success: false);
        MotifMemoryStore.Save();

        if (_styleIndex >= _styles.Length - 1)
        {
            BadButton.IsEnabled = false;
            StatusText.Text =
                "All redraw styles for this SAME source motif were tried. Change target area or choose Different source.";
            return;
        }

        _styleIndex++;
        PreviewRepair();
    }

    private void OnDifferentSource(object sender, RoutedEventArgs e)
    {
        var candidate = CurrentCandidate();
        if (candidate is null || _candidates.Count == 0)
            return;

        MotifMemorySerializer.LearnNegative(
            MotifMemoryStore.Memory,
            candidate.MemoryFamilyId);
        MotifMemoryStore.Save();

        _candidateIndex =
            (_candidateIndex + 1) %
            _candidates.Count;
        ShowCurrentCandidate();
        StatusText.Text = "Different source motif selected. Inspect it, then Preview repair.";
    }

    private void OnPreviousCandidate(object sender, RoutedEventArgs e)
    {
        if (_candidates.Count == 0)
            return;

        _candidateIndex =
            (_candidateIndex - 1 + _candidates.Count) %
            _candidates.Count;
        ShowCurrentCandidate();
    }

    private void OnNextCandidate(object sender, RoutedEventArgs e)
    {
        if (_candidates.Count == 0)
            return;

        _candidateIndex =
            (_candidateIndex + 1) %
            _candidates.Count;
        ShowCurrentCandidate();
    }

    private static MotifRepairStyle[] BuildDefaultStyles(MotifKind kind) =>
        kind switch
        {
            MotifKind.Branch =>
            [
                MotifRepairStyle.Balanced,
                MotifRepairStyle.PreserveBranches,
                MotifRepairStyle.DetailAndConnectivity,
                MotifRepairStyle.ConnectivityFirst,
                MotifRepairStyle.ContourFirst,
            ],
            MotifKind.LeafLike =>
            [
                MotifRepairStyle.Balanced,
                MotifRepairStyle.PreserveBranches,
                MotifRepairStyle.DetailAndConnectivity,
                MotifRepairStyle.ConnectivityFirst,
                MotifRepairStyle.ContourFirst,
            ],
            MotifKind.Compound =>
            [
                MotifRepairStyle.PreserveBranches,
                MotifRepairStyle.DetailAndConnectivity,
                MotifRepairStyle.Balanced,
                MotifRepairStyle.ConnectivityFirst,
                MotifRepairStyle.ContourFirst,
            ],
            MotifKind.LargePart =>
            [
                MotifRepairStyle.DetailAndConnectivity,
                MotifRepairStyle.PreserveBranches,
                MotifRepairStyle.Balanced,
                MotifRepairStyle.ConnectivityFirst,
                MotifRepairStyle.ContourFirst,
            ],
            _ =>
            [
                MotifRepairStyle.DetailAndConnectivity,
                MotifRepairStyle.Balanced,
                MotifRepairStyle.PreserveBranches,
                MotifRepairStyle.ConnectivityFirst,
                MotifRepairStyle.ContourFirst,
            ],
        };

    private static MotifRepairStyle[] BuildRefinementStyles(
        MotifRepairCandidate candidate) =>
        BuildDefaultStyles(candidate.Kind)
            .Select((style, index) => new
            {
                Style = style,
                Index = index,
                Confidence =
                    MotifMemorySerializer.RepairStyleConfidence(
                        MotifMemoryStore.Memory,
                        candidate.MemoryFamilyId,
                        style),
            })
            .OrderByDescending(item => item.Confidence)
            .ThenBy(item => item.Index)
            .Select(item => item.Style)
            .ToArray();

    private static string FriendlyStyle(MotifRepairStyle style) =>
        style switch
        {
            MotifRepairStyle.Balanced => "Balanced color-role reconstruction",
            MotifRepairStyle.PreserveBranches => "Thin branches + color-role preservation",
            MotifRepairStyle.ContourFirst => "Contour fidelity without color bleed",
            MotifRepairStyle.ConnectivityFirst => "Source-supported connectivity",
            MotifRepairStyle.DetailAndConnectivity => "Maximum source fidelity",
            _ => style.ToString(),
        };

    private DesignDocument ComposeAcceptedPreview()
    {
        if (_basePreview is null)
            throw new InvalidOperationException("Scale preview is not ready.");

        var document = _basePreview;

        foreach (var repair in _accepted)
        {
            document =
                MotifRepairEngine.ApplyCandidate(
                    document,
                    repair.Selection,
                    repair.Candidate);
        }

        return document;
    }

    private void ResetCandidatesOnly()
    {
        _candidates = [];
        _candidateIndex = -1;
        _styles = [];
        _styleIndex = 0;
        _previewedRepair = null;
        _previewedSelection = null;
        MotifImage.Source = null;
        CandidateText.Text = "No motif detected.";
        RedrawText.Text = "Redraw: not previewed yet";
        RepairButton.IsEnabled = false;
        GoodButton.IsEnabled = false;
        BadButton.IsEnabled = false;
        DifferentButton.IsEnabled = false;
    }

    private void ResetMotifSession(bool clearSelections)
    {
        ResetCandidatesOnly();

        if (clearSelections)
        {
            _detectionSelection = null;
            _targetSelection = null;
            DetectButton.IsEnabled = false;
            SelectionRect.Visibility = Visibility.Collapsed;
            SelectionInfoText.Text = "";
        }
    }

    private void OnImportMemory(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import RugScale motif memory",
            Filter = "RugScale memory (*.json)|*.json|JSON (*.json)|*.json",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            MotifMemoryStore.Import(dialog.FileName, merge: true);
            MemoryText.Text =
                $"Memory: {MotifMemoryStore.Memory.Families.Count} families\n{MotifMemoryStore.StorePath}";
            StatusText.Text = "Motif memory imported and merged.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportMemory(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export RugScale motif memory",
            Filter = "RugScale memory (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "rugscale-motif-memory.json",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            MotifMemoryStore.Export(dialog.FileName);
            StatusText.Text = $"Motif memory exported: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
