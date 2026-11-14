# P5-02b 編譯矩陣研究

## 文件目的與範圍

本文件供 P5-02b 實作決定舊專案編譯矩陣的輸入清單、可執行範圍、安全降級及輸出契約。內容依 2026-11-14 工作樹中的 05A 階段細則、已保存的唯讀舊方案盤點、PluginHarness reader/compiler 與 FakeOrganizationService 原始碼整理；不代表本次重新成功編譯舊方案或執行實機。本文是研究紀錄，不是舊行為黃金資料。

## 核心結論

- 05A §1 F11、§2 D5-3、§5 P5-02/P5-13 是計畫權威：範圍是 36 個第一層舊 `.csproj`；其中 29 個舊入口（11 個 `IPlugin`、18 個 `CodeActivity`）逐專案編譯、只編譯不執行。P5-02b 把每個入口所在專案的編譯結果寫入 `research/legacy-compile-matrix.md`。
- 現有 `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/old-project-matrix.json` 有 37 列，並含 `Dynamics365Line/Dynamics365Line.csproj`；其先前 markdown 記錄的 37 是不同盤點口徑，不能因此把 shell 算成 37 個計畫專案，也不能靜默刪除舊證據。該 shell 的專案目錄在盤點記錄中沒有任何 `.cs`，不會生成 plugin/activity，應明確標為 `excluded-shell-no-source`，不納入 36/29 編譯矩陣成功數。
- `BoundedSourceReader.ReadProjects` 是目錄探索器，不是 36/29 的權威白名單：它會挑第一層有 `.csproj` 的所有資料夾，因而會把 Dynamics365Line shell 也回傳；P5-02b 應由核定舊方案盤點中的 36 個專案名（排除 shell）和 05A §5 明列的 29 個入口名建立預期集合，再將 Reader 輸出做精確集合比對。
- 29 個入口不能從 36 個專案資料夾名稱推定。入口型別權威來源為 05A §5 各工作包的「舊入口」宣告及 §1 F11 的 11/18 分類；編譯期實際入口型別由 Roslyn 語意模型確認。結果必須保留「入口專案」與「其 ProjectReference 閉包」差異；不可把每個相依專案都當成獨立入口，也不可把同名型別副本混進同一 compilation。

## 權威清單及現有盤點差異

權威專案數是 05A 的 36，不是現有 matrix JSON 的 37。現存舊盤點 JSON/Markdown 的 37 個名稱如下；就矩陣預期集合而言，排除 `Dynamics365Line` 後其餘 36 名即為現有研究底稿：

`AllotPlugIn`, `AllotTlsWorkflow`, `AllotWorkflow`, `AppointmentPlugIn`, `AppointmentSigning`, `AppointmentSigningLibrary`, `AppointmentWorkflow`, `CalculateQrCodeScanNumberWorkflow`, `DiscipleLessonsPluIn`, `Dynamics365Line`, `EquipmentWorkflow`, `ExampleWorkflow`, `FeePushLineWorkflow`, `HappyBirthdayWorkflow`, `Line.Messaging`, `LineMessagePlugIn`, `LineNotifyLibrary`, `LineUtility`, `NewPersonCareWorkflow`, `NewPersonCareWorkflow-ForDebug`, `PersonalQrCodeWorkflow`, `PresentRecordLinePlugIn`, `QrCodeWorkflow`, `RecursiveWorkflow`, `StorLessonSigningLibrary`, `StorLessonSigningWorkflow`, `StorLessonSignningPlugIn`, `SundayQrCodeWorkflow`, `TemplatePlugIn`, `ToolUtility`, `Trace`, `TwoWingsPlugIn`, `WeeklyReportLinePlugIn`, `WeeklyReportPlugIn`, `WeeklyReportWorkflow`, `WorkflowTemplate`, `YangMeillcStorLessonsPlugIn`.

計畫核定的 29 是「入口型別」數，不是 29 個資料夾。入口專案會有共用函式庫及其他非入口專案；入口名/型別應以 05A §5 對入口的逐項宣告維持明確 manifest，對照 Roslyn 編譯出的 `EntryType`。目前 classifier 的 `EntryType` 僅回傳入口專案中第一個符合條件的 public concrete IPlugin/CodeActivity，不能自行證明 29 個全數已被列出、每專案恰有一個入口，或列出的型別必定是計畫指定型別。

## 目前可用產物

