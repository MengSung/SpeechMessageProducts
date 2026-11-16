# P6-06 舊套件評估

依 06A §5 P6-06，時間盒 2 小時，2026-11-16 18:49 Asia/Taipei (UTC+08:00) 開始。
產品無執行行為變更，僅刪除已停用的九個套件註解並加入架構防回歸規則。

## 指定測試與實作

- [x] `SyntheticHost_AspNetCore2xPackage_IsReported`：先紅後綠；九個精確套件 ID 的直接參考及已還原相依須回報 `LegacyAspNetCorePackage`。
- [x] `ChurchReport_ResolvedPackages_ExcludeAspNetCore2xPackageIds`：Debug、Release 各一次，真實方案 ChurchReport 不得違規。
- [x] 如果真實傳遞相依含禁用 ID，不移除／升級上層套件；依 06A 改只查直接套件並記明原因。
- [x] csproj 只移除「.NET 10 使用框架引用」與九個被註解的套件參考。
- [x] WebApi.Client 五種 API 使用次數只盤點、登錄之後再說，不改程式或套件。

## 五項完成定義

1. 指定測試全部存在並通過；ChurchReport Release 建置及 Hosting 架構測試通過。
2. 程式提交後固定 `C:\smv\guard` 完整 00 §8.1，基準 `9b7ac0a0`。
3. 無新專案，覆蓋率不適用。
4. 本包無實機，不連 D365。
5. 一頁回報、進度 DATA 與驗收時間／提交齊全；驗收後第一次推送 P6，ls-remote 核對。

提交以 `P6-06` 開頭，不自動推送、不改 NoWarn、不改 duplicate-baseline.json。
