# P8-01c 執行清單

## 範圍與前置

- 依 08A §5 P8-01c 執行；測試組織僅限 `goodshepherdback`。
- 重新讀取 `AGENTS.md`、08A、父任務 `implement.md`、`P8-decisions.md` 與本工作樹最新 D365 證據。只允許使用 `C:\smv\guard-p8`，證據只能寫入 `C:\smv\guard-p8-evidence`。
- 修正已查明的 FileVersion 根因：Plugins 與 Workflows 都以 Release、`SpeechMessagePackagePlugin=true`、`SpeechMessageBuildNumber=20` 打包；執行 `Test-PluginAssembly.ps1`，並確認兩個產物的 FileVersion 均為 `1.0.0.20`。若任何 DLL 內容再改變，後續 build number 遞增。
- 在所有操作前確認 organization catalog 只含 `goodshepherdback`，必要 User Secrets 鍵存在但不讀取／顯示值。唯讀執行 `verify --org goodshepherdback`；由於 DLL 版本根因已修正，這不屬於未改條件的重試。

## 實機步驟

1. 核對起點 `verify=0`、D34、`SMV-TEST-` 數量及操作所需設定；任一不符時依 08A 的回復流程先恢復到安全基線。
2. 唯讀盤點會受回填影響的收費單及預約 ID／教會 ID，存入 08A 指定的 `artifacts/p8/offering-backup-<時間>.json`；不得存姓名或其他個資。
3. 執行結構 plan，確認差異只有 `new_fee.new_offeringcategoryid` 與 `new_dedication_booking.new_offeringcategoryid`；完成 apply、verify=0。依結果記錄是否需要發布。
4. 如組織稽核原先未開啟，依 D8-17 開啟並讀回確認。
5. 產生 migration plan，逐段核對教會序號、類別／回填筆數、無法判定筆數、未對應標籤及兩個舊欄 RequiredLevel。確認名稱按 08A 定案以 metadata label 為準，不合併不同選項值。
6. 驗證 plan hash 與來源快照後 apply；執行 `--verify`，確認每間教會缺 lookup 數為 0、跨教會 lookup 數為 0；再以同一 plan apply，確認新增／回填寫入數皆為 0。
7. 執行 `OfferingCategories_Live_ListAndLookupResolve`，只使用測試教會及已回填資料，並核對呈現文字與 migration verify 結果一致。
8. 設定組織為 `off`，唯讀 verify=0，確認 D34 有效、舊步驟／流程啟用數 0、`SMV-TEST-` 及本次測試流程殘留 0；回復非預期的追蹤及待辦變更。

## 交付與驗收

- 寫繁體中文檔案說明與 `wp-reports/P8-01c-migration.md`，以序號呈現教會，不記教會名稱、姓名、秘密或來源記錄 ID；描述資料量、無法判定量、未對應標籤、RequiredLevel、實際結構與 Live 結果及可回復方式。
- 將「教會沒有奉獻類別時，ChurchReport 拒絕建立奉獻」列入 `P8-上線前置清單.md`：部署新版 ChurchReport 前必須完成類別轉換及必要回填。
- 以原 P8-01b 測試證明 behavior；若 RequiredLevel 實況推翻 P8-01b 規則，先寫 failing test，再以 `P8-01 fix:` 提交修正。
- 更新父任務、P8-01c 一頁回報與進度頁 DATA；不得修改進度頁規劃文字或 08A 以外計畫文件。
- 執行單元要求的測試、完整守門、提交、回報、驗收；在唯一 `C:\smv\guard-p8` checkout 該提交 SHA，完整守門證據只存 `C:\smv\guard-p8-evidence`。
- 推送 P8 分支並以 `git ls-remote` 核對遠端 SHA。

## 2026-11-20 續跑紀錄

- 10:32 Asia/Taipei：以 `Test-PluginAssembly.ps1` 檢查目前 Plugins／Workflows 產物，兩者均回 `ok=true`；唯讀 `verify --org goodshepherdback` 結束碼 0，輸出狀態符合 catalog 預期。此為 build 20 根因修正後的新操作，不是相同條件重試。
- 10:36 Asia/Taipei：`export-legacy --org goodshepherdback` 唯讀結果為啟用舊步驟 0、舊流程 0。
- 10:42 Asia/Taipei：migration plan 在讀業務資料前因共用 option value 的 metadata labels 衝突而 fail closed。未以相同條件重試，未執行結構或資料 apply。
- 10:42 Asia/Taipei：新增 `ReadBackupRecords_ConflictingOptionLabels_DoesNotRequireCategoryMapping`，確認紅燈為標籤合併例外；backup-only 路徑略過類別映射合併後綠燈。ID-only 備份建立 97 筆，僅含兩個來源實體的 record ID 與 church ID。
- 10:45 Asia/Taipei：Deployment 全部 Release 測試 1,029/1,029 通過。`OfferingCategories_Live_ListAndLookupResolve` 未執行，因其前置 lookup 回填未完成。P8-01c 保持 `in_progress`；完整缺項見父任務 `wp-reports/P8-01c-migration.md`。
- 不宣稱本次結束時 D34、`SMV-TEST-` 殘留、lookup verify 或 off 清理均已完成；前次 `live-check` 在 `LIVE-PREP-06` 寫入前拒絕。
- 11:12 Asia/Taipei：唯讀 `export-metadata --org goodshepherdback` 成功；snapshot 格式僅存選項值與 RequiredLevel，不含 labels。語系 1028；new_fee 24 值、new_dedication_booking 17 值，共享 17 值；RequiredLevel 均為 None。snapshot SHA-256 `E0F4498B97A70A2D33174AD1ADD77903115667DAC2E192F63ECC89DC63060A8B`。
- 11:18 Asia/Taipei：同值異標籤與 D8-6 固定 `legacy-<值>` 的資料身份無法同時表達；未變更已核定代碼、未猜 labels、未 apply。登錄 P8-PROP-008，P8-01c 維持進行中並依 D8-26 接續後續單元。
- 12:34 Asia/Taipei：新會話核對 User Secrets 必要鍵存在及 endpoint 符合 goodshepherdback allowlist 後，唯讀 `verify --org goodshepherdback` 成功，狀態符合 off；安全摘要存於 `C:\smv\guard-p8-evidence\p8-01c-resume-verify-20261120.log`。沒有組織寫入；D34 兩次同條件失敗與 SMV-TEST 數未知仍阻止實機遷移收尾，不重試相同 D34 操作，P8-01c 維持進行中。