- `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/old-project-matrix.json`：37 筆唯讀盤點列，欄位為 `name`、`project`、`sourceFiles`、`networkIndicator`。其 `sourceFiles` 是舊盤點自己的統計，不等同 bounded reader 的 Compile closure 檔數；不得當作編譯成功證據。
- `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/old-project-matrix.md`：同一盤點的可讀表格及當時來源路徑、時間與安全聲明。保留其歷史數值，不覆寫成新結果。
- `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/attendance-scenarios.md`：PresentRecordLinePlugIn 的入口、觸發路徑、必要資料、預測效果與安全限制；明確指出它是靜態閱讀，不是 legacy 實跑。
- `tools/LegacyGolden/PluginHarness/SourceReader.cs`：bounded reader，逐專案解析 XML Compile/ProjectReference closure。
- `tools/LegacyGolden/PluginHarness/LegacyProjectCompiler.cs`：Roslyn 編譯、EntryType 辨識及固定診斷。
- `tools/LegacyGolden/PluginHarness/Classification.cs` / `Models.cs`：單專案結果分類模型。現有列舉狀態是 Generated、CompileFailed、BlockedByNetworkApi。
- `tools/LegacyGolden/PluginHarness/Test-PluginHarness.ps1` 與現有 `Program`/runner：目前自動產物是原型報告與三筆 synthetic attendance；它不是 P5-02b 的完整 36/29 編譯矩陣。

## Bounded reader 的限制與矩陣處理

`SourceReader.cs:90-116` 限第一層 256 個目錄；從有 `.csproj` 的目錄呼叫 `ReadProject`。它不排除「沒有 C# Compile source 的入口殼」，因此 shell 必須由矩陣層明確辨識並排除，而不應靠 reader 例外或假造入口。`ReadProject` 每次只接受一個專案資料夾內唯一 `.csproj`（約 138-160 行），解析來源時僅收集 XML 中明列的 `<Compile Include>` 與 `<ProjectReference>`，不執行 MSBuild、Import、Target 或 glob；Condition、`* ? $ ; %` 等不支援形式會讓 closure 失敗（約 197-235 行）。

封閉的上限是每個 closure 64 個專案、512 個 `.cs`、單檔 1 MiB、總來源 16 MiB；會排除 `Properties` 與 `AssemblyInfo.cs`，嚴格解碼 UTF-8/UTF-16 並防越界、reparse point 及禁止目錄（約 90-96、197-235 行及後續路徑/解碼 helper）。因此矩陣的失敗有兩類必須分開呈現：reader/輸入閉包拒絕（固定 read issue）與 Roslyn emit 編譯失敗。不可把 reader 的簡化 XML 支援範圍描述成完整 MSBuild 等價。

現行 `LegacyProjectCompiler.CompileInput` 只在 Roslyn 編譯與改寫成功後回 `Generated`，`CompileFailed` 使用安全診斷代碼/來源 basename；語意改寫識別到拒絕的不可攔截網路 API 時回 `BlockedByNetworkApi`。`Generated` 只表示可編譯，不是可安全執行或 golden 已實跑。P5-02b matrix 應只編譯 29 個明列入口專案，其他 7 個計畫專案可在專案 disposition 清單中標為 library/non-entry，不必錯誤當成入口逐一執行。

## FakeOrganizationService 與 PresentRecordLinePlugIn 查詢缺口

Fake 實作位置 `SpeechMessage.Dynamics.Plugins.Tests/Fakes/FakeOrganizationService.cs`：

- 建構子接受 `IReadOnlyDictionary<int, EntityCollection> queryResults`（約 37-44 行）；key 是從 1 起算的 `RetrieveMultiple` 呼叫序號，不是 QueryExpression/Fetched XML 的內容鍵，也不是「某類查詢」自動比對。
- `SetQueryResult` 有 1 起算索引、最多 256 筆覆蓋，並複製回應（約 71-80 行）。`RetrieveMultiple` 優先序為 handler、當前呼叫序號覆蓋、內建 Query 引擎（約 93-102 行）。
- 未命中覆蓋時只走 Fake 已實作的有限查詢語法；遇到不支援 QueryBase/條件/連結/XML 結構應具名丟 `NotSupportedException`，不以空集合假裝查詢成功。測試含未知 query type 的序號覆蓋與未覆蓋拒絕（`FakeOrganizationServiceTests.cs:511-526`）。
- 對 PresentRecordLinePlugIn，已有靜態盤點 `attendance-scenarios.md` 記錄其執行會經 `ToolUtilityClass.RetrieveEntity` 呼叫 `Retrieve(new_present_record, id, ColumnSet(true))`，再 `Retrieve(contact, id, ColumnSet(true))`；這兩者是 Retrieve，不受 `queryResults` 控制。若實際舊入口閉包執行時另發出 `RetrieveMultiple`，必須依呼叫序號逐筆提供 queryResults，並以真實捕捉到的呼叫順序對照，不能依猜測設答案。
- 目前可直接確認的限制不是「Fake 對某一特定 PresentRecord 查詢必定缺功能」；現有靜態盤點指出該路徑未見 `new_orgrecord` 查詢，且本次可讀工作樹沒有舊方案根目錄在當前預期路徑以外的額外來源可供重新驗證。具體缺口是 queryResults 只按序號覆蓋、無 query predicate matching/具名 alias、無法自行保證場景查詢順序穩定；未被覆蓋且不在既有 Query 子集的語法會拒絕。P5-02b 應透過實跑記錄查詢型別/安全摘要及次序，逐一為實際未知查詢設覆蓋，或為高重用且語義明確的舊查詢增加 Fake 支援與測試，不能用全域空結果 callback。

