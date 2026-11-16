# P6-03 執行清單

依固定順序執行，不與其他工作包平行。

1. 讀取並比對四個相容殼、其 owner 與既有專用測試；保存 P2-06 兩份黃金檔原始 SHA-256。
2. 暫時改名四個殼型別，依序建置 ChurchReport、ChurchReport.MemberInfo.Tests 與 ToolUtility.Dataverse.Tests；保存所有編譯錯誤後還原暫改。按編譯結果記錄正式／測試呼叫者，再核對 `typeof`、`nameof`、字串型別名及 DI 註冊。
3. 建立 `ChurchReportLineNotifierTests` 與黃金測試指定案例，先執行並確認預期紅燈；再完成通知器與呼叫點轉接，逐案確認綠燈。
4. 僅在編譯及反射核對證明無正式用途時移除 `ReplyUtility`、`LineUtilityClass` 對應欄位／類別；建構式參數簽章依 06A 保留。檢查 `ChurchReportLegacyRichMenuCatalog` 使用者後依計畫保留或刪除。
5. 改寫 P2-06 黃金重播案例；明確分類每案為重播或退役，保留來源檔位元組。刪除只驗證四個殼的測試；如其中仍有共用流程案例，移入共用流程自己的測試專案。
6. 確認四個殼型別不存在，且指定十個測試名稱逐字存在並通過。執行 06A 指定的五組 Release 測試、I18nCheck verify、重複程式檢查。
7. 檢查修改檔案的繁中檔首說明、型別／方法 XML 文件、UTF-8 無 BOM、CRLF 與末尾 CRLF。提交單一 P6-03 程式提交並在固定 guard 跑完整 00 §8.1。
8. 建立一頁 `wp-reports/P6-03.md`，更新進度 DATA 與 Trellis／CCG 任務狀態，執行適用的輕量文件檢查；五項完成定義成立後才標 done、推送並以 `git ls-remote` 核對。

## 固定測試名稱

- `P206Golden_File_Sha256Unchanged`
- `Notifier_TextBestEffort_SendsUserTextWithLegacySourceTag`
- `Notifier_MessagesBestEffort_SendsSdkMessagesWithLegacySourceTag`
- `Notifier_BestEffortFailure_ReportsAndDoesNotThrow`
- `Notifier_Reliable_PassesRetryKeyAndThrowsOnFailure`
- `Notifier_Multicast_WritesStatisticsThenSendsOnce`
- `Notifier_TwoChannelsInterleaved_NoCrossChannelSend`
- `P206Golden_KeptCases_ReplayedThroughNewEntry_ByteIdentical`
- `P206Golden_EveryCase_IsEitherReplayedOrRetiredWithReason`
- `Source_NoObsoleteSendShellTypes`

## 修訂1核定

- 新增指定測試 `Notifier_WrongConstructionForMethod_FailsFast`：模式誤用必須在try之前拒絕，四方法與零副作用同案驗證。
- DevelopmentSafetyTests清冊11→10與方法註解，其他斷言不變（D6-19）。
- 06A修訂1僅原樣納入版控，不修改計畫內容。
