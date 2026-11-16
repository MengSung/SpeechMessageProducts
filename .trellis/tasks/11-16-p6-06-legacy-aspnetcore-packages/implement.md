# P6-06 執行證據

2026-11-16 18:57 Asia/Taipei (UTC+08:00)：九個合成反例先紅（缺規則）；
實作後架構集合 82 案中的 81 案通過，唯一失敗是本工作樹缺 Plugin Release DLL，
訊息明確要求建置產物。補建既有 Plugin 專案後原失敗案例 1 案通過。
不改測試期望、不加回 Debug 補建、不改 NoWarn；完整方案結果以提交後 guard 為準。

- `SyntheticHost_AspNetCore2xPackage_IsReported`：9 案已由紅轉綠。
- `ChurchReport_ResolvedPackages_ExcludeAspNetCore2xPackageIds`：Debug、Release 2 案綠；
  真實 ChurchReport 已還原相依沒有禁用 ID，保留同時檢查直接／傳遞套件的完整規則。
- ChurchReport Release 建置：0 警告、0 錯誤。
- WebApi.Client 五種 API：各 0 處；只登錄後續事項，套件未移除。
- 進度頁首次 active 紀錄漏 `dod`，由既有契約測試拒絕；依 renderer 原 schema 補齊五項後 28 案全綠，只改 DATA。
- 編碼首次 24 檔、0 問題；最終提交與守門另留原始紀錄。

原始輸出留 `artifacts/p6/p6-06-*.log`，不進版控。
下一步：程式提交 → 固定 guard 完整八項 → 一頁回報／驗收／推送核對。