目前研究材料 `attendance-scenarios.md` 的具體 gap/攔截風險還包括：lookup `EntityReference.Name` 必須合成提供；contact 必須有 `new_lineid`；Target 的 Retrieve 後狀態與 PreImage 需分開一致供應；Line Utility 發送為未 await Task，runner 必須排空已攔截呼叫。這些是執行輸入與 async drain 要求，不是 queryResults 本身的能力。

## 建議命令與輸出契約

建議沿用現有 CLI 入口，為矩陣增加唯讀 compile-only mode（例如 `compile-matrix --legacy-root <absolute-root> --out <path>`），不要讓此命令經過舊入口執行流程。根路徑採既有核定位置：

`D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.其他解決方案\Dynamics365Line`

命令必須先比對精確專案白名單（36 名），再排除已辨識的 `Dynamics365Line` 純 shell；對 36 名逐一確認唯一 csproj，並只對 29 名入口專案呼叫 bounded reader/compiler。每個入口各自建立獨立 compilation，ProjectReference 依 csproj closure 收入；不將所有舊專案 sources 合併成一個 compilation。逐列輸出 `project`、`projectFile`、`entrypointExpected`、`entryTypeActual`、`sourceFileCount`、`sourceByteCount`、`sourceSha256`、`status`、`diagnostics`。矩陣頂層記錄 schema/version、UTC run time、source root 的非機密標識、expected counts（36 projects / 29 entrypoints / 11 plugin / 18 activity）、observed counts、excluded shell 與排除理由。

狀態至少區分 `Generated`、`CompileFailed`、`BlockedByNetworkApi`、`SourceClosureReadFailed`、`EntryTypeMismatch`、`ExcludedNonEntry`、`ExcludedShellNoSource`；錯誤診斷只使用固定代碼和 basename，不寫原文、絕對路徑、token、編譯器診斷訊息或來源內容。跑完集合必須精確驗證：36 個 project records（含明確 shell disposition）、29 個 entrypoint records，並對 11/18 類型計數；任何缺漏、重複、未知項目或型別不匹配使命令非零退出，但仍可原子發布完整失敗矩陣供 D5-3 分流，不得發布部分成功假象。Markdown 報表適合閱讀，機器可驗證 JSON 可作為其來源；既有 `old-project-matrix.*` 保留為靜態盤點證據，不覆寫。

## 參考位置

- `文件/規劃設計書/實作計畫-3.5/05A_階段5_執行細則.md:39`：36/29 數量與舊方案基準；`:85` 舊方案邊界；`:343` P5-02a/b 範圍與時間盒；`:364` PresentRecord 實跑、29 入口 compile-only matrix；`:572-581` 36 專案 disposition 要求。
- `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/old-project-matrix.md`：既存 37 列、含 shell 的歷史盤點。
- `.trellis/tasks/11-14-11-14-p5-02-legacy-golden/research/attendance-scenarios.md`：入口資料流與實跑要求。
- `tools/LegacyGolden/PluginHarness/SourceReader.cs:90-116,138-235`：枚舉、project 唯一性、bounded XML closure。
- `tools/LegacyGolden/PluginHarness/LegacyProjectCompiler.cs:120-160,170-204`：安全編譯狀態及 entry type 只取第一個匹配項。
- `tools/LegacyGolden/PluginHarness/Classification.cs:15-21`、`Models.cs:18-60`：目前分類結果。
- `SpeechMessage.Dynamics.Plugins.Tests/Fakes/FakeOrganizationService.cs:37-80,93-102`：queryResults contract 與優先序。
- `SpeechMessage.Dynamics.Plugins.Tests/Fakes/FakeOrganizationServiceTests.cs:511-526`：覆蓋與未知查詢拒絕測試。
