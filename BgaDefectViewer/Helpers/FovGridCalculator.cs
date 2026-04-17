using BgaDefectViewer.Models;
using BgaDefectViewer.Parsers;

namespace BgaDefectViewer.Helpers;

/// <summary>
/// Static utility for all FOV grid calculations used in overlap inspection simulation.
/// </summary>
public static class FovGridCalculator
{
    /// <summary>
    /// Calculate ball cluster center and span from the outermost balls.
    /// </summary>
    public static (double centerX, double centerY, double spanX, double spanY)
        CalculateBallClusterCenter(MasterBall[] balls)
    {
        var (minX, maxX, minY, maxY) = MasterCsvParser.GetBounds(balls);
        return ((minX + maxX) / 2.0, (minY + maxY) / 2.0, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Build the FOV grid with physical positions relative to the ball cluster center.
    /// FOV count is auto-calculated from DeviceArea, FovSize, and MoveDist.
    /// Uses the formula from the spec:
    ///   offset_x = -1 * (centerFovX - gridX) * MoveDistX
    ///   offset_y = (centerFovY - gridY) * MoveDistY
    /// </summary>
    public static List<FovCell> CalculateFovGrid(OverlapParams p,
        double clusterCenterX, double clusterCenterY)
    {
        var cells = new List<FovCell>();
        int countX = p.FovCountX;
        int countY = p.FovCountY;
        double moveDistX = p.MoveDistX;
        double moveDistY = p.MoveDistY;

        double centerFovX = (1 + countX) / 2.0;
        double centerFovY = (1 + countY) / 2.0;

        for (int y = 1; y <= countY; y++)
        {
            for (int x = 1; x <= countX; x++)
            {
                double offsetX = -1.0 * (centerFovX - x) * moveDistX;
                double offsetY = (centerFovY - y) * moveDistY;

                cells.Add(new FovCell
                {
                    GridX = x,
                    GridY = y,
                    CenterX = clusterCenterX + offsetX,
                    CenterY = clusterCenterY + offsetY,
                    HalfWidth = p.FovSizeX / 2.0,
                    HalfHeight = p.FovSizeY / 2.0,
                });
            }
        }

        AssignScanOrder(cells, countX, countY);
        return cells;
    }

    /// <summary>
    /// Assign serpentine scan indices to FOV cells (1-based).
    /// Odd rows: left-to-right; even rows: right-to-left.
    /// </summary>
    public static void AssignScanOrder(List<FovCell> cells, int maxX, int maxY)
    {
        int idx = 1;
        for (int y = 1; y <= maxY; y++)
        {
            if (y % 2 == 1) // odd row: left to right
            {
                for (int x = 1; x <= maxX; x++)
                {
                    var cell = cells.First(c => c.GridX == x && c.GridY == y);
                    cell.ScanIndex = idx++;
                }
            }
            else // even row: right to left
            {
                for (int x = maxX; x >= 1; x--)
                {
                    var cell = cells.First(c => c.GridX == x && c.GridY == y);
                    cell.ScanIndex = idx++;
                }
            }
        }
    }

    /// <summary>
    /// For each FOV cell, populate BallIds with master balls whose position
    /// falls within that cell's rectangle.
    /// </summary>
    public static void AssignBallsToFovCells(List<FovCell> cells, MasterBall[] balls)
    {
        foreach (var cell in cells)
            cell.BallIds.Clear();

        foreach (var ball in balls)
        {
            foreach (var cell in cells)
            {
                if (ball.X >= cell.Left && ball.X <= cell.Right &&
                    ball.Y >= cell.Bottom && ball.Y <= cell.Top)
                {
                    cell.BallIds.Add(ball.Id);
                }
            }
        }
    }

    /// <summary>
    /// Calculate overlap rectangles between adjacent FOV cells.
    /// Returns list of (left, bottom, right, top) in data coordinates.
    /// </summary>
    public static List<(double left, double bottom, double right, double top)>
        CalculateOverlapRegions(List<FovCell> cells)
    {
        var regions = new List<(double, double, double, double)>();
        for (int i = 0; i < cells.Count; i++)
        {
            for (int j = i + 1; j < cells.Count; j++)
            {
                var a = cells[i];
                var b = cells[j];

                // Only adjacent cells (differ by 1 in X or Y, not diagonal)
                int dx = Math.Abs(a.GridX - b.GridX);
                int dy = Math.Abs(a.GridY - b.GridY);
                if (!((dx == 1 && dy == 0) || (dx == 0 && dy == 1)))
                    continue;

                // Compute rectangle intersection
                double left = Math.Max(a.Left, b.Left);
                double right = Math.Min(a.Right, b.Right);
                double bottom = Math.Max(a.Bottom, b.Bottom);
                double top = Math.Min(a.Top, b.Top);

                if (left < right && bottom < top)
                    regions.Add((left, bottom, right, top));
            }
        }
        return regions;
    }

    /// <summary>
    /// Detect duplicate balls: balls that appear in multiple FOV cells
    /// (i.e., they are in the overlap zone between FOVs).
    /// </summary>
    public static List<DuplicateBallPair> DetectDuplicateBalls(
        List<FovCell> cells, MasterBall[] balls)
    {
        var duplicates = new List<DuplicateBallPair>();

        // Build ball lookup by ID
        var ballById = new Dictionary<int, MasterBall>();
        foreach (var b in balls)
            ballById[b.Id] = b;

        // Find balls that appear in multiple FOV cells
        var ballFovMap = new Dictionary<int, List<FovCell>>();
        foreach (var cell in cells)
        {
            foreach (var ballId in cell.BallIds)
            {
                if (!ballFovMap.ContainsKey(ballId))
                    ballFovMap[ballId] = new List<FovCell>();
                ballFovMap[ballId].Add(cell);
            }
        }

        // Balls in multiple FOVs are duplicates
        foreach (var (ballId, fovList) in ballFovMap)
        {
            if (fovList.Count <= 1) continue;

            var ball = ballById[ballId];
            // Record once per ball (any pair of FOVs is sufficient)
            duplicates.Add(new DuplicateBallPair
            {
                BallA = ball,
                BallB = ball,
                FovA = fovList[0].ScanIndex,
                FovB = fovList[1].ScanIndex,
                Distance = 0,
            });
        }

        return duplicates;
    }

    /// <summary>
    /// Validate parameters.
    /// Errors are hard blockers (geometry can't be computed).
    /// Warnings are advisory (e.g., boundary mask creates inspection dead zones).
    /// </summary>
    public static (List<string> errors, List<string> warnings) ValidateParams(OverlapParams p)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (p.DeviceAreaX <= 0) errors.Add("Device Area X must be > 0");
        if (p.DeviceAreaY <= 0) errors.Add("Device Area Y must be > 0");
        if (p.FovSizeX <= 0) errors.Add("FOV X must be > 0");
        if (p.FovSizeY <= 0) errors.Add("FOV Y must be > 0");
        if (p.OverlapLengthX < 0) errors.Add("Overlap Length X must be >= 0");
        if (p.OverlapLengthY < 0) errors.Add("Overlap Length Y must be >= 0");
        if (p.OverlapLengthX >= p.FovSizeX) errors.Add("Overlap Length X must be < FOV X");
        if (p.OverlapLengthY >= p.FovSizeY) errors.Add("Overlap Length Y must be < FOV Y");
        if (p.BoundaryMaskX < 0) errors.Add("Boundary Mask X must be >= 0");
        if (p.BoundaryMaskY < 0) errors.Add("Boundary Mask Y must be >= 0");
        if (p.DuplicationAllowancePix < 0) errors.Add("Duplication Allowance must be >= 0");

        if (p.Alignment1FovX < 1 || p.Alignment1FovX > p.FovCountX)
            errors.Add($"Align 1 X must be 1~{p.FovCountX}");
        if (p.Alignment1FovY < 1 || p.Alignment1FovY > p.FovCountY)
            errors.Add($"Align 1 Y must be 1~{p.FovCountY}");
        if (p.Alignment2FovX < 1 || p.Alignment2FovX > p.FovCountX)
            errors.Add($"Align 2 X must be 1~{p.FovCountX}");
        if (p.Alignment2FovY < 1 || p.Alignment2FovY > p.FovCountY)
            errors.Add($"Align 2 Y must be 1~{p.FovCountY}");

        // Overlap must be wide enough to cover the boundary mask on both
        // adjacent FOVs, otherwise a strip at each FOV seam is never inspected.
        if (p.OverlapLengthX < 2 * p.BoundaryMaskX)
            warnings.Add($"Overlap X ({p.OverlapLengthX:F3}) < 2 × Boundary Mask X ({p.BoundaryMaskX:F3}): inspection dead zone at FOV seams");
        if (p.OverlapLengthY < 2 * p.BoundaryMaskY)
            warnings.Add($"Overlap Y ({p.OverlapLengthY:F3}) < 2 × Boundary Mask Y ({p.BoundaryMaskY:F3}): inspection dead zone at FOV seams");

        return (errors, warnings);
    }
}
