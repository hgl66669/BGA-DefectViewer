using System.Windows.Media.Imaging;
using BgaDefectViewer.Helpers;
using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Processing;

/// <summary>
/// Stage 2 prototype defect judge. Currently implements only <see
/// cref="DefectCode.Miss"/>: for each master position, count white pixels
/// inside the pad disk on the binarized frame; if the white-fill ratio is
/// below <see cref="BinarizationParams.MissingFillRatio"/>, the pad is
/// classified as Miss. Pads whose centre falls outside the rendered
/// viewport are reported as <see cref="DefectCode.EO"/> (no data).
/// Future defect judges (Shift / Extra / Bridge / ...) can plug in by
/// adding new branches; the result schema is already complete.
/// </summary>
public static class Inspector
{
    public static unsafe InspectionFrame RunMissingDetection(
        WriteableBitmap binary,
        CoordinateTransform transform,
        SimulatedMaster[] masters,
        BinarizationParams binParams,
        SimulationParams simParams)
    {
        var results = new PadInspectionResult[masters.Length];
        var counts = new Dictionary<DefectCode, int>();

        int w = binary.PixelWidth;
        int h = binary.PixelHeight;
        int stridePixels = binary.BackBufferStride / 4;
        uint* pBuf = (uint*)binary.BackBuffer;

        // Use pad diameter from sim params; fall back to master diameter if pads are disabled.
        double radiusMm = simParams.PadEnabled && simParams.PadDiameter > 0
            ? simParams.PadDiameter / 2.0
            : simParams.MasterDiameter / 2.0;
        if (radiusMm <= 0) radiusMm = 0.05;
        double r2Mm = radiusMm * radiusMm;

        for (int idx = 0; idx < masters.Length; idx++)
        {
            ref readonly var m = ref masters[idx];

            var (sxMin0, syMax0) = transform.DataToScreen(m.X - radiusMm, m.Y - radiusMm);
            var (sxMax0, syMin0) = transform.DataToScreen(m.X + radiusMm, m.Y + radiusMm);
            int sxMin = Math.Max(0, Math.Min(sxMin0, sxMax0));
            int sxMax = Math.Min(w - 1, Math.Max(sxMin0, sxMax0));
            int syMin = Math.Max(0, Math.Min(syMin0, syMax0));
            int syMax = Math.Min(h - 1, Math.Max(syMin0, syMax0));

            int total = 0;
            int white = 0;

            if (sxMax >= sxMin && syMax >= syMin)
            {
                for (int sy = syMin; sy <= syMax; sy++)
                {
                    uint* row = pBuf + sy * stridePixels;
                    for (int sx = sxMin; sx <= sxMax; sx++)
                    {
                        var (dx, dy) = transform.ScreenToData(sx + 0.5, sy + 0.5);
                        double ddx = dx - m.X;
                        double ddy = dy - m.Y;
                        if (ddx * ddx + ddy * ddy > r2Mm) continue;
                        total++;
                        // Binary bitmap pixels are either 0 or 255 (B channel).
                        if ((row[sx] & 0xFFu) >= 128) white++;
                    }
                }
            }

            DefectCode code;
            double ratio;
            if (total == 0)
            {
                // Pad lies outside the rendered viewport — no data to inspect.
                code = DefectCode.EO;
                ratio = -1.0;
            }
            else
            {
                ratio = (double)white / total;
                code = ratio < binParams.MissingFillRatio ? DefectCode.Miss : DefectCode.OK;
            }

            results[idx] = new PadInspectionResult
            {
                MasterIndex = idx,
                Code = code,
                WhiteFillRatio = ratio,
            };
            counts[code] = counts.GetValueOrDefault(code) + 1;
        }

        return new InspectionFrame
        {
            PadResults = results,
            Counts = counts,
        };
    }
}
