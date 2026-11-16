# P6-02 週報計算規則與旗標共用

2026-11-16 19:37 Asia/Taipei (UTC+08:00) 開始，時間盒半天。
依 06A §4.1、§5 P6-02；P6-01 已驗收並推送 e7f92bd82799ae0981100839ad55296f7e9f56c2，
ls-remote 與本機相同；P6-04 仍待核定且變更在 stash，本包離線、不套用它。

## 指定測試與完成條件

- [ ] `IsCountedClassification_ExcludedAndMissingValues_NotCounted`：雙目標；
  null 與三個排除值為 false，階段 5 scenarios 中其他分類全部 true。
- [ ] `CalculationPrefix_IsLegacyFlagText`：雙目標，與原旗標逐字相同。
- [ ] `SavedFlag_Source_UsesSharedCalculationPrefix`：以語法樹確認全部產品 C# 無旗標字面值，
  三個工具均呼叫共用 accessor，註解不算。
- [ ] `WeeklyReportProcessor_Source_NoLocalClassificationRule`：語法樹無三值比較副本。
- [ ] 新增共用純 `IsCountedClassification(int?)`，Evaluate 委派；舊黃金期望檔完全不變。
- [ ] 六處正式旗標改用 CalculationPrefix；註解不改，字面值輸出不變。
- [ ] 逐一暫時改名三個舊分類方法，以全方案編譯錯誤判斷呼叫者。
  無呼叫刪除；若有則保留既有副作用而委派純規則。不得靠搜尋找呼叫者。
- [ ] 不改 CreateWeeklyReportAndPresentRecord、SetupWeeklyReportRecord 及建立流程。
  ConvertIndexToClearIdentity 已讀到仍在 SetupPresentRecordEntityAttributes 使用，必須保留。
- [ ] README 及受影響檔首同步；UTF-8 無 BOM、CRLF、末尾 CRLF。
- [ ] Core 雙目標、Plugins、ChurchReport 既有測試通過，兩個沙箱 DLL 打包與 PE 檢查通過。
- [ ] 程式提交後固定 guard 完整八項、五項完成定義、一頁回報、DATA、封存、推送及遠端核對。

不新增 csproj、不改期望黃金、不改 NoWarn／duplicate-baseline、不連 D365，
不切換到整批 WeeklyReportGenerationService、不改任何畫面／D365／LINE 文字或值。
