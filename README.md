# BGA Defect Viewer

A WPF desktop viewer and analyzer for **BGA (Ball Grid Array) defect inspection data** produced by
Athlete-FA **KBGA** inspection/repair machines. It reads the raw output of an `AthleteSYS` tree
(master ball coordinates, substrate maps, per-substrate `.afa` defect files, lot `.summary.csv`)
and presents it through seven tightly cross-linked tabs, plus a dedicated **Overlap Inspection**
simulator that mirrors the real KBGA machine's FOV-based scan behavior.

- **Framework:** .NET 9.0 (Windows), WPF, MVVM
- **Language:** C# (nullable enabled, `AllowUnsafeBlocks` for fast drawing paths)
- **Entry point:** [BgaDefectViewer/Views/MainWindow.xaml](BgaDefectViewer/Views/MainWindow.xaml)
- **Solution:** [BGA Defectviewer.sln](BGA%20Defectviewer.sln)

---

## Tabs overview

The main window hosts a `TabControl` with seven tabs — see
[MainWindow.xaml:41-64](BgaDefectViewer/Views/MainWindow.xaml):

| # | Tab | ViewModel | Purpose |
|---|-----|-----------|---------|
| 0 | **Settings** | [SettingsViewModel](BgaDefectViewer/ViewModels/SettingsViewModel.cs) | File-status checklist for the currently selected Part/Lot (Master, Summary, `.map`, `.afa`, optional Map.csv) — which files were found, and their resolved paths. |
| 1 | **Lot Monitor** | [LotMonitorViewModel](BgaDefectViewer/ViewModels/LotMonitorViewModel.cs) | Substrate-level rows parsed from `{lot}.summary.csv`: substrate name, stage, OK/defect counts, plus the 5-line Lot Summary (1st Insp., Repaired, PPM, Repaired%, DateTime). Fires single-click / double-click events to drive other tabs. |
| 2 | **Substrate Map** | [SubstrateMapViewModel](BgaDefectViewer/ViewModels/SubstrateMapViewModel.cs) | Color-coded die-grid for each substrate and inspection round. Natural-sort substrate list, multi-die support, inspection-number selector, double-click a substrate or a single die to drill into the viewer. |
| 3 | **Substrate Viewer** | [SubstrateViewerViewModel](BgaDefectViewer/ViewModels/SubstrateViewerViewModel.cs) | Ball-level canvas: plots `MasterBall[]` coordinates and overlays `DefectBall`s from `.afa`, with inspection switching and per-die filtering for multi-die substrates. |
| 4 | **Defect Map** | [DefectMapViewModel](BgaDefectViewer/ViewModels/DefectMapViewModel.cs) | Lot-level pre-repair vs. post-repair heat maps, one per defect type (Missing / Extra / Shift / ETC / Bridge / Diameter / Unknown / Failed). Computed from `.map` files by default; falls back to `log/{lot}.map.csv`. |
| 5 | **Recurring Defects** | [RecurringDefectViewModel](BgaDefectViewer/ViewModels/RecurringDefectViewModel.cs) | Die-position histogram of defect occurrences across every substrate in the lot. Two-phase load: phase 1 tallies defect types per die from `.map`; phase 2 enriches with ball-level counts from `.afa` (async). Defect-type filters and min-count threshold. |
| 6 | **Overlap Inspection** | [OverlapInspectionViewModel](BgaDefectViewer/ViewModels/OverlapInspectionViewModel.cs) | FOV (Field-of-View) simulator matching the real KBGA machine: configures camera lens, Device Area, FOV size, overlap length, boundary mask and two alignment marks; computes the serpentine scan grid, detects duplicate balls in overlap zones, and renders three nested layers (Camera Raw / FOV / Effective). |

---

## Data source (AthleteSYS folder layout)

The app reads an **`AthleteSYS` root** that contains the KBGA machine's output tree:

```
AthleteSysRoot/
├─ kbgadata/                     (also accepts "KBGA Data")
│  └─ {PartNumber}/
│     └─ {PartNumber}.csv        ← Master ball coordinates: X,Y,Diameter
├─ kbgaresults/
│  └─ {PartNumber}/
│     └─ {LotNumber}/
│        ├─ {LotNumber}.summary.csv   ← substrate-level summary
│        ├─ *.afa                     ← per-substrate defect results
│        └─ *.map                     ← per-substrate die-grid matrices
└─ log/
   └─ {LotNumber}.map.csv         (optional — lot-level pre/post repair)
```

