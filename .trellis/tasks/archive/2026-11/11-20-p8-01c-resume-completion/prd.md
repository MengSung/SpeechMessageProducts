# P8-01c 奉獻類別遷移續跑與驗收

## 目標與範圍

依使用者 2026-11-20 的最新指示，先完成 P8-02，再回到 P8-01c 並依 `08A_階段8_執行細則.md` §5 的原定次序執行。本任務取代 2026-11-20 15:52 將 P8-01c／P8-01 標為縮減驗收的結論；舊報告只保留為歷史證據，不作本次完成證明。

本任務只對核准測試組織 `goodshepherdback` 執行必要操作，不接觸正式組織。秘密只從 User Secrets 或環境變數供既有工具讀取，不顯示、不寫入命令列、repo、紀錄、回報或截圖。

## 必須完成的工作

- 重新打包 Plugins 與 Workflows，使用 `SpeechMessagePackagePlugin=true`、`SpeechMessageBuildNumber=20`；兩個 DLL 都通過 `Test-PluginAssembly.ps1` 後才執行部署工具。
- 起點唯讀核對 `verify=0`、D34 有效、`SMV-TEST-` 殘留 0；建立只含來源實體、記錄 ID、教會 ID 的唯讀備份。
- 核對並完成兩個奉獻查閱欄的結構 plan/apply/verify；差異只限 `new_fee.new_offeringcategoryid` 與 `new_dedication_booking.new_offeringcategoryid`。讀回並保留組織稽核狀態。
- 執行奉獻 migration plan，逐段核對類別數、回填筆數、無法判定筆數、未對應標籤及兩欄 RequiredLevel；依已核定 metadata 規則產生可安全驗收的 plan，再 apply、`--verify`，並以相同 plan 第二次 apply 證明零寫入冪等性。
- 執行 `OfferingCategories_Live_ListAndLookupResolve`，並以組織操作紀錄證明本單元結尾回到 `off`、`verify=0`、舊步驟／流程啟用數為 0、`SMV-TEST-` 與測試流程殘留為 0。
- 若 `RequiredLevel` 實測推翻 P8-01b 寫入規則，先新增指定失敗測試，再以 `P8-01 fix:` 提交修正；目前已知兩欄為 `None`，不可把歷史假設當成新觀測。
- 更新 `P8-01c-migration.md` 取代原阻擋紀錄，更新上線前置清單，明確列出 ChurchReport 在類別轉換及必要回填前不得部署，以及教會沒有類別時拒絕新增奉獻的前置處理。
- 更新階段父任務與進度頁 DATA；不修改任何計畫文件或進度頁規劃文字。

## 安全與資料正確性限制

- 僅以實際 metadata 標籤及已核定的固定代碼規則產生 plan；不按語意猜標籤、不合併不同選項值、不覆寫同名不同 ID 的資料。
- 若來源 metadata 使同一教會／代碼無法唯一表示兩個不同標籤，plan 必須 fail closed；不可為達成 apply 而任選名稱或放寬驗證。記錄精確衝突與證據，依階段核准的決策規則繼續其餘單元。
- 每一個修改動作前均需有可驗證備份或現況 plan；任何失敗都不可回報成功。結尾的 off、verify 與殘留盤點是本任務必要驗收條件，不能挪到後續單元。
- 僅重用 `C:\\smv\\guard-p8`；守門及實機證據只放 `C:\\smv\\guard-p8-evidence`。不得建立其他 guard clone 或工作樹。

## 驗收條件

- [ ] 起點證據證明 `goodshepherdback` 的 verify=0、D34 有效及測試殘留 0；證據不含秘密或個資。
- [ ] 兩個 build 20 DLL 通過組件檢查；兩個 lookup 欄結構差異、apply 與 verify 均有證據。
- [ ] migration plan 逐段覆核完成；可以安全轉換時，`--apply`、`--verify` 及第二次零寫入 apply 均通過。
- [ ] 指定 P8Live 測試通過；或因核准資料契約衝突無法安全執行時，精確記錄縮減範圍、原因及提案，不冒稱測試通過。
- [ ] 組織結尾狀態符合 off、verify=0、啟用舊步驟／流程為 0、測試殘留為 0。
- [ ] P8-01c 一頁回報、P8-01 上線前置、父任務與進度頁 DATA 均準確；P8-01 整包只有在 a、b、c 結果完整後才標記。
- [ ] 依 00 §8.1 在唯一 guard 對本單元提交 SHA 完成完整守門，推送 P8 分支並用 `git ls-remote` 核對遠端 SHA。
