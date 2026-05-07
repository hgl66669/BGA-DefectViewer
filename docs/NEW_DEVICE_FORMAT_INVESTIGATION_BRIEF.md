# 新版設備 Log 檔案格式調查 — 給調查 Agent 的交辦書

## 0. 任務目的

本專案 `BgaDefectViewer` 目前只支援 **既有 Athlete-FA 設備**（KBGA / GTX 系列）所產出的目錄結構與檔案格式。客戶手上已開始導入 **新版設備**，其 Log / 結果檔的目錄與格式不同，但承載的資料語意（Master 球座標、批次摘要、基板 Die 矩陣、缺陷球清單⋯⋯）大致重疊。

本文件的目的是：

1. 把「既有設備的檔案讀取流程」整理成 Agent 能對照的**規格表**。
2. 列出 Agent 在拿到新設備的範例檔案後，**必須回填的欄位**與**輸出格式**。
3. 之後我會根據 Agent 的調查結論在程式碼裡實作新格式的 Parser／Locator。

> 給 Agent：你不必修改任何程式碼，只需要產出一份 `.md` 調查報告，後續會由本 Agent（Claude）把調查結論落地。

---

## 1. 既有資料根目錄結構（AthleteSYS）

`FileLocator.cs` 把使用者選的任意子資料夾「拉回」根目錄（root = `AthleteSYS`），允許七種選擇情境（見 `FileLocator.AnalyzeSelectedFolder`）。

```
AthleteSYS/                                 ← root path
├── kbgadata/               (大小寫變體：「KBGA Data」也接受)
│   └── {PartNumber}/
│       ├── {PartNumber}.csv               ← Master 球座標
│       └── {PartNumber}.dat               ← Master Metadata（INI 格式）
├── kbgaresults/
│   └── {PartNumber}/
│       └── {LotNumber}/
│           ├── {LotNumber}.summary.csv    ← Lot 級摘要（多輪累積）
│           ├── *{SubstrateID}*afa*        ← 每基板缺陷球詳列
│           └── *{SubstrateID}*.map        ← 每基板 Die 矩陣 + 球統計
└── log/
    └── {LotNumber}.map.csv                 ← Lot 級 Die-Map（選配，可由 .map 計算取代）
```