Folder resolution is handled by [FileLocator](BgaDefectViewer/Helpers/FileLocator.cs). A flat layout
where part-number folders sit directly under the selected root is also supported, and the
**Browse…** button auto-detects whether the user picked the root, a part folder or a lot folder via
[`FileLocator.AnalyzeSelectedFolder`](BgaDefectViewer/Helpers/FileLocator.cs:51).

---

## Cross-tab navigation

Tabs are wired together through events in
[MainViewModel](BgaDefectViewer/ViewModels/MainViewModel.cs):

- **Lot Monitor** row single-click → highlights the matching substrate in **Substrate Map**
  ([MainViewModel.cs:363](BgaDefectViewer/ViewModels/MainViewModel.cs#L363)).
- **Substrate Map** substrate single-click → selects the matching row in **Lot Monitor**
  ([MainViewModel.cs:369](BgaDefectViewer/ViewModels/MainViewModel.cs#L369)).
- **Lot Monitor** row double-click → switches to **Substrate Map** and auto-selects the matching
  substrate and INSP stage ([MainViewModel.cs:376](BgaDefectViewer/ViewModels/MainViewModel.cs#L376)).
- **Substrate Map** substrate double-click → loads the `.afa` file and switches to **Substrate
  Viewer** ([MainViewModel.cs:399](BgaDefectViewer/ViewModels/MainViewModel.cs#L399)).
- **Substrate Map** die double-click (multi-die substrates) → loads `.afa` filtered to that die
  column/row and switches to **Substrate Viewer**
  ([MainViewModel.cs:431](BgaDefectViewer/ViewModels/MainViewModel.cs#L431)).

Master CSV is loaded eagerly as soon as a Part# is selected, so Substrate Viewer and Overlap
Inspection can show the coordinate map even before a Lot is picked
([MainViewModel.cs:191-198](BgaDefectViewer/ViewModels/MainViewModel.cs#L191)).

---

## Settings persistence

User state lives in `%LocalAppData%\BgaDefectViewer\settings.json`
([AppSettings.cs:12](BgaDefectViewer/Models/AppSettings.cs#L12)) and stores:

- `AthleteSysPath` — last used source path
- `LastPartNumber` — last selected Part#
- `LastLotNumber` — last selected Lot#

These are auto-restored on startup and re-saved on every relevant change.

---

## Parsers

All parsers live in [BgaDefectViewer/Parsers/](BgaDefectViewer/Parsers).

| Parser | File format | Output |
|--------|-------------|--------|
| [`MasterCsvParser`](BgaDefectViewer/Parsers/MasterCsvParser.cs) | `{partNo}.csv` — one ball per line: `X,Y,Diameter` (mm) | `MasterBall[]` (1-based IDs); also `GetBounds()` for viewport fitting. |
| [`AfaFileParser`](BgaDefectViewer/Parsers/AfaFileParser.cs) | `.afa` — header fields (`StartTime;`, `Mapfile;`, `Recipe;`, `LotNo;`, `WaferId;`, `BallName;`, `BallDiameter;`, `Balls;`, `TotalBalls;`) then `INSPECTION=N` blocks with `Data;` (die index, col, row, code) and `BallData;` (ball ID, code, x, y, diameter) lines. | `AfaFile` with metadata + `InspectionResult[]` each containing a list of `DefectBall`. |
| [`SubstrateMapParser`](BgaDefectViewer/Parsers/SubstrateMapParser.cs) | `.map` — `INSPECTION=N` header, `{SubstrateID} OK=… MISS=… BRIDGE=…` stats, then rows of die characters (`G`/`M`/`E`/`B`/`D`/`C`/`S`/`U`/`F`/`O`/…). | `SubstrateMap` with one `MapInspection` per inspection (ball stats + `char[,]` die grid). |
| [`MapCsvParser`](BgaDefectViewer/Parsers/MapCsvParser.cs) | Optional `log/{lot}.map.csv` — lot-level `Pre Repair` / `Post Repair` matrices per defect type. | `DieMapData` with `DieMapPair[]`. |
| [`SummaryCsvParser`](BgaDefectViewer/Parsers/SummaryCsvParser.cs) | `{lot}.summary.csv` — multi-section file with repeated `LOT Start` blocks and an optional `LOT Summary` aggregate. The **latest** lot block is used. | `LotSession` with `SummaryRow[]` and 5-line `LotSummary`. If `LOT Summary` is missing it is computed from the rows (see [`LotSummaryCalculator`](BgaDefectViewer/Helpers/LotSummaryCalculator.cs)). |

---

## Helpers (pure logic)

[BgaDefectViewer/Helpers/](BgaDefectViewer/Helpers):

| Helper | Role |
|--------|------|
| [`CoordinateTransform`](BgaDefectViewer/Helpers/CoordinateTransform.cs) | Maps data mm ↔ screen pixels for every canvas. Stores bounds, canvas size, base scale, zoom and pan, with `DataToScreen` / `ScreenToData` / `BallRadiusPixels` / `CenterOn` / `ResetToFit`. |
| [`DieJudge`](BgaDefectViewer/Helpers/DieJudge.cs) | Die-character → `DieInfo` (name + fill/foreground colour). F/M/E/B/D/C/S/U/G/`1` plus lowercase "mixed-defects" variants. |
| [`DieMapCalculator`](BgaDefectViewer/Helpers/DieMapCalculator.cs) | Aggregates all `.map` files in a lot into `DieMapData` — one pre-repair (INSPECTION=1) and one post-repair (last INSPECTION) matrix per defect type. |
| [`FovGridCalculator`](BgaDefectViewer/Helpers/FovGridCalculator.cs) | Overlap-inspection geometry: `CalculateBallClusterCenter`, `CalculateFovGrid` (serpentine scan, odd rows L→R / even R→L), `AssignBallsToFovCells` (dedup by scan order + boundary-mask aware), `DetectDuplicateBalls`, `CalculateOverlapRegions`, `ValidateParams`. |
| [`LotSummaryCalculator`](BgaDefectViewer/Helpers/LotSummaryCalculator.cs) | Rebuilds the 5-line `LotSummary` from `SummaryRow[]` when the CSV lacks it — dedup by (name, stage), split stage-1 (1st inspection) vs. later (repaired), compute counts / PPM / yield%. |
| [`RecurringDefectCalculator`](BgaDefectViewer/Helpers/RecurringDefectCalculator.cs) | Phase 1: per-die defect-type tally from `.map` INSPECTION=1 grids. Phase 2 (`EnrichWithAfaData`): per-ball defect counts from `.afa` files. |
| [`FileLocator`](BgaDefectViewer/Helpers/FileLocator.cs) | All filesystem resolution — folder-name variants (`kbgadata` vs `KBGA Data`), `AnalyzeSelectedFolder` for Browse auto-detection, Find* helpers for each file kind. |
| `RelayCommand`, `ViewModelBase`, `BindingProxy` | MVVM boilerplate. |

---

## Key models

[BgaDefectViewer/Models/](BgaDefectViewer/Models):

- **`MasterBall`** — 1-based ball ID, X/Y (mm), Diameter (mm). The immutable coordinate truth.
- **`DefectBall`** — BallId (−1 for Extras), defect code, X/Y/Diameter, with computed `DefectName`,
  `IsExtra` and formatted display fields.
- **`SubstrateMap`** / **`MapInspection`** — one substrate's `.map`; per-inspection ball stats and a
  `char[,] DieGrid`. Helpers for `IsMultiDie`, `LastInspection`, `SingleDieChar`.
- **`AfaFile`** — full `.afa` content: header metadata + `InspectionResult[]`.
- **`LotSession`** / **`SummaryRow`** / **`LotSummary`** — output of `SummaryCsvParser`.
- **`DieMapData`** / **`DieMapPair`** — lot-level defect heat maps.
- **`RecurringDefectData`** / **`RecurringDieInfo`** — recurring-defect histogram grid.
- **`FovCell`** — FOV grid element: `GridX/GridY`, serpentine `ScanIndex`, center + half-size in
  mm, and the list of assigned master ball IDs.
- **`OverlapParams`** — full config for the overlap simulator: device area, FOV size, overlap
  length, boundary mask, staggered-pattern flag, duplication allowance (px), two alignment-mark
  FOV coordinates, camera-lens preset (Normal 94.52×62.87 / Enlarged 109.66×72.95 / Custom), and
  the three layer-visibility flags (Camera Raw / FOV / Effective).
- **`DuplicateBallPair`** — overlap warning pair: two master balls inside the same overlap zone,
  plus the two FOV scan indices and the distance in pixels.
- **`DefectTypes` / `DefectTypeInfo`** — catalog of defect codes (2–30 real, 1001–1010 pseudo for
  recurring-defect colour ramps).
- **`AppSettings`** — persisted user state (see above).

---

## Custom controls

[BgaDefectViewer/Controls/](BgaDefectViewer/Controls):

- **[`BallMapCanvas`](BgaDefectViewer/Controls/BallMapCanvas.cs)** — high-performance 2D renderer
  built on stacked `DrawingVisual` layers (background, bitmap, defects, selection, dimensions,
  measure, stamps, hover). Mouse wheel zoom, drag pan, selection highlight, probe mode (click-to-
  stamp), precise click-to-click measurement. Used by Substrate Viewer **and** Overlap Inspection.
- **[`FovOverlayCanvas`](BgaDefectViewer/Controls/FovOverlayCanvas.cs)** — transparent overlay on
  top of `BallMapCanvas` with ten z-ordered layers: Camera Raw frame, Device Area rectangle, FOV
  grid, Effective (inspection) area, overlap zones, edge masks, serpentine scan path, ball
  cluster crosshair, alignment-mark circles (A1/A2), duplicate-ball connectors.
- **[`DieGridControl`](BgaDefectViewer/Controls/DieGridControl.xaml)** — templated die-matrix grid
  with header row/column, click selection and double-click navigation. Used by Substrate Map.

---

## Overlap Inspection simulator

The **Overlap Inspection** tab is the project's most specialised feature — a simulator for the
real KBGA machine's overlapping-FOV scan strategy. See
[OverlapInspectionViewModel.cs](BgaDefectViewer/ViewModels/OverlapInspectionViewModel.cs).

**Inputs** (all live-bound):

- Enable / disable the simulation.
- **Device Area** (mm) — auto-populated from master ball cluster span + 2 mm margin.
- **FOV size** and **Overlap length** (mm) — camera-field geometry.
- **Boundary mask** (mm) — edge of each FOV that the real machine ignores (so a ball straddling
  the mask is only inspected once, by the neighbour).
- **Staggered pattern** checkbox and **Duplication allowance** (px) for dedup tolerance.
- **Alignment 1 / Alignment 2** FOV coordinates — Alignment 2 **auto-tracks** the diagonal bottom-
  right FOV until the user edits it, and re-enables tracking whenever a new device is loaded
  ([OverlapInspectionViewModel.cs:124](BgaDefectViewer/ViewModels/OverlapInspectionViewModel.cs#L124)).
- **Camera Type** presets: Normal 94.52 × 62.87, Enlarged 109.66 × 72.95, or Custom.
- Three independent **layer toggles**: Camera Raw (full sensor footprint) / FOV (photographed
  area) / Effective (actually inspected area after masking).

**Output** (rendered only after pressing **Execute**):

- FOV grid with serpentine scan path and scan-index labels.
- Per-FOV ball counts list.
- Duplicate-ball connectors in overlap zones (balls are deduplicated onto the latest-scan FOV,
  matching real machine behaviour — see [commit 2822825](#)).
- Masked-out balls are hidden from the canvas to mirror what the machine would actually inspect.
- Validation panel with three quick sanity checks: FOV union covers Device Area, Camera Raw fits
  FOV, Overlap ≥ 2 × boundary mask.
- Summary line: FOV count, inspected balls / total (with masked count), number of overlap zones,
  number of deduplicated shared balls.

---

## Project layout

```
BgaDefectViewer/
├─ App.xaml, App.xaml.cs, AssemblyInfo.cs, GlobalUsings.cs
├─ BgaDefectViewer.csproj              (net9.0-windows, UseWPF, AllowUnsafeBlocks)
├─ Controls/                           BallMapCanvas, FovOverlayCanvas, DieGridControl
├─ Converters/                         CountToColor, DefectCodeToColor, DieLetterToColor, JudgeToRowColor
├─ Helpers/                            CoordinateTransform, DieJudge, DieMapCalculator,
│                                      FileLocator, FovGridCalculator, LotSummaryCalculator,
│                                      RecurringDefectCalculator, RelayCommand, ViewModelBase,
│                                      BindingProxy
├─ Models/                             AppSettings, AfaFile, DefectBall, DefectTypes/DefectTypeInfo,
│                                      DieMapData, DuplicateBallPair, FilePathConfig, FolderAnalysisResult,
│                                      FovCell, InspectionResult, LotSession, LotSummary, MasterBall,
│                                      OverlapParams, RecurringDefectData, SubstrateMap, SummaryRow
├─ Parsers/                            AfaFileParser, MapCsvParser, MasterCsvParser,
│                                      SubstrateMapParser, SummaryCsvParser
├─ ViewModels/                         MainViewModel + one per tab (×7)
└─ Views/                              MainWindow + one per tab (×7)

Test_files/                            Sample AthleteSYS trees used for manual testing
                                       (PSPREP_*, PSPUBL_*, Q19444Aa, kensa, test, 306/AthleteSYS)
```

---

## Building & running

Requires the **.NET 9 SDK** and Windows (WPF target is `net9.0-windows`).

```bash
dotnet build "BGA Defectviewer.sln"
dotnet run --project BgaDefectViewer/BgaDefectViewer.csproj
```

On first launch, press **Browse…**, point at any level of an `AthleteSYS` tree (root, part folder
or lot folder — all auto-detected), then pick Part# and Lot#. Remaining tabs populate automatically
and the selection is remembered across runs via `settings.json`.
