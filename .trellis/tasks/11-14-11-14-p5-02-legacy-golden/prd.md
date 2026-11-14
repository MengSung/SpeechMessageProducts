# P5-02 舊程式黃金資料產生器
依05A修訂1、§4.2、§4A、P5-02執行；2026-11-14撤回原型完成標記。
## 必要成果
- net48嚴格console，加入方案/CI/coverage；連結共用Fakes，不複製舊來源。
- generate --legacy-root --module --scenarios --out：逐舊專案與project-reference閉包Roslyn編譯，執行真實IPlugin/CodeActivity，記錄D365寫入、LINE請求、活動輸出及例外型別。
- 語法攔截HttpClient/RestClient；失效loopback預設proxy；未攔截連外API拒絕；檔案副作用導向單次scratch並清理；Domain finally卸載，不保留跨情境狀態。
- 輸出前拒絕真LINE ID、超100 base64/JWT/Bearer，失敗不發布；三檔schema與declaredDifferences機器驗證。
- PresentRecord實際執行至少三情境，正常情境必有可驗證效果；全部29入口只編譯矩陣；新工具獨立覆蓋率至少80%。
## 同名驗收測試
Harness_SyntheticPlugin_RecordsWritesAndLineCalls、Harness_SyntheticActivity_RecordsOutputArguments、Harness_SameScenarioTwice_ByteIdenticalOutput、Harness_CompileFailure_ReportedNotThrown、Harness_UninterceptedNetworkApi_RefusesToRun、Harness_OutputWithSecretPattern_NotWritten、Expected_DiffersFromLegacy_OnlyByDeclaredDifferences。
## 證據界線
原型合成attendance不是舊行為黃金。無法安全執行逐入口依D5-3記錄，不得以IOrganizationService拒絕全部。實機另依D5-18，不在此包連外。37個第一層csproj與計畫36差異如實記錄。
