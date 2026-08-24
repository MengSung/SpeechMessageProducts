ROLE: reviewer

請審查目前工作樹中 F2 變更，範圍只包括：
- SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
- ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs

需求契約：
1. 無 HttpContext/Session 時，TryGetSessionCacheKey 必須回傳 false，且六個授權 getter（ListManager、SmallGroupDataList、WeeklyReportData、NewPersonModel、PersonalInfomationModel、HappyGroupDataManager）不得讀寫 IMemoryCache，只能回傳目前 Scoped context 的既有 m_XXX 後備欄位。
2. 有 Session 時，既有 SessionId、bound user、fingerprint、SessionCreatedTime key 組成與 cache 行為不得改變。
3. 若保留 GetCurrentSessionId，無 Session 必須是固定 NOSESSION，不得再含 Ticks；不得設定 IMemoryCache SizeLimit，也不得擴大修改其他七個 legacy getter。
4. 測試必須在無 HttpContext 下重複存取 ListManager 1,000 次，證明 cache 項目數不增加；測試替身不可引入 CRM/背景資源洩漏。
5. 遵守跨使用者隔離、Scoped 生命週期、確定性 Dispose、繁體中文文件註解、UTF-8 無 BOM、全 CRLF、末尾 CRLF。

請檢查：正確性、Session/cross-user isolation、cache retention、resource ownership、測試是否真能抓住原始 bug、nullable/編譯問題、scope 是否超出需求。
輸出 Critical/Warning/Info 分級報告；每個 finding 請附檔案與行號及可驗證理由。若沒有問題，仍請列出已核對的契約。
