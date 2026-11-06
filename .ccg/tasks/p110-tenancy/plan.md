# P1-10實作與整合
1. 完整讀active來源並列差異與新增副本；以合成configuration鎖定舊優先序。保留public建構式與環境provider順序，不載入真憑證。
2. 建立strict net10產品/測試與可執行空殼；先跑resolver、settings、scope及owner測試真紅。
3. 完成不可變TenantId、ResolvedLineChannel、唯一ConfigurationLineChannelResolver與legacy薄bridge；不複製Core credential。新增settings provider明確完整scopekey，固定安全warning只含event，不含tenant/token。
4. 將產品所有原解析方法改委派唯一resolver，依原優先序明確選policy；Startup/DI集中註冊，例外sender保持owner取消/drain/釋放。不更動金流或其他業務。
5. source架構檢查禁止active配置解析鏈副本；方案/CI filter/coverage增加專案，雙目標既有回歸保持有效。
6. 針對故障/缺token/1000A-B/weakref/borrowed DI，逐切片紅綠；整包guard後主會話審查、報告/進度一般資訊、commit及只封存子任務。
目前專案inline，主會話實作/審查，不派子代理，不恢復外部雙模型。ChannelId缺值及legacy無token的相容性界線須在驗收明列，不能宣稱遠端有效。
