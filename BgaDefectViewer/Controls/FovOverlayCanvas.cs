using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Models;

namespace BgaDefectViewer.Controls;

/// <summary>
/// Transparent overlay that renders FOV grid rectangles, overlap zones,
/// scan order path, ball cluster center crosshair, alignment marks,
/// edge mask zones, and duplicate ball highlights.
/// Sits on top of BallMapCanvas sharing the same CoordinateTransform.
/// </summary>
public class FovOverlayCanvas : FrameworkElement
{
    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _deviceAreaVisual;
    private readonly DrawingVisual _fovGridVisual;
    private readonly DrawingVisual _overlapVisual;
    private readonly DrawingVisual _scanPathVisual;
    private readonly DrawingVisual _centerCrosshairVisual;
    private readonly DrawingVisual _alignmentVisual;
    private readonly DrawingVisual _edgeMaskVisual;
    private readonly DrawingVisual _duplicateVisual;

    private CoordinateTransform? _transform;

    public FovOverlayCanvas()
    {
        _visuals = new VisualCollection(this);
        _deviceAreaVisual = new DrawingVisual();
        _fovGridVisual = new DrawingVisual();
        _overlapVisual = new DrawingVisual();
        _scanPathVisual = new DrawingVisual();
        _centerCrosshairVisual = new DrawingVisual();
        _alignmentVisual = new DrawingVisual();
        _edgeMaskVisual = new DrawingVisual();
        _duplicateVisual = new DrawingVisual();

        _visuals.Add(_deviceAreaVisual);
        _visuals.Add(_fovGridVisual);
        _visuals.Add(_overlapVisual);
        _visuals.Add(_edgeMaskVisual);
        _visuals.Add(_scanPathVisual);
        _visuals.Add(_centerCrosshairVisual);
        _visuals.Add(_alignmentVisual);
        _visuals.Add(_duplicateVisual);

        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetTransform(CoordinateTransform transform) => _transform = transform;

    public void RenderAll(
        List<FovCell>? cells,
        List<(double left, double bottom, double right, double top)>? overlapRegions,
        (double x, double y) clusterCenter,
        (double spanX, double spanY) clusterSpan,
        List<DuplicateBallPair>? duplicates,
        OverlapParams? parameters)
    {
        if (_transform == null) { Clear(); return; }

        RenderDeviceArea(parameters, clusterCenter, clusterSpan);
        RenderFovGrid(cells);
        RenderOverlapZones(overlapRegions);
        RenderEdgeMask(cells, parameters);
        RenderScanPath(cells);
        RenderCenterCrosshair(clusterCenter, clusterSpan);
        RenderAlignmentMarks(cells, parameters);
        RenderDuplicateBalls(duplicates);
    }

    public void Clear()
    {
        using var dc0 = _deviceAreaVisual.RenderOpen();
        using var dc1 = _fovGridVisual.RenderOpen();
        using var dc2 = _overlapVisual.RenderOpen();
        using var dc3 = _scanPathVisual.RenderOpen();
        using var dc4 = _centerCrosshairVisual.RenderOpen();
        using var dc5 = _alignmentVisual.RenderOpen();
        using var dc6 = _edgeMaskVisual.RenderOpen();
        using var dc7 = _duplicateVisual.RenderOpen();
    }

    // ── Device Area + Chip Bump Area Outlines ────────────────────────
    //
    // Two frames are drawn so the user can see immediately whether the
    // Device Area input is large enough to enclose the chip bumps, and
    // how the FOV union extends beyond the device (P4 of the spec).
    //
    //   Device Area (input, centered on 0,0): solid cyan dashed frame
    //   Chip bump bounding box (from balls): fine dotted orange frame
    //
    // FOV union is NOT outlined — the four translucent FOV rectangles
    // already show that extent naturally.

    private void RenderDeviceArea(OverlapParams? p,
        (double x, double y) chipCenter,
        (double spanX, double spanY) chipSpan)
    {
        using var dc = _deviceAreaVisual.RenderOpen();
        if (p == null || _transform == null) return;
        if (!p.Enabled) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Device Area frame — centered at device origin (0, 0) by spec
        double halfDx = p.DeviceAreaX / 2.0;
        double halfDy = p.DeviceAreaY / 2.0;
        var (dlx, dty) = _transform.DataToScreen(-halfDx, halfDy);
        var (drx, dby) = _transform.DataToScreen(halfDx, -halfDy);
        var devRect = new Rect(
            Math.Min(dlx, drx), Math.Min(dty, dby),
            Math.Abs(drx - dlx), Math.Abs(dby - dty));

        var devPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 220, 220)), 1.5);
        devPen.DashStyle = DashStyles.Dash;
        devPen.Freeze();
        dc.DrawRectangle(null, devPen, devRect);

        var devLabel = new FormattedText(
            $"Device Area: {p.DeviceAreaX:F2} x {p.DeviceAreaY:F2} mm",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11,
            new SolidColorBrush(Color.FromArgb(255, 0, 220, 220)), dpi);
        var bgBrush = new SolidColorBrush(Color.FromArgb(180, 20, 20, 20));
        bgBrush.Freeze();
        dc.DrawRectangle(bgBrush, null,
            new Rect(devRect.Left + 4, devRect.Bottom - devLabel.Height - 4,
                     devLabel.Width + 6, devLabel.Height + 2));
        dc.DrawText(devLabel, new Point(devRect.Left + 7, devRect.Bottom - devLabel.Height - 3));

        // Chip bump bounding box — drawn at the ball cluster's true position
        // (NOT re-centered on 0,0) so misalignment between device origin and
        // the bumps is visible.
        if (chipSpan.spanX > 0 && chipSpan.spanY > 0)
        {
            double halfCx = chipSpan.spanX / 2.0;
            double halfCy = chipSpan.spanY / 2.0;
            var (clx, cty) = _transform.DataToScreen(chipCenter.x - halfCx, chipCenter.y + halfCy);
            var (crx, cby) = _transform.DataToScreen(chipCenter.x + halfCx, chipCenter.y - halfCy);
            var chipRect = new Rect(
                Math.Min(clx, crx), Math.Min(cty, cby),
                Math.Abs(crx - clx), Math.Abs(cby - cty));

            var chipPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 165, 0)), 1.0);
            chipPen.DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0);
            chipPen.Freeze();
            dc.DrawRectangle(null, chipPen, chipRect);

            var chipLabel = new FormattedText(
                $"Chip bump: {chipSpan.spanX:F3} x {chipSpan.spanY:F3} mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10,
                new SolidColorBrush(Color.FromArgb(255, 255, 165, 0)), dpi);
            dc.DrawRectangle(bgBrush, null,
                new Rect(chipRect.Left + 4, chipRect.Top + 4, chipLabel.Width + 6, chipLabel.Height + 2));
            dc.DrawText(chipLabel, new Point(chipRect.Left + 7, chipRect.Top + 5));
        }
    }

    // ── FOV Grid Rectangles ──────────────────────────────────────────

    private void RenderFovGrid(List<FovCell>? cells)
    {
        using var dc = _fovGridVisual.RenderOpen();
        if (cells == null || _transform == null) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Color palette for FOV cells
        var fovColors = new[]
        {
            Color.FromArgb(40, 0, 150, 255),   // blue
            Color.FromArgb(40, 0, 200, 100),   // green
            Color.FromArgb(40, 200, 150, 0),   // amber
            Color.FromArgb(40, 180, 0, 200),   // purple
            Color.FromArgb(40, 200, 80, 0),    // orange
            Color.FromArgb(40, 0, 180, 180),   // teal
        };

        foreach (var cell in cells)
        {
            var (lx, ty) = _transform.DataToScreen(cell.Left, cell.Top);
            var (rx, by) = _transform.DataToScreen(cell.Right, cell.Bottom);

            double x = Math.Min(lx, rx);
            double y = Math.Min(ty, by);
            double w = Math.Abs(rx - lx);
            double h = Math.Abs(by - ty);

            // Semi-transparent fill
            var colorIdx = (cell.ScanIndex - 1) % fovColors.Length;
            var fill = new SolidColorBrush(fovColors[colorIdx]);
            fill.Freeze();

            // Dashed border
            var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.5);
            borderPen.DashStyle = DashStyles.Dash;
            borderPen.Freeze();

            dc.DrawRectangle(fill, borderPen, new Rect(x, y, w, h));

            // Label: scan number and grid position
            var label = $"#{cell.ScanIndex} ({cell.GridX},{cell.GridY})";
            var ft = new FormattedText(
                label,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 12, Brushes.White, dpi);

            var bgBrush = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30));
            bgBrush.Freeze();
            dc.DrawRectangle(bgBrush, null,
                new Rect(x + 4, y + 4, ft.Width + 6, ft.Height + 4));
            dc.DrawText(ft, new Point(x + 7, y + 6));
        }
    }

    // ── Overlap Zones ────────────────────────────────────────────────

    private void RenderOverlapZones(
        List<(double left, double bottom, double right, double top)>? regions)
    {
        using var dc = _overlapVisual.RenderOpen();
        if (regions == null || _transform == null) return;

        var fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 0));
        fill.Freeze();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 200, 0)), 1.0);
        pen.Freeze();

        foreach (var (left, bottom, right, top) in regions)
        {
            var (lx, ty) = _transform.DataToScreen(left, top);
            var (rx, by) = _transform.DataToScreen(right, bottom);

            double x = Math.Min(lx, rx);
            double y = Math.Min(ty, by);
            double w = Math.Abs(rx - lx);
            double h = Math.Abs(by - ty);

            dc.DrawRectangle(fill, pen, new Rect(x, y, w, h));
        }
    }

    // ── Edge Mask Zones ──────────────────────────────────────────────

    private void RenderEdgeMask(List<FovCell>? cells, OverlapParams? p)
    {
        using var dc = _edgeMaskVisual.RenderOpen();
        if (cells == null || p == null || _transform == null) return;
        if (p.BoundaryMaskX <= 0 && p.BoundaryMaskY <= 0) return;

        var fill = new SolidColorBrush(Color.FromArgb(80, 100, 100, 100));
        fill.Freeze();

        foreach (var cell in cells)
        {
            // Left/Right mask
            if (p.BoundaryMaskX > 0)
            {
                DrawMaskRect(dc, fill, cell.Left, cell.Bottom, cell.Left + p.BoundaryMaskX, cell.Top);
                DrawMaskRect(dc, fill, cell.Right - p.BoundaryMaskX, cell.Bottom, cell.Right, cell.Top);
            }
            // Top/Bottom mask
            if (p.BoundaryMaskY > 0)
            {
                DrawMaskRect(dc, fill, cell.Left, cell.Top - p.BoundaryMaskY, cell.Right, cell.Top);
                DrawMaskRect(dc, fill, cell.Left, cell.Bottom, cell.Right, cell.Bottom + p.BoundaryMaskY);
            }
        }
    }

    private void DrawMaskRect(DrawingContext dc, Brush fill,
        double dataLeft, double dataBottom, double dataRight, double dataTop)
    {
        if (_transform == null) return;
        var (lx, ty) = _transform.DataToScreen(dataLeft, dataTop);
        var (rx, by) = _transform.DataToScreen(dataRight, dataBottom);
        double x = Math.Min(lx, rx);
        double y = Math.Min(ty, by);
        double w = Math.Abs(rx - lx);
        double h = Math.Abs(by - ty);
        if (w > 0 && h > 0)
            dc.DrawRectangle(fill, null, new Rect(x, y, w, h));
    }

    // ── Scan Path ────────────────────────────────────────────────────

    private void RenderScanPath(List<FovCell>? cells)
    {
        using var dc = _scanPathVisual.RenderOpen();
        if (cells == null || cells.Count < 2 || _transform == null) return;

        var ordered = cells.OrderBy(c => c.ScanIndex).ToList();

        var arrowPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 100, 100)), 2.0);
        arrowPen.Freeze();
        var dotBrush = new SolidColorBrush(Color.FromArgb(200, 255, 100, 100));
        dotBrush.Freeze();

        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var (sx, sy) = _transform.DataToScreen(ordered[i].CenterX, ordered[i].CenterY);
            var (ex, ey) = _transform.DataToScreen(ordered[i + 1].CenterX, ordered[i + 1].CenterY);

            dc.DrawLine(arrowPen, new Point(sx, sy), new Point(ex, ey));

            // Arrow head
            DrawArrowHead(dc, dotBrush, sx, sy, ex, ey);
        }

        // Draw dots at each center
        foreach (var cell in ordered)
        {
            var (px, py) = _transform.DataToScreen(cell.CenterX, cell.CenterY);
            dc.DrawEllipse(dotBrush, null, new Point(px, py), 4, 4);
        }
    }

    private static void DrawArrowHead(DrawingContext dc, Brush brush,
        double fromX, double fromY, double toX, double toY)
    {
        double dx = toX - fromX;
        double dy = toY - fromY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        dx /= len;
        dy /= len;

        double arrowLen = 10;
        double arrowWidth = 5;

        double ax = toX - dx * arrowLen;
        double ay = toY - dy * arrowLen;

        var p1 = new Point(toX, toY);
        var p2 = new Point(ax + dy * arrowWidth, ay - dx * arrowWidth);
        var p3 = new Point(ax - dy * arrowWidth, ay + dx * arrowWidth);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, true, true);
            ctx.LineTo(p2, true, false);
            ctx.LineTo(p3, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    // ── Center Crosshair ─────────────────────────────────────────────

    private void RenderCenterCrosshair((double x, double y) center,
        (double spanX, double spanY) span)
    {
        using var dc = _centerCrosshairVisual.RenderOpen();
        if (_transform == null) return;

        var pen = new Pen(Brushes.LimeGreen, 1.5);
        pen.DashStyle = DashStyles.DashDot;
        pen.Freeze();

        var (cx, cy) = _transform.DataToScreen(center.x, center.y);
        double canvasW = ActualWidth;
        double canvasH = ActualHeight;

        // Vertical line through center
        dc.DrawLine(pen, new Point(cx, 0), new Point(cx, canvasH));
        // Horizontal line through center
        dc.DrawLine(pen, new Point(0, cy), new Point(canvasW, cy));

        // Center dot
        dc.DrawEllipse(Brushes.LimeGreen, null, new Point(cx, cy), 4, 4);

        // Span labels
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        bgBrush.Freeze();

        // X span label below center
        var xLabel = new FormattedText(
            $"X span: {span.spanX:F3} mm",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11, Brushes.LimeGreen, dpi);
        double xlx = cx - xLabel.Width / 2;
        double xly = cy + 10;
        dc.DrawRectangle(bgBrush, null,
            new Rect(xlx - 2, xly - 1, xLabel.Width + 4, xLabel.Height + 2));
        dc.DrawText(xLabel, new Point(xlx, xly));

        // Y span label right of center
        var yLabel = new FormattedText(
            $"Y span: {span.spanY:F3} mm",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11, Brushes.LimeGreen, dpi);
        double ylx = cx + 10;
        double yly = cy - yLabel.Height / 2;
        dc.DrawRectangle(bgBrush, null,
            new Rect(ylx - 2, yly - 1, yLabel.Width + 4, yLabel.Height + 2));
        dc.DrawText(yLabel, new Point(ylx, yly));

        // Center coordinate label
        var centerLabel = new FormattedText(
            $"Center: ({center.x:F3}, {center.y:F3})",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 11, Brushes.LimeGreen, dpi);
        double clx = cx - centerLabel.Width / 2;
        double cly = cy - centerLabel.Height - 10;
        dc.DrawRectangle(bgBrush, null,
            new Rect(clx - 2, cly - 1, centerLabel.Width + 4, centerLabel.Height + 2));
        dc.DrawText(centerLabel, new Point(clx, cly));
    }

    // ── Alignment Marks ──────────────────────────────────────────────

    private void RenderAlignmentMarks(List<FovCell>? cells, OverlapParams? p)
    {
        using var dc = _alignmentVisual.RenderOpen();
        if (cells == null || p == null || _transform == null) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bgBrush = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20));
        bgBrush.Freeze();

        bool same = p.Alignment1FovX == p.Alignment2FovX
                 && p.Alignment1FovY == p.Alignment2FovY;

        // Alignment 1 (magenta). When A1 and A2 collapse to the same FOV
        // (common before alignment has been registered — both default to (1,1))
        // we offset A1 slightly to the upper-left so both marks stay visible.
        var align1 = cells.FirstOrDefault(c => c.GridX == p.Alignment1FovX && c.GridY == p.Alignment1FovY);
        if (align1 != null)
            DrawAlignMark(dc, dpi, bgBrush, align1, "A1", Colors.Magenta,
                offsetScreenX: same ? -18 : 0, offsetScreenY: same ? -18 : 0);

        // Alignment 2 (cyan — distinct color from A1 for clarity)
        var align2 = cells.FirstOrDefault(c => c.GridX == p.Alignment2FovX && c.GridY == p.Alignment2FovY);
        if (align2 != null)
            DrawAlignMark(dc, dpi, bgBrush, align2, "A2", Colors.Cyan,
                offsetScreenX: same ? 18 : 0, offsetScreenY: same ? 18 : 0);
    }

    private void DrawAlignMark(DrawingContext dc, double dpi, Brush bgBrush,
        FovCell cell, string label, Color color,
        double offsetScreenX = 0, double offsetScreenY = 0)
    {
        if (_transform == null) return;

        var (cx0, cy0) = _transform.DataToScreen(cell.CenterX, cell.CenterY);
        double cx = cx0 + offsetScreenX;
        double cy = cy0 + offsetScreenY;
        var pen = new Pen(new SolidColorBrush(color), 2.0);
        pen.Freeze();

        double size = 15;
        // Cross mark
        dc.DrawLine(pen, new Point(cx - size, cy), new Point(cx + size, cy));
        dc.DrawLine(pen, new Point(cx, cy - size), new Point(cx, cy + size));

        // Label
        var ft = new FormattedText(
            label,
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 12, new SolidColorBrush(color), dpi);
        dc.DrawRectangle(bgBrush, null,
            new Rect(cx + size + 2, cy - ft.Height / 2 - 1, ft.Width + 4, ft.Height + 2));
        dc.DrawText(ft, new Point(cx + size + 4, cy - ft.Height / 2));
    }

    // ── Duplicate Ball Highlights ────────────────────────────────────

    private void RenderDuplicateBalls(List<DuplicateBallPair>? duplicates)
    {
        using var dc = _duplicateVisual.RenderOpen();
        if (duplicates == null || duplicates.Count == 0 || _transform == null) return;

        // LOD: skip individual ball rendering when zoomed out too far
        double sampleRadius = duplicates.Count > 0
            ? _transform.BallRadiusPixels(duplicates[0].BallA.Diameter)
            : 0;
        if (sampleRadius < 2.0) return; // Too small to see — just show count in UI

        int canvasW = (int)ActualWidth;
        int canvasH = (int)ActualHeight;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 140, 0)), 1.5);
        pen.Freeze();

        var seen = new HashSet<int>(); // Avoid drawing multiple rings on same ball
        int rendered = 0;
        foreach (var dup in duplicates)
        {
            if (!seen.Add(dup.BallA.Id)) continue;

            var (px, py) = _transform.DataToScreen(dup.BallA.X, dup.BallA.Y);

            // Viewport culling: skip balls outside canvas
            if (px < -20 || px > canvasW + 20 || py < -20 || py > canvasH + 20)
                continue;

            double r = Math.Max(4, _transform.BallRadiusPixels(dup.BallA.Diameter) + 2);
            dc.DrawEllipse(null, pen, new Point(px, py), r, r);
            rendered++;
        }
    }
}
