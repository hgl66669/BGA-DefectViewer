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
    private readonly DrawingVisual _cameraRawVisual;    // outermost: actual photo
    private readonly DrawingVisual _deviceAreaVisual;
    private readonly DrawingVisual _fovGridVisual;       // middle: logical FOV
    private readonly DrawingVisual _effectiveVisual;     // innermost: FOV minus mask
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
        _cameraRawVisual = new DrawingVisual();
        _deviceAreaVisual = new DrawingVisual();
        _fovGridVisual = new DrawingVisual();
        _effectiveVisual = new DrawingVisual();
        _overlapVisual = new DrawingVisual();
        _scanPathVisual = new DrawingVisual();
        _centerCrosshairVisual = new DrawingVisual();
        _alignmentVisual = new DrawingVisual();
        _edgeMaskVisual = new DrawingVisual();
        _duplicateVisual = new DrawingVisual();

        // Z-order (bottom → top): camera raw, device area, fov grid,
        // effective area, overlap, edge mask, scan path, crosshair, align,
        // duplicates.
        _visuals.Add(_cameraRawVisual);
        _visuals.Add(_deviceAreaVisual);
        _visuals.Add(_fovGridVisual);
        _visuals.Add(_effectiveVisual);
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

        RenderCameraRaw(cells, parameters);
        RenderDeviceArea(parameters, clusterCenter, clusterSpan);
        RenderFovGrid(cells, parameters);
        RenderEffectiveArea(cells, parameters);
        RenderOverlapZones(overlapRegions);
        RenderEdgeMask(cells, parameters);
        RenderScanPath(cells);
        RenderCenterCrosshair(clusterCenter, clusterSpan);
        RenderAlignmentMarks(cells, parameters);
        RenderDuplicateBalls(duplicates);
    }

    public void Clear()
    {
        using var dcA = _cameraRawVisual.RenderOpen();
        using var dc0 = _deviceAreaVisual.RenderOpen();
        using var dc1 = _fovGridVisual.RenderOpen();
        using var dcB = _effectiveVisual.RenderOpen();
        using var dc2 = _overlapVisual.RenderOpen();
        using var dc3 = _scanPathVisual.RenderOpen();
        using var dc4 = _centerCrosshairVisual.RenderOpen();
        using var dc5 = _alignmentVisual.RenderOpen();
        using var dc6 = _edgeMaskVisual.RenderOpen();
        using var dc7 = _duplicateVisual.RenderOpen();
    }

    // ── Camera Raw Image Layer ───────────────────────────────────────
    //
    // Each inspection position captures a raw image larger than the logical
    // FOV (Normal lens: 94.52×62.87 mm vs FOV 60×60). When this layer is
    // enabled we draw the raw extent as a very faint dashed rectangle so
    // the user can see why real machine photos look bigger than the FOV.

    private void RenderCameraRaw(List<FovCell>? cells, OverlapParams? p)
    {
        using var dc = _cameraRawVisual.RenderOpen();
        if (cells == null || p == null || _transform == null) return;
        if (!p.Enabled || !p.ShowCameraRawLayer) return;
        if (p.CameraRawX <= 0 || p.CameraRawY <= 0) return;

        double halfRx = p.CameraRawX / 2.0;
        double halfRy = p.CameraRawY / 2.0;

        foreach (var cell in cells)
        {
            var (lx, ty) = _transform.DataToScreen(cell.CenterX - halfRx, cell.CenterY + halfRy);
            var (rx, by) = _transform.DataToScreen(cell.CenterX + halfRx, cell.CenterY - halfRy);
            var rect = new Rect(Math.Min(lx, rx), Math.Min(ty, by),
                                Math.Abs(rx - lx), Math.Abs(by - ty));

            // Very faint fill so heavy overlaps don't dominate the view
            var fill = new SolidColorBrush(Color.FromArgb(15, 180, 180, 180));
            fill.Freeze();
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, 200, 200, 200)), 1.0);
            pen.DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0);
            pen.Freeze();
            dc.DrawRectangle(fill, pen, rect);
        }
    }

    // ── Effective Inspection Area (FOV − Boundary mask) ──────────────

    private void RenderEffectiveArea(List<FovCell>? cells, OverlapParams? p)
    {
        using var dc = _effectiveVisual.RenderOpen();
        if (cells == null || p == null || _transform == null) return;
        if (!p.Enabled || !p.ShowEffectiveLayer) return;
        if (p.BoundaryMaskX <= 0 && p.BoundaryMaskY <= 0) return;

        double halfEx = (p.FovSizeX - 2 * p.BoundaryMaskX) / 2.0;
        double halfEy = (p.FovSizeY - 2 * p.BoundaryMaskY) / 2.0;
        if (halfEx <= 0 || halfEy <= 0) return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 80, 255, 80)), 1.0);
        pen.DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0);
        pen.Freeze();

        foreach (var cell in cells)
        {
            var (lx, ty) = _transform.DataToScreen(cell.CenterX - halfEx, cell.CenterY + halfEy);
            var (rx, by) = _transform.DataToScreen(cell.CenterX + halfEx, cell.CenterY - halfEy);
            var rect = new Rect(Math.Min(lx, rx), Math.Min(ty, by),
                                Math.Abs(rx - lx), Math.Abs(by - ty));
            dc.DrawRectangle(null, pen, rect);
        }
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

        // Substrate outline (green) + ghost units. The substrate is drawn
        // at SubstrateOffset so the focused unit sits on the simulator
        // origin. For multi-unit layouts every other unit gets a thin
        // grey ghost outline at its relative pitch position.
        if (p.ShowSubstrate && p.SubstrateSizeX is { } subX && p.SubstrateSizeY is { } subY
            && subX > 0 && subY > 0)
        {
            // Substrate-center offset in focused-unit-local coordinates.
            int N = Math.Max(1, p.SubstrateDeviceCountX);
            int M = Math.Max(1, p.SubstrateDeviceCountY);
            double subOffX = ((N - 1) / 2.0 - (p.FocusedUnitX - 1)) * p.DevicePitchX;
            double subOffY = -((M - 1) / 2.0 - (p.FocusedUnitY - 1)) * p.DevicePitchY;

            double halfSx = subX / 2.0;
            double halfSy = subY / 2.0;
            var (slx, sty) = _transform.DataToScreen(subOffX - halfSx, subOffY + halfSy);
            var (srx, sby) = _transform.DataToScreen(subOffX + halfSx, subOffY - halfSy);
            var subRect = new Rect(
                Math.Min(slx, srx), Math.Min(sty, sby),
                Math.Abs(srx - slx), Math.Abs(sby - sty));

            var subPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 0, 220, 80)), 2.0);
            subPen.DashStyle = new DashStyle(new[] { 6.0, 4.0 }, 0);
            subPen.Freeze();
            dc.DrawRectangle(null, subPen, subRect);

            string subLabelText = (N > 1 || M > 1)
                ? $"Substrate: {subX:F2} x {subY:F2} mm  ({N}x{M} units, focus ({p.FocusedUnitX},{p.FocusedUnitY}))"
                : $"Substrate: {subX:F2} x {subY:F2} mm";
            var subLabel = new FormattedText(
                subLabelText,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 11,
                new SolidColorBrush(Color.FromArgb(255, 0, 220, 80)), dpi);
            dc.DrawRectangle(bgBrush, null,
                new Rect(subRect.Right - subLabel.Width - 10, subRect.Top + 4,
                         subLabel.Width + 6, subLabel.Height + 2));
            dc.DrawText(subLabel, new Point(subRect.Right - subLabel.Width - 7, subRect.Top + 5));

            // ── Substrate center marker + edge-to-center distances ──
            //
            // Gold cross at substrate center; thin dashed inner rulers from
            // the left/top edges to that center, labelled with X/2 and Y/2;
            // and (multi-unit only) a longer dashed connector from substrate
            // center to the focused unit's center (= simulator origin).
            var (subCxS, subCyS) = _transform.DataToScreen(subOffX, subOffY);
            var subCenterColor = Color.FromArgb(235, 255, 215, 0);
            var subCenterBrush = new SolidColorBrush(subCenterColor);
            subCenterBrush.Freeze();
            var subCenterPen = new Pen(subCenterBrush, 1.5);
            subCenterPen.Freeze();
            var subCenterRulerPen = new Pen(new SolidColorBrush(Color.FromArgb(170, 255, 215, 0)), 1.0);
            subCenterRulerPen.DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0);
            subCenterRulerPen.Freeze();

            const double crossArm = 9;
            dc.DrawLine(subCenterPen,
                new Point(subCxS - crossArm, subCyS),
                new Point(subCxS + crossArm, subCyS));
            dc.DrawLine(subCenterPen,
                new Point(subCxS, subCyS - crossArm),
                new Point(subCxS, subCyS + crossArm));
            dc.DrawEllipse(subCenterBrush, null, new Point(subCxS, subCyS), 2.5, 2.5);

            // X/2 ruler — left edge → center, drawn just inside the frame.
            var (leftEdgeXs, _) = _transform.DataToScreen(subOffX - halfSx, subOffY);
            dc.DrawLine(subCenterRulerPen,
                new Point(leftEdgeXs, subCyS),
                new Point(subCxS - crossArm, subCyS));
            var xHalfLabel = new FormattedText(
                $"X/2: {halfSx:F2} mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, subCenterBrush, dpi);
            double xLblX = (leftEdgeXs + subCxS - crossArm) / 2 - xHalfLabel.Width / 2;
            double xLblY = subCyS - xHalfLabel.Height - 3;
            dc.DrawRectangle(bgBrush, null,
                new Rect(xLblX - 2, xLblY - 1, xHalfLabel.Width + 4, xHalfLabel.Height + 2));
            dc.DrawText(xHalfLabel, new Point(xLblX, xLblY));

            // Y/2 ruler — top edge → center.
            var (_, topEdgeYs) = _transform.DataToScreen(subOffX, subOffY + halfSy);
            dc.DrawLine(subCenterRulerPen,
                new Point(subCxS, topEdgeYs),
                new Point(subCxS, subCyS - crossArm));
            var yHalfLabel = new FormattedText(
                $"Y/2: {halfSy:F2} mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, subCenterBrush, dpi);
            double yLblX = subCxS + 5;
            double yLblY = (topEdgeYs + subCyS - crossArm) / 2 - yHalfLabel.Height / 2;
            dc.DrawRectangle(bgBrush, null,
                new Rect(yLblX - 2, yLblY - 1, yHalfLabel.Width + 4, yHalfLabel.Height + 2));
            dc.DrawText(yHalfLabel, new Point(yLblX, yLblY));

            // Sub-center coordinate caption (under the cross).
            var subCoordLabel = new FormattedText(
                $"Sub center ({subOffX:F2}, {subOffY:F2}) mm",
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, subCenterBrush, dpi);
            double scLblX = subCxS - subCoordLabel.Width / 2;
            double scLblY = subCyS + crossArm + 4;
            dc.DrawRectangle(bgBrush, null,
                new Rect(scLblX - 2, scLblY - 1, subCoordLabel.Width + 4, subCoordLabel.Height + 2));
            dc.DrawText(subCoordLabel, new Point(scLblX, scLblY));

            // Connector substrate-center → focused-unit-center (origin).
            // Skipped for a 1×1 substrate (the two points coincide).
            if (N > 1 || M > 1)
            {
                var (focusXs, focusYs) = _transform.DataToScreen(0, 0);
                var connectorPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 215, 0)), 1.5);
                connectorPen.DashStyle = new DashStyle(new[] { 6.0, 4.0 }, 0);
                connectorPen.Freeze();
                dc.DrawLine(connectorPen, new Point(subCxS, subCyS), new Point(focusXs, focusYs));

                double dx = -subOffX, dy = -subOffY; // sub center → focus
                double dist = Math.Sqrt(dx * dx + dy * dy);
                var connLabel = new FormattedText(
                    $"Δ = {dist:F2} mm  ({dx:+0.00;-0.00}, {dy:+0.00;-0.00})",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 10, subCenterBrush, dpi);
                double midX = (subCxS + focusXs) / 2;
                double midY = (subCyS + focusYs) / 2;
                double clblX = midX - connLabel.Width / 2;
                double clblY = midY + 4;
                dc.DrawRectangle(bgBrush, null,
                    new Rect(clblX - 2, clblY - 1, connLabel.Width + 4, connLabel.Height + 2));
                dc.DrawText(connLabel, new Point(clblX, clblY));
            }

            // Ghost outlines for the non-focused units. Each unit is sized
            // to the chip-bump bbox (preferred) or device area, drawn with
            // a thin grey solid frame and labelled with its (X, Y) index.
            if ((N > 1 || M > 1) && p.DevicePitchX > 0 && p.DevicePitchY > 0)
            {
                double ghostHalfX = chipSpan.spanX > 0 ? chipSpan.spanX / 2.0 : p.DeviceAreaX / 2.0;
                double ghostHalfY = chipSpan.spanY > 0 ? chipSpan.spanY / 2.0 : p.DeviceAreaY / 2.0;

                var ghostPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 180, 180, 180)), 1.0);
                ghostPen.DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0);
                ghostPen.Freeze();
                var ghostFill = new SolidColorBrush(Color.FromArgb(20, 180, 180, 180));
                ghostFill.Freeze();

                for (int j = 1; j <= M; j++)
                {
                    for (int i = 1; i <= N; i++)
                    {
                        if (i == p.FocusedUnitX && j == p.FocusedUnitY) continue;
                        double ucx = (i - p.FocusedUnitX) * p.DevicePitchX;
                        double ucy = -(j - p.FocusedUnitY) * p.DevicePitchY;

                        var (glx, gty) = _transform.DataToScreen(ucx - ghostHalfX, ucy + ghostHalfY);
                        var (grx, gby) = _transform.DataToScreen(ucx + ghostHalfX, ucy - ghostHalfY);
                        var gRect = new Rect(
                            Math.Min(glx, grx), Math.Min(gty, gby),
                            Math.Abs(grx - glx), Math.Abs(gby - gty));
                        dc.DrawRectangle(ghostFill, ghostPen, gRect);

                        var idxLabel = new FormattedText(
                            $"({i},{j})",
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface("Consolas"), 11,
                            new SolidColorBrush(Color.FromArgb(220, 200, 200, 200)), dpi);
                        // Center the label in the ghost rect
                        double tx = gRect.Left + (gRect.Width - idxLabel.Width) / 2;
                        double ty = gRect.Top + (gRect.Height - idxLabel.Height) / 2;
                        dc.DrawRectangle(bgBrush, null,
                            new Rect(tx - 2, ty - 1, idxLabel.Width + 4, idxLabel.Height + 2));
                        dc.DrawText(idxLabel, new Point(tx, ty));
                    }
                }
            }
        }

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

    private void RenderFovGrid(List<FovCell>? cells, OverlapParams? p)
    {
        using var dc = _fovGridVisual.RenderOpen();
        if (cells == null || _transform == null) return;
        if (p != null && !p.ShowFovLayer) return;

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

        // If the .dat file registered real mm fiducial positions, draw there
        // directly — this matches the exact spot the real machine aligns to.
        // Otherwise fall back to the containing FOV's center.
        bool sameGrid = p.Alignment1FovX == p.Alignment2FovX
                     && p.Alignment1FovY == p.Alignment2FovY;

        // Collision offsets only apply when both fall back to FOV centers;
        // mm positions are already precise so no shift is needed.
        double a1OffX = 0, a1OffY = 0, a2OffX = 0, a2OffY = 0;
        if (sameGrid && p.Align1Mm == null && p.Align2Mm == null)
        {
            a1OffX = -18; a1OffY = -18;
            a2OffX = +18; a2OffY = +18;
        }

        // Alignment 1 (magenta)
        if (p.Align1Mm is { } a1mm)
        {
            DrawAlignMarkMm(dc, dpi, bgBrush, a1mm.X, a1mm.Y, "A1", Colors.Magenta);
        }
        else
        {
            var align1 = cells.FirstOrDefault(c => c.GridX == p.Alignment1FovX && c.GridY == p.Alignment1FovY);
            if (align1 != null)
                DrawAlignMarkAtCell(dc, dpi, bgBrush, align1, "A1", Colors.Magenta, a1OffX, a1OffY);
        }

        // Alignment 2 (cyan)
        if (p.Align2Mm is { } a2mm)
        {
            DrawAlignMarkMm(dc, dpi, bgBrush, a2mm.X, a2mm.Y, "A2", Colors.Cyan);
        }
        else
        {
            var align2 = cells.FirstOrDefault(c => c.GridX == p.Alignment2FovX && c.GridY == p.Alignment2FovY);
            if (align2 != null)
                DrawAlignMarkAtCell(dc, dpi, bgBrush, align2, "A2", Colors.Cyan, a2OffX, a2OffY);
        }
    }

    private void DrawAlignMarkAtCell(DrawingContext dc, double dpi, Brush bgBrush,
        FovCell cell, string label, Color color,
        double offsetScreenX, double offsetScreenY)
    {
        if (_transform == null) return;
        var (cx0, cy0) = _transform.DataToScreen(cell.CenterX, cell.CenterY);
        DrawAlignCross(dc, dpi, bgBrush, cx0 + offsetScreenX, cy0 + offsetScreenY, label, color);
    }

    private void DrawAlignMarkMm(DrawingContext dc, double dpi, Brush bgBrush,
        double mmX, double mmY, string label, Color color)
    {
        if (_transform == null) return;
        var (cx, cy) = _transform.DataToScreen(mmX, mmY);
        DrawAlignCross(dc, dpi, bgBrush, cx, cy, label, color);

        // Tiny mm-coordinate caption under the label so the registered
        // position is visible on the canvas.
        var ft = new FormattedText(
            $"({mmX:F2}, {mmY:F2}) mm",
            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 10,
            new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)), dpi);
        dc.DrawRectangle(bgBrush, null,
            new Rect(cx + 20, cy + 8, ft.Width + 4, ft.Height + 2));
        dc.DrawText(ft, new Point(cx + 22, cy + 9));
    }

    private static void DrawAlignCross(DrawingContext dc, double dpi, Brush bgBrush,
        double cx, double cy, string label, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2.0);
        pen.Freeze();
        const double size = 15;
        dc.DrawLine(pen, new Point(cx - size, cy), new Point(cx + size, cy));
        dc.DrawLine(pen, new Point(cx, cy - size), new Point(cx, cy + size));

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
