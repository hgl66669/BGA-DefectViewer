using System.Text.RegularExpressions;

namespace BgaDefectViewer.Models;

/// <summary>
/// Substrate layout encoded in the `.afa` file's `Mapfile;` line.
///
/// In legacy KBGA the `Mapfile;` carried a real `.map` filename. In the new
/// firmware it carries a layout token `{N}C,{M}R`:
///   - `0C,0R` ⇒ single-unit substrate (the only physical unit IS the substrate)
///   - `{N}C,{M}R` (N,M > 0) ⇒ multi-unit substrate with N×M devices laid out
///     across the substrate (kept for forward compatibility — current samples
///     are all single-unit).
/// </summary>
public abstract record SubstrateLayout
{
    public sealed record SingleUnit() : SubstrateLayout;
    public sealed record MultiUnit(int Cols, int Rows) : SubstrateLayout;

    private static readonly Regex Pattern = new(@"^\s*(\d+)C\s*,\s*(\d+)R\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse the raw `Mapfile;` value. Anything that doesn't look like the
    /// `{N}C,{M}R` token (including legacy filenames such as `ABC123.map`)
    /// returns <see cref="SingleUnit"/> as a safe default — old code paths
    /// don't read the layout, so the legacy reading is unaffected.
    /// </summary>
    public static SubstrateLayout FromMapfileToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return new SingleUnit();
        var m = Pattern.Match(token);
        if (!m.Success) return new SingleUnit();
        var c = int.Parse(m.Groups[1].Value);
        var r = int.Parse(m.Groups[2].Value);
        return (c == 0 && r == 0) ? new SingleUnit() : new MultiUnit(c, r);
    }

    public string DisplayName => this switch
    {
        SingleUnit       => "Single Unit",
        MultiUnit(var c, var r) => $"Multi Unit ({c}×{r})",
        _ => "Unknown",
    };
}