關鍵 API：
- 解析使用者選的資料夾 → `FileLocator.AnalyzeSelectedFolder` ([FileLocator.cs:51](BgaDefectViewer/Helpers/FileLocator.cs#L51))
- 列舉 Part / Lot → `GetPartNumbers`, `GetLotNumbers`
- 找特定檔案 → `FindMasterCsv`, `FindSummaryCsv`, `FindMapCsv`, `FindAfaFile`, `FindMapFile`
- 平面結構 fallback：當沒有 `kbgadata`/`kbgaresults` 殼，只要 `{folder}/{folder}.csv` 存在，就把 folder 視為 PartNumber 資料夾

---

## 2. 既有檔案格式逐項規格

下列每個檔案都用「**檔名／位置 → 解析欄位 → 用到哪些 ViewModel／Tab**」的三段式記錄；新格式 Agent 應該**逐項對應**。

### 2.1 Master CSV — 球座標表

- **位置**：`kbgadata/{Part}/{Part}.csv`（fallback：該資料夾任意 `.csv`）
- **Parser**：[BgaDefectViewer/Parsers/MasterCsvParser.cs](BgaDefectViewer/Parsers/MasterCsvParser.cs)
- **格式**：純 CSV，每行一顆球，**無表頭**
  ```
  X,Y,Diameter
  ```
  - X / Y / Diameter：`double`（mm，stage 座標系，AlignmentCenter = (0,0)）
  - `Id` 在 parser 裡是 1-based 行號（檔案不存）
- **下游模型**：[`MasterBall`](BgaDefectViewer/Models/MasterBall.cs)（struct）
- **使用點**：`SubstrateViewer`、`OverlapInspection`、`RecurringDefect`、`MainViewModel`

### 2.2 Master DAT — INI 風 sidecar

- **位置**：與 Master CSV 同目錄、同檔名，副檔名換成 `.dat`
- **Parser**：[BgaDefectViewer/Parsers/MasterDatParser.cs](BgaDefectViewer/Parsers/MasterDatParser.cs)
- **格式**：INI 風（`Key=Value`，`;` 註解、`[Section]` 略過）。已使用 keys：

  | Key                    | 形態        | 意義                                                      |
  |------------------------|-------------|-----------------------------------------------------------|
  | `AlignmentPoint1mm`    | `x,y`       | 第一個對位 fiducial（mm）                                 |
  | `AlignmentPoint2mm`    | `x,y`       | 第二個對位 fiducial（mm）                                 |
  | `AlignmentCenterMm`    | `x,y`       | 對位中心（通常為 0,0）                                    |
  | `SubstrateSize`        | `x,y,z`     | 基板物理尺寸（mm，含厚度 z）                              |
  | `SubstrateDeviceCount` | `N,M`       | 基板上 device 行列數（N 欄 × M 列）                       |
  | `DevicePitch`          | `x,y`       | device 中心-中心間距（mm）                                |

- **解析容錯**：檔案不存在或全空回傳 `null`，呼叫端會退化成「以網格索引推估對位」。
- **下游模型**：[`MasterMetadata`](BgaDefectViewer/Models/MasterMetadata.cs)
- **使用點**：`OverlapInspection.LoadMaster(masterBalls, metadata)`

### 2.3 AFA 檔 — 每基板缺陷球詳列

- **位置**：`kbgaresults/{Part}/{Lot}/*{SubstrateID}*afa*`（fallback：`*{SubstrateID}*`，再不行回傳找不到）
- **Parser**：[BgaDefectViewer/Parsers/AfaFileParser.cs](BgaDefectViewer/Parsers/AfaFileParser.cs)
- **格式**：行式自描述檔，分隔符以 `;` 串第一段、後續資料逗號分隔。重要 token：

  | 行首                | 範例                              | 備註                                    |
  |---------------------|-----------------------------------|-----------------------------------------|
  | `StartTime;`        | `StartTime;2024/01/15 09:30:21`   | 任意字串                                |
  | `Mapfile;`          | `Mapfile;ABC123.map`              |                                         |
  | `Recipe;`           | `Recipe;XYZ_Recipe_v3`            |                                         |
  | `LotNo;`            | `LotNo;LOT001`                    |                                         |
  | `WaferID;`          | `WaferID;ABC123`                  | 即 SubstrateID                          |
  | `BallName;`         | `BallName;A1`                     |                                         |
  | `BallDiameter;`     | `BallDiameter;0.250`              | mm                                      |
  | `Balls;`            | `Balls;1156`                      |                                         |
  | `TotalBalls;`       | `TotalBalls;2304`                 |                                         |
  | `INSPECTION=N`      | `INSPECTION=1`                    | 之後的 Data;／BallData; 屬於該輪        |
  | `Data;`             | `Data;<DieIndex>,<DieCol>,<DieRow>,<WorstCode>,<WorstName>` | 每行 = 一個 NG die |
  | `BallData;`         | `BallData;<BallId>,<DefectCode>,<X>,<Y>,<Diameter>[,<Unknown>]` | 屬於最近一筆 Data; |
  | `EndTime;`          | `EndTime;2024/01/15 09:31:12`     |                                         |
  | `EndInspection;`    | `EndInspection;51.230`            | 秒                                      |

- **缺陷代碼對照**：見 [BgaDefectViewer/Models/DefectTypes.cs](BgaDefectViewer/Models/DefectTypes.cs)（2=Missing, 3=Shift, 4=Extra, 11=Bridge, 21=SD, 22=LD, 30=ETC，其他歸 E.O.；新設備若用不同代碼必須註明）。
- **語意關鍵點**：
  - **每一行 `Data;` 會建立一個新的 `InspectionResult`**（規格 v3 之後的行為，舊版本「同 Inspection 共用一筆」已淘汰）
  - `BallId == -1` 代表 Extra ball
- **下游模型**：[`AfaFile`](BgaDefectViewer/Models/AfaFile.cs) 內含 `List<InspectionResult>`（[InspectionResult.cs](BgaDefectViewer/Models/InspectionResult.cs)），每筆再含 `List<DefectBall>`（[DefectBall.cs](BgaDefectViewer/Models/DefectBall.cs)）。
- **使用點**：`SubstrateViewer.LoadAfa(...)`、`RecurringDefectCalculator.EnrichWithAfaData(...)`

### 2.4 .map 檔 — 每基板 Die 矩陣 + 球級統計

- **位置**：`kbgaresults/{Part}/{Lot}/{SubstrateID}.map`（fallback：`*{SubstrateID}.map`）
- **Parser**：[BgaDefectViewer/Parsers/SubstrateMapParser.cs](BgaDefectViewer/Parsers/SubstrateMapParser.cs)
- **格式**：每輪檢查由 `INSPECTION=N` 起頭，緊接一行統計、再來是 Die 字元矩陣。
  ```
  INSPECTION=1
  ABC123 OK=1234 MISS=0 SHIFT=2 SD=0 LD=0 ETC=0 BRIDGE=0 EXTRA=1 E.O.=0 GD=10 NGD=2 PPM=1733.10
  GGGGGGGG
  GGSGGGGG
  GGGGGGGG
  ...
  INSPECTION=2
  ...
  ```
  - 統計行使用 regex `([\w.]+?)=([\d.]+)`，**會把 `E.O.` 當成 `EO`**（去點之後上鍵入 dict）
  - Die 矩陣每字元代表一顆 die：`G` 良品、`1` 無 die（mask）、`F` 失敗、其餘大小寫為缺陷字母（規格見 [DieJudge.cs](BgaDefectViewer/Helpers/DieJudge.cs)，例：`M`/`m` Missing、`E`/`e` Extra、`S`/`s` Shift⋯⋯小寫 = 「+」混合）
  - 矩陣的 `Cols` 取**第一行字元數**，矩陣可不規則但會以 `'1'` 補齊
- **下游模型**：[`SubstrateMap`](BgaDefectViewer/Models/SubstrateMap.cs)（含 `List<MapInspection>`），其中 `MapInspection.DieGrid: char[,]`
- **使用點**：`SubstrateMap` Tab、`DieMapCalculator`（彙整 lot 級熱度圖）、`RecurringDefectCalculator`（Phase 1）

### 2.5 Lot Summary CSV — 批次摘要

- **位置**：`kbgaresults/{Part}/{Lot}/{Lot}.summary.csv`（fallback：該目錄任意 `*.summary.csv`）
- **Parser**：[BgaDefectViewer/Parsers/SummaryCsvParser.cs](BgaDefectViewer/Parsers/SummaryCsvParser.cs)
- **格式**：每次檢查整批會 append 一段 LOT section，本程式取**最後一段有資料的**：
  ```
  LOT Start,LOT001,...
  Name,OK,Miss,Shift,SD,LD,ETC,Bridge,Extra,E.O.,NGDie,GDie,PPM,Judge,Stage,Yield,DateTime
  001-ABC123,1234,0,2,0,0,0,0,1,0,2,10,1733.10,NG,1,99.83,2024/01/15 09:31
  ...
  LOT Summary SubstrateCount=25
  Total,30850,5,...
  LOT Summary End
  ```
  - 編碼支援 UTF-8 / 系統預設並去 BOM
  - 沒有 `LOT Start` 標記時整檔當作單一 Lot
  - `Name` 通常是 `{prefix}-{SubstrateId}`，後續以 `-` 切（`FileLocator.ExtractSubstrateId`）
  - 沒有 `LOT Summary` 區段時，後置以 [`LotSummaryCalculator`](BgaDefectViewer/Helpers/LotSummaryCalculator.cs) 從各 row 自行計算
- **下游模型**：[`LotSession`](BgaDefectViewer/Models/LotSession.cs)（含 `List<SummaryRow>` + `LotSummary`，分別對應 [SummaryRow.cs](BgaDefectViewer/Models/SummaryRow.cs)、[LotSummary.cs](BgaDefectViewer/Models/LotSummary.cs)）
- **使用點**：`LotMonitor` Tab

### 2.6 Map CSV — Lot 級 Die-Map（選配）

- **位置**：`log/{Lot}.map.csv`（fallback：log 目錄任何含 lotNo 的 `*.map.csv`）
- **Parser**：[BgaDefectViewer/Parsers/MapCsvParser.cs](BgaDefectViewer/Parsers/MapCsvParser.cs)
- **格式**：CSV 表頭數行 + 多個「Pre Repair / Post Repair」缺陷塊：
  ```
  Recipe No.,123
  Recipe Name,XYZ
  Lot Name,LOT001
  Pre Repair
  Missing,8, ,Missing,8
  1,0,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0
  ...
  Shift,8, ,Shift,8
  ...
  ```
  - 每塊的第一格是缺陷名、第二格是欄數；中間以**空格分隔**前/後修
  - 每筆 row 第 0 欄是 row 索引（捨棄），之後 `preCols` 個值 → 空格 → `postCols` 個值
- **下游模型**：[`DieMapData`](BgaDefectViewer/Models/DieMapData.cs)
- **取代來源**：當 `kbgaresults/{Part}/{Lot}/` 內有 `.map` 檔時，[`DieMapCalculator`](BgaDefectViewer/Helpers/DieMapCalculator.cs) 會**直接由 .map 計算**，這份 CSV 只在無 .map 時被讀取。

### 2.7 Settings JSON — 應用程式自身偏好

[BgaDefectViewer/Models/AppSettings.cs](BgaDefectViewer/Models/AppSettings.cs) 把上次選的 `AthleteSysPath` / Part / Lot 存在
`%LocalAppData%\BgaDefectViewer\settings.json`。**與設備 log 無關**，提示給 Agent 避免誤踩。

---

## 3. 載入流程（用來理解現有耦合點）

[`MainViewModel.OnLotNumberChanged`](BgaDefectViewer/ViewModels/MainViewModel.cs#L208) 是核心調度：

1. 由 PartNumber 找 Master CSV → `MasterCsvParser.Parse` → `MasterBall[]`
2. 同名 `.dat` → `MasterDatParser.Parse` → `MasterMetadata?`
3. Lot 找 Summary CSV → `SummaryCsvParser.ParseFirstLot` → `LotSession` → 餵 `LotMonitor`
4. ResultDir 內所有 `.map` → 平行 `SubstrateMapParser.Parse` → `List<SubstrateMap>` → 餵 `SubstrateMap` Tab
5. `DieMapCalculator.Calculate(...)` 從 (4) 算出 `DieMapData`；無 .map 才退化到 `MapCsvParser`
6. `RecurringDefectCalculator` 兩階段：先吃 .map（快），再背景吃 .afa 補球資料
7. 雙擊基板/die → `FileLocator.FindAfaFile` → `AfaFileParser.Parse` → `SubstrateViewer.LoadAfa`
8. Settings Tab 透過 [`SettingsViewModel.UpdateFileStatuses`](BgaDefectViewer/ViewModels/SettingsViewModel.cs) 顯示 5 個檔案的 ✓ / ✗ 狀態

> 任何新格式都必須能填入這 8 個輸出位（或在你判定不適用時明確說明），否則 UI 會缺資料。

---

## 4. Agent 的調查任務 — 要回答的問題

請針對每個新設備產出的檔案，產出以下欄位（**對齊本文件 §2 的格式**），方便後續直接寫 Parser：

### 4.1 樣本檔案盤點

- 列出全部你拿到的樣本檔案，**完整路徑**（相對於樣本根目錄）
- 副檔名、大致大小、編碼（UTF-8 / UTF-16 / Shift-JIS / 其他）
- 是否含 BOM、換行符（CRLF / LF）

### 4.2 目錄結構

- 新設備的根資料夾名稱（取代 `AthleteSYS`）
- 是否仍有 `kbgadata` / `kbgaresults` 兩層；若無，畫出實際樹狀圖
- Part Number / Lot Number / Substrate ID 在新結構中的位置與命名規則
- 是否引入新的中介資料夾（recipe / station / date 分層等）

### 4.3 對應到既有 7 類檔案的一一映射

對 §2 的每個檔案，回答：
- **新設備中對應檔案的檔名 pattern 與位置**（若不存在則註明「無」）
- **新格式相對於既有格式的差異**（多／少／改名的欄位、是否從 CSV 改成 JSON / XML / 二進位）
- **單位與座標系**：mm / µm / pixel？基準點是否仍為對位中心 (0,0)？Y 軸方向？
- **缺陷代碼**是否與 [DefectTypes](BgaDefectViewer/Models/DefectTypes.cs) 一致；不一致請列差異表
- **Die 字元集**是否與 [DieJudge](BgaDefectViewer/Helpers/DieJudge.cs) 一致；不一致請列差異表

### 4.4 新增 / 不對應的資料

- 新格式裡有但既有格式沒有的欄位 → 列出來、推測語意
- 既有格式裡有但新格式沒有的欄位 → 標出，說明影響哪個 Tab／流程

### 4.5 範例片段（必附）

每種新檔案請至少貼出 **20–30 行的代表性片段**（去識別化後），含：
- 表頭區
- 一筆完整資料 record
- 任何特殊區塊（多輪檢查、彙總、注釋）

> 注意：若樣本含客戶機敏資料（料號、批號、操作員），請以 `XXXX` 取代再貼。

### 4.6 不確定 / 需要追問的問題

列一份「我目前無法從樣本判斷的事」清單，由我（Claude）轉問使用者。例：
- 「`STAGE_LOG.bin` 是否每次測量都會 append？」
- 「`*.recipe.xml` 與 既有 `.dat` 的對位欄位是否等價？」

---

## 5. 報告交付格式建議

請以**單一 Markdown 檔**繳交，建議檔名：
```
docs/NEW_DEVICE_FORMAT_FINDINGS.md
```

骨架：

```markdown
# 新設備檔案格式調查結果

## A. 樣本盤點
| # | 路徑 | 副檔名 | 編碼 | BOM | 換行 | 行數/Bytes |

## B. 目錄結構
（樹狀圖 + Part/Lot/Substrate 對應）

## C. 檔案對映表（vs. 既有 7 類）
| 既有檔案 | 對應新檔案 | 大致差異 | 對應 §1–§7 完整規格章節 |

## D. 各新檔案完整規格
### D.1 {新檔名}
- 位置 pattern
- 欄位定義表
- 範例片段
- 與既有版本差異

## E. 新增資料 / 缺漏資料
## F. 缺陷代碼 & Die 字元差異表
## G. 待確認問題清單
## H. 實作建議（可選）
```

---

## 6. 給後續實作 Agent（Claude）的便箋

當 §5 的報告回來後，落地步驟大致是：

1. 在 `Models/` 新增對應 record 類型（或擴充既有 model 的可空欄位）
2. 在 `Parsers/` 新增 `XxxParser`，介面盡量貼齊既有 parser（`static Parse(string filePath) → Model`）
3. 在 `Helpers/FileLocator.cs` 增加新設備的目錄變體（仿 `KbgaDataNames` / `KbgaResultsNames`）；必要時擴充 `AnalyzeSelectedFolder` 的 case
4. `MainViewModel.OnLotNumberChanged` 內依「新／舊設備」分支選擇 parser；或抽出 `IFileBundle` 抽象由 detector 決定
5. `SettingsViewModel.UpdateFileStatuses` 顯示新設備的檔案狀態
6. 缺陷代碼 / Die 字元若有差異，**避免改舊表**，改成在 `DefectTypes` / `DieJudge` 加新 entry，或加一層「device profile」轉換層

> 給調查 Agent：你不必擔心這段；只要 §5 的報告寫得齊全，實作會由我（Claude）銜接。
