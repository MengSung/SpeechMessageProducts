# P5-02 PluginHarness 離線黃金工具

本 net48 console 逐個編譯操作者核定的舊專案，再執行真正的 IPlugin 或 CodeActivity。
它與上層 P3-01 Webhook 工具是不同入口，不使用原型合成 attendance 代替舊行為。
以下說明執行契約，不表示 P5-02、全部舊入口或 D365／LINE 實機驗收已完成。

```powershell
dotnet build tools/LegacyGolden/PluginHarness/PluginHarness.csproj -c Release
& tools/LegacyGolden/PluginHarness/bin/Release/net48/LegacyGolden.PluginHarness.exe generate `
  --legacy-root "<核定 Dynamics365Line 根目錄>" `
  --module PresentRecordLinePlugIn `
  --scenarios "<合成 scenarios.json>" `
  --out "<legacy.json>"
```

只接受 generate 與四組必要選項；選項可換序且區分大小寫。未知、重複、缺值及額外
位置參數回 2；單獨 --help 或 -h 回 0。成功發布回 0，其他失敗回 1。stderr 只有
固定說明或安全代碼，不回顯路徑、原始診斷或例外本文。

情境限制一 MiB（包含 UTF-8 BOM），完整嚴格 UTF-8 解碼；拒絕 UTF-16、截斷或
替代字元解碼。ScenarioRunner 驗證 cases；每案含唯一 name、合成 seed 及 trigger。
不得傳入真 token、真 LINE ID 或正式資料。LegacyProjectCompiler 按單一入口及
ProjectReference 閉包只讀編譯，不執行建置事件；只有 Generated、非空 AssemblyBytes
及 EntryType 同時成立才呼叫 runner。CompileFailed 或 BlockedByNetworkApi 不發布。

程序啟動設預設 proxy http://127.0.0.1:9。compiler 仍須改寫已支援的 HttpClient／
RestClient 呼叫並拒絕未攔截 API。runner 擁有每案 AppDomain、scratch、逾時、卸載及
清理，完成後 CLI Dispose CompilationResult 清零組件位元組。來源必須是核定可信
程式；AppDomain 不能取代作業系統對敵意 native code 的安全邊界。

結果含實際 writes、lineCalls、活動 outputs 與例外型別。案例例外可能是黃金的一部分；
runner 基礎設施失敗則停止整批。完整成功候選才由 GoldenWriter 秘密掃描並以 UTF-8
無 BOM、CRLF 原子發布。失敗不新建／覆蓋目標；不得將輸出指向來源或他人檔案。
同一路徑由操作者串行產生，不協調不同程序的發布順序。

```powershell
dotnet test SpeechMessage.Dynamics.Plugins.Tests -c Release --no-restore `
  --filter "FullyQualifiedName~PluginHarnessCliTests"
& tools/LegacyGolden/PluginHarness/Test-PluginHarness.ps1 `
  -LegacyRoot "<核定舊根>" -Module PresentRecordLinePlugIn `
  -Scenarios "<合成 scenarios.json>" `
  -HarnessPath "tools/LegacyGolden/PluginHarness/bin/Release/net48/LegacyGolden.PluginHarness.exe" `
  -OutputDirectory "<ignored 驗證目錄>"
```

CLI 測試以子程序驗證說明、參數、大小、UTF-8、脫敏及失敗不寫檔，無須舊方案或外網。
腳本對相同真入口執行兩次，核對 SHA-256、案例名稱及筆數與至少一項實際效果，finally
只清除本次唯一子目錄的兩個候選。它不固定期待三筆手寫 attendance 或原型網路摘要。
出席至少三案、正常案效果、編譯矩陣、差異宣告及覆蓋率仍須由工作包另外驗證。

情境可選填 `queryResults`，用一開始為一的 `RetrieveMultiple` 呼叫次序指定 seed
代號，例如 `"queryResults":{"1":["contact-2","contact-1"],"2":[]}`。這代表第一次
依明列順序回傳兩筆不同 ID 的記錄，第二次明確回傳空集合；相同姓名不合併。
答案為 seed 的獨立快照，後續寫入不會修改答案；每個案例的答案與呼叫序號重新建立。
未列出的查詢仍由嚴格假查詢引擎處理，不支援的語法繼續丟例外。

序號只接受 `1` 至 `256` 的標準十進位字串，最多覆蓋 256 次查詢、合計 4096 答案列。
未知 alias、同一答案內重複 ID、非陣列、非字串代號、JSON 重複屬性與超限輸入均拒絕，
不執行入口、不回傳部分案例。`null` 不等於省略；顯式空答案應寫 `[]`。
