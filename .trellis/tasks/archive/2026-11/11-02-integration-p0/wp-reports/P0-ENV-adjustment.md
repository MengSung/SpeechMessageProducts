# P0-ENV 調整　核定 Development 帳密存放與完整服務網址回歸

## 範圍
- 修改：DevelopmentOrganizationSettingsTests.cs — 原空白斷言改為三鍵存在；保留其他五包防護，新增完整服務網址與 User Secrets provider 順序回歸。
- 修改：3.3 的 01 P0-ENV、development-configuration-safety.md — 記錄使用者 2026-11-03 核定；不擴及 Deployment。
- 修改：task.json、implement.md 與 3.3 進度頁 DATA — 補 P0-11 實際提交與本次結果。
- 未改：01f3c03e、appsettings.json、appsettings.Development.json、ToolUtility、3.2 凍結副本、已完成歷史回報與設計文件。

## TDD 紀錄
- 原五方法中的帳密案例由 Development_Credentials_AreUnavailableUntilUserSecretsOverrides 改名 Development_Credentials_KeysExistWithoutRestrictingStorage；依核定調整其唯一被放寬的契約，不刪其他案例或斷言。
- 新增兩案例：Development_CrmServerUrl_UsesHttpsAndFullOrganizationServicePath、Development_UserSecrets_OverrideDevelopmentCredentialKeys。
- 紅燈：舊空白斷言 1 失敗、其餘 6 通過；只記錄布林斷言，見 wp-evidence/P0-ENV-adjustment/red-credential-contract.txt。
- 回歸紅燈：完整網址原已由使用者提交修正，故在新測試建立後，以舊首頁網址做可還原故障注入；路徑斷言 1／1 失敗。finally 將原設定逐位元組恢復，不回退使用者提交；見 red-service-url-regression.txt。
- 綠燈：設定案例 7／7，加既有安全案例為 32／32；原有 provider 順序未改，真實 AddUserSecrets provider 讀獨立假 secrets 後覆寫 JSON 假值。
- 重構：fixture 寫檔前先把帳密換為假值；建構中途失敗也清除專有目錄，正常先 Dispose 組態根再刪檔。
- 黃金檔變更：無。

## 驗證
- 00 §8 六項全部通過，完整命令與結束碼見 wp-evidence/P0-ENV-adjustment/validation.json。
- Release 建置：0 錯誤、76 個既有警告，未新增抑制；原門檻不超過 77。
- Release：971／971；ChurchReport 503／503；Debug tracer 7／7。
- Changed 編碼：382 檔、零問題；重複掃描 24 專案、865 檔，groups／new／stale 全零；I18nCheck verify 通過。
- 進度頁 3／3；本提交全部文字檔逐位元組 UTF-8 無 BOM、CRLF、檔尾 CRLF 與無替代字元檢查通過；git diff --check 通過。
- 覆蓋率：本包不新增產品專案，不適用新模組門檻。

## AGENTS.md 檢查
- 新增及修改的測試、fixture 與生命週期均補繁中 XML 文件。
- 真實帳密不比對、不輸出、不複製到測試暫存檔；只有實際鍵的存在與非機密網址受檢查。
- 唯一假 UserSecretsId 與可回收動態組件不共享使用者狀態；無靜態快取、watcher、Host 或連線。
- 組態與假檔由單案擁有；所有刪除先核對絕對根與專有識別碼。
- 例外通知：本包不新增產品 catch 或通知入口；測試只建立組態與假檔。

## 風險與待決
- 使用者已核定 ChurchReport 設定檔可存帳密；本次例外不包含 P0-12，Deployment 仍只讀 User Secrets。
- 基底設定維持原計畫在 P2／P4 處理；本回報不記錄任何帳密值。
- goodshepherdback 清理尚未回報，所有 D365／LINE 連線仍禁止。
- cross-user-isolation-and-performance.md 缺檔仍依使用者指示不搬移，照 AGENTS.md 條文執行。
- 外部審查依核定留到 P0-12 後；本調整只做本機驗證。

## 需要使用者做的事
- 無；本調整提交後接續 P0-12 離線實作。
