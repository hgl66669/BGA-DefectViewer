namespace BgaDefectViewer.Simulation.Models;

/// <summary>
/// Defect classification — numeric values match KBGA error codes used in
/// the BM-3300SI inspection software. Colours/letters are defined in
/// KBGA-O1 (操作方法) Ver.1.3.7 PDF §2.4.1.3. Only <c>Miss</c> is wired
/// to a detection function in the Stage 2 prototype; the rest of the enum
/// is reserved so future judges (Shift / Extra / Bridge / etc.) can plug in
/// without breaking the result schema.
/// </summary>
public enum DefectCode
{
    OK = 0,         // G/g — Good
    Miss = 2,       // M  — シアン (cyan)
    Shift = 3,      // S  — 黄 (yellow)
    Extra = 4,      // E  — 薄赤 (light red)
    Bridge = 11,    // B  — マジェンタ (magenta)
    SD = 21,        // D  — 紫 (purple) — Small Diameter
    LD = 22,        // D  — 紫 (purple) — Large Diameter
    ETC = 30,       // C  — 橙 (orange)
    EO = 99,        // U  — 青 (blue) — Unknown / E.O.
    Failure = 100,  // F  — 赤 (red) — gross alignment failure
}
