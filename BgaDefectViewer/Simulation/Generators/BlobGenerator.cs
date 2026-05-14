using BgaDefectViewer.Simulation.Models;

namespace BgaDefectViewer.Simulation.Generators;

public static class BlobGenerator
{
    public static SimulatedBlob[] Generate(SimulatedMaster[] masters, SimulationParams p)
    {
        var blobs = new SimulatedBlob[masters.Length];

        // Pre-select missing / offset indices (single-threaded for reproducibility)
        bool[] isMissing = new bool[masters.Length];
        bool[] isOffset = new bool[masters.Length];
        var setupRng = new Random(p.Seed ^ 0x9E37);

        if (p.Mode == SimulationMode.RandomMissing)
        {
            if (p.MissingQuantityMode == QuantityMode.Probability)
            {
                for (int i = 0; i < masters.Length; i++)
                    isMissing[i] = Distributions.NextBernoulli(setupRng, p.MissingProbability);
            }
            else
            {
                foreach (var i in Distributions.SampleKWithoutReplacement(setupRng, masters.Length, p.MissingCount))
                    isMissing[i] = true;
            }
        }
        else if (p.Mode == SimulationMode.RandomOffset)
        {
            if (p.OffsetQuantityMode == QuantityMode.Probability)
            {
                for (int i = 0; i < masters.Length; i++)
                    isOffset[i] = Distributions.NextBernoulli(setupRng, p.OffsetProbability);
            }
            else
            {
                foreach (var i in Distributions.SampleKWithoutReplacement(setupRng, masters.Length, p.OffsetCount))
                    isOffset[i] = true;
            }
        }

        // Generate blob features in parallel (per-row RNG for reproducibility + thread-safety)
        Parallel.For(0, p.Rows, row =>
        {
            var rng = new Random(p.Seed ^ row ^ 0x5A5A);
            int baseIdx = row * p.Cols;
            for (int col = 0; col < p.Cols; col++)
            {
                int idx = baseIdx + col;
                var master = masters[idx];

                bool present = !isMissing[idx];
                double diameter = Distributions.NextGaussianClamped(rng,
                    p.BlobDiameterMean, p.BlobDiameterStdDev,
                    p.BlobDiameterMean * 0.5, p.BlobDiameterMean * 1.5);
                // KBGA's ACIRCULARITY = raw P²/(4πA), 1.0 = circle. Real
                // detected balls cluster around 1.1–1.3 due to pixel-rasterization
                // noise on perimeter measurement. KBGA recipe OK range: 0.7–3.0.
                double acircularity = Distributions.NextGaussianClamped(rng,
                    p.BlobAcircularityMean, p.BlobAcircularityStdDev, 0.5, 5.0);
                // Radial geometric deformation γ (independent of acircularity).
                double shapeDeform = Distributions.NextGaussianClamped(rng,
                    p.BlobShapeDeformationMean, p.BlobShapeDeformationStdDev, 0.0, 0.5);
                double score = Distributions.NextGaussianClamped(rng,
                    p.BlobScoreMean, p.BlobScoreStdDev, 0.0, 1.0);
                byte brightness = (byte)Math.Clamp(
                    Distributions.NextGaussianClamped(rng,
                        p.BlobBrightnessMean, p.BlobBrightnessStdDev, 0, 255),
                    0, 255);

                // Pixel-domain derivations via calibration. With KBGA's raw
                // P²/(4πA) acircularity (1.0 = circle) and area ≈ π r² conserved,
                //   acircularity = P²/(4πA)   ⇒   P = 2π r · √(acircularity)
                double radiusPx = (diameter / 2.0) / Math.Max(1e-9, p.MmPerPixel);
                double area = Math.PI * radiusPx * radiusPx;
                double perimeter = 2 * Math.PI * radiusPx * Math.Sqrt(Math.Max(1.0, acircularity));

                double shiftX = 0, shiftY = 0;
                double cx = master.X, cy = master.Y;
                if (isOffset[idx])
                {
                    double mag = Distributions.NextUniform(rng, p.OffsetMinMm, p.OffsetMaxMm);
                    double angle = Distributions.NextUniform(rng, 0, 2 * Math.PI);
                    shiftX = mag * Math.Cos(angle);
                    shiftY = mag * Math.Sin(angle);
                    cx += shiftX;
                    cy += shiftY;
                }

                blobs[idx] = new SimulatedBlob
                {
                    MasterIndex = idx,
                    CenterX = cx,
                    CenterY = cy,
                    Diameter = diameter,
                    Acircularity = acircularity,
                    ShapeDeformation = shapeDeform,
                    Score = score,
                    Area = area,
                    Perimeter = perimeter,
                    ShiftX = shiftX,
                    ShiftY = shiftY,
                    Brightness = brightness,
                    IsPresent = present
                };
            }
        });

        if (p.EnableCollision && p.Mode == SimulationMode.RandomOffset)
            ResolveCollisions(blobs, masters, p);

        return blobs;
    }

