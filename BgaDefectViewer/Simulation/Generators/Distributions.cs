namespace BgaDefectViewer.Simulation.Generators;

public static class Distributions
{
    /// <summary>Box-Muller Gaussian sample, clamped to [min, max].</summary>
    public static double NextGaussianClamped(Random rng, double mean, double stddev, double min, double max)
    {
        if (stddev <= 0) return Math.Clamp(mean, min, max);
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Clamp(mean + stddev * z, min, max);
    }

    public static double NextUniform(Random rng, double min, double max)
        => min + rng.NextDouble() * (max - min);

    public static bool NextBernoulli(Random rng, double p) => rng.NextDouble() < p;

    /// <summary>Sample k unique indices from [0, n) using Floyd's algorithm. O(k) time and memory.</summary>
    public static int[] SampleKWithoutReplacement(Random rng, int n, int k)
    {
        k = Math.Clamp(k, 0, n);
        if (k == 0) return Array.Empty<int>();
        if (k == n)
        {
            var all = new int[n];
            for (int i = 0; i < n; i++) all[i] = i;
            return all;
        }
        var set = new HashSet<int>(k);
        for (int j = n - k; j < n; j++)
        {
            int t = rng.Next(j + 1);
            if (!set.Add(t)) set.Add(j);
        }
        var result = new int[k];
        int idx = 0;
        foreach (var i in set) result[idx++] = i;
        return result;
    }
}
