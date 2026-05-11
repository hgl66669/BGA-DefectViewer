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
                double acircularity = Distributions.NextGaussianClamped(rng,
                    p.BlobAcircularityMean, p.BlobAcircularityStdDev, 0.7, 3.0);
                double score = Distributions.NextGaussianClamped(rng,
                    p.BlobScoreMean, p.BlobScoreStdDev, 0.0, 1.0);
                byte brightness = (byte)Math.Clamp(
                    Distributions.NextGaussianClamped(rng,
                        p.BlobBrightnessMean, p.BlobBrightnessStdDev, 0, 255),
                    0, 255);

                // Pixel-domain derivations via calibration
                double radiusPx = (diameter / 2.0) / Math.Max(1e-9, p.MmPerPixel);
                double idealArea = Math.PI * radiusPx * radiusPx;
                double idealPerimeter = 2 * Math.PI * radiusPx;
                // Acircularity ≈ (P^2)/(4πA); recover area from acircularity
                double area = idealArea / Math.Max(1e-9, acircularity);
                double perimeter = idealPerimeter * Math.Sqrt(Math.Max(1.0, acircularity));

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

        return blobs;
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