    /// <summary>Push offset balls that overlap a neighbor back to exact contact
    /// distance (centers = r₁_eff + r₂_eff). Effective radius includes radial
    /// ShapeDeformation so highly-perturbed balls collide earlier. Fixed
    /// (non-shifted) balls are anchored and only push, never pushed.
    /// Iterates up to 4 sweeps for chains of contacts to settle.
    ///
    /// Search range is dynamic: a ball offset by N pitches can collide with a
    /// neighbour N grid-cells away, so an 8-neighbour scan misses anything
    /// beyond 1 pitch. We derive the scan radius from the maximum offset
    /// magnitude and ball radius so the fix covers default (offset &lt; 1·pitch)
    /// and pathological (offset » pitch) parameters alike.</summary>
    private static void ResolveCollisions(SimulatedBlob[] blobs, SimulatedMaster[] masters, SimulationParams p)
    {
        int cols = p.Cols;
        int rows = p.Rows;
        const int maxIter = 4;

        double maxOffset = Math.Max(Math.Abs(p.OffsetMaxMm), Math.Abs(p.OffsetMinMm));
        double maxRadius = p.BlobDiameterMean * 0.5
                           * (1.0 + Math.Max(0, p.BlobShapeDeformationMean + 3 * p.BlobShapeDeformationStdDev));
        double minPitch = Math.Max(1e-6, Math.Min(p.PitchX, p.PitchY));
        // +1 cell margin so cascade-pushed balls still see the next ring of neighbours.
        int searchRange = Math.Max(1, (int)Math.Ceiling((maxOffset + 2 * maxRadius) / minPitch) + 1);

        for (int iter = 0; iter < maxIter; iter++)
        {
            bool anyChange = false;
            for (int idx = 0; idx < blobs.Length; idx++)
            {
                ref var blob = ref blobs[idx];
                if (!blob.IsPresent) continue;
                // Only move balls that were originally offset (or were displaced
                // by a previous iteration). Fixed pads stay anchored.
                if (blob.ShiftX == 0 && blob.ShiftY == 0) continue;

                int row = idx / cols;
                int col = idx % cols;
                double rSelf = (blob.Diameter / 2.0) * (1.0 + blob.ShapeDeformation);
                double cx = blob.CenterX;
                double cy = blob.CenterY;
                bool moved = false;

                for (int dr = -searchRange; dr <= searchRange; dr++)
                {
                    int nr = row + dr;
                    if (nr < 0 || nr >= rows) continue;
                    for (int dc = -searchRange; dc <= searchRange; dc++)
                    {
                        if (dr == 0 && dc == 0) continue;
                        int nc = col + dc;
                        if (nc < 0 || nc >= cols) continue;

                        int nIdx = nr * cols + nc;
                        ref var nb = ref blobs[nIdx];
                        if (!nb.IsPresent) continue;

                        double rNb = (nb.Diameter / 2.0) * (1.0 + nb.ShapeDeformation);
                        double minD = rSelf + rNb;
                        // Per-pair gap — reproducible from (idx, nIdx, seed).
                        double gap = PairGap(idx, nIdx, p.Seed, p.CollisionGapMean, p.CollisionGapVariance);
                        if (gap < -0.005) gap = -0.005;
                        else if (gap > 0.050) gap = 0.050;
                        double targetD = minD + gap;
                        if (targetD < 1e-9) targetD = minD;  // safety
                        double ddx = cx - nb.CenterX;
                        double ddy = cy - nb.CenterY;
                        double d2 = ddx * ddx + ddy * ddy;
                        if (d2 >= targetD * targetD) continue;

                        double d = Math.Sqrt(d2);
                        if (d < 1e-9) { ddx = 1; ddy = 0; d = 1; }  // degenerate co-centric
                        double push = (targetD - d) / d;
                        cx += ddx * push;
                        cy += ddy * push;
                        moved = true;
                    }
                }

                if (moved)
                {
                    blob.CenterX = cx;
                    blob.CenterY = cy;
                    blob.ShiftX = cx - masters[idx].X;
                    blob.ShiftY = cy - masters[idx].Y;
                    anyChange = true;
                }
            }
            if (!anyChange) break;
        }
    }

    /// <summary>Deterministic uniform gap from a per-pair hash. Symmetric in
    /// (idxA, idxB) so both balls of a pair use the same gap. Output in
    /// [mean − variance, mean + variance]; caller clamps to a physical range.</summary>
    private static double PairGap(int idxA, int idxB, int seed, double mean, double variance)
    {
        if (variance <= 0) return mean;
        uint lo = (uint)Math.Min(idxA, idxB);
        uint hi = (uint)Math.Max(idxA, idxB);
        uint h = lo * 2654435761u ^ hi * 0x9E3779B9u ^ (uint)seed * 0x85EBCA6Bu;
        h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13;
        double u = (h & 0xFFFFFFu) / (double)0x1000000u;     // [0, 1)
        return mean + (u * 2.0 - 1.0) * variance;            // [mean−v, mean+v]
    }

    public static SimulationStats ComputeStats(SimulatedBlob[] blobs, long genMs)
    {
        int present = 0, missing = 0, shifted = 0;
        for (int i = 0; i < blobs.Length; i++)
        {
            if (blobs[i].IsPresent)
            {
                present++;
                if (blobs[i].ShiftX != 0 || blobs[i].ShiftY != 0) shifted++;
            }
            else missing++;
        }
        return new SimulationStats(blobs.Length, present, missing, shifted, genMs);
    }
}
