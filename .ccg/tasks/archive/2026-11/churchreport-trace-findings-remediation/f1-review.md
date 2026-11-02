# F1 背景上傳狀態隔離審查紀錄

日期：2026-08-22

## 變更範圍

- `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs`
- `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs`
- `SpeechMessageProducts.ChurchReport/Models/SmallGroupDataList.cs`
- `SpeechMessageProducts.ChurchReport/Models/Member.cs`
- `ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs`
- `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
- `ToolUtility/Factory/ToolUtilityFactory.cs`
- `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
- `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`
- `ChurchReport.MemberInfo.Tests/Models/LegacyToolUtilityFactoryCollection.cs`

## F1.1 盤點與設計決策

指定三個集合的完整 repository literal scan 為 140 筆；產品 C# 精確可執行命中為 34 筆，納入
`?.Members` 變體後為 44 筆，超過計畫的 30 處門檻。完整分類見
`f1-usage-inventory.md`。

因此採用 PRD/design 的唯讀退路：request 期間建立深層、背景專屬副本；背景上傳與清理只操作
副本，不將成員集合發布或替換回 Session／IMemoryCache。這避免 14 秒背景工作的舊快照覆蓋同期
前景 CRUD，也避免在超過 30 個 legacy 讀寫端之間建立不完整的全域鎖協定。回應保留 `status`、
`message`，新增 `requiresRefresh=true`。

`CreateIsolatedSnapshot()` 的 lock 只保護快照建立的短臨界區，不宣稱尚未採用同一鎖的其他
legacy writer 已具全域執行緒安全；背景長時間清理不持有該鎖。

## 外部審查

已透過 `docs/scripts/Start-CcgDualModelRun.ps1` 自我修復入口執行 reviewer；產物位於
`.ccg/dual-model-runs/20260822-120207-churchreport-trace-remediation-f1-review-reviewer/`。

- Gemini：兩次嘗試皆產生可用報告，均無 Critical。第一次 PASS；第二次提出一項 Warning：
  背景清理例外只用 `Debug.WriteLine`，Release 可能不可觀測。
- Claude：兩次嘗試皆 `no-usable-output`，不是 quota fallback；runner `ok=false`、
  `degradedFallback=false`。因此本次是 Gemini-only 降級審查，**不可宣稱雙模型審查完成**。

已採納 Warning：清理 catch 現在保留去敏 Debug 訊息，並呼叫 `ToolUtilityClass.TraceByLevelStatic`
記錄例外型別；不輸出例外文字、stack、帳號、密碼或成員內容。診斷失敗仍不會中斷背景副本
收尾或 using scope 的釋放。

Gemini 提到的「亂碼註解」與本任務無關且未被本地 byte-level 檢查證實：修改檔均為 UTF-8 無 BOM、
全 CRLF、final CRLF、無 PUA/replacement character。未擴大修改既有編碼另案範圍。

## 本地驗證

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore `
  --filter FullyQualifiedName~SmallGroupDataListSnapshotIsolationTests `
  -p:BaseOutputPath="ChurchReport.MemberInfo.Tests\bin\f1-model-focused4\"
```

結果：3/3 通過。

```powershell
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore `
  -p:OutputPath="$env:TEMP\churchreport-f1-compile-final3\"
```

結果：0 warnings、0 errors。

完整 `ChurchReport.MemberInfo.Tests` 結果：312 通過、23 失敗。失敗皆為既存付款命名契約或
測試以錯誤工作目錄尋找 `ChurchReport.sln`／原始碼的問題，沒有 F1 模型或編譯失敗；F1 focused
測試與專案建置均獨立通過。

```powershell
python .trellis/scripts/check_encoding.py
git diff --check
```

結果：修改的五個 C# 檔均 `noBOM CRLF endsCRLF noPUA ok`，`git diff --check` 無輸出。

## 初版結論（已被後續生命週期審查取代）

沒有未處理的 Critical。F1 已移除 SaveIntegrate 背景工作對共享 Session／快取 Members 的持有與
原地修改路徑；背景資源由獨立 DI scope 與 trace scope 擁有並確定性釋放。剩餘的 legacy 前景
讀寫同步缺口已在 spec 中明確標示為範圍外，不能把本修正誤報成整個物件圖已全面 thread-safe。

## 後續生命週期 Critical 修復與最終驗證

初版審查後，以原始碼追蹤確認一項 Critical：`Task.Run` 會流動 `IHttpContextAccessor` 的
`AsyncLocal<HttpContext>`；`UploadIntegrateData` 持有的 legacy `ToolUtilityFactory` ambient service
會優先解析繼承的 `HttpContext.RequestServices`，而不是 SaveIntegrate 新建的 scope。因此原本的
`using var scope` 並非 CRM 操作的實際 owner，request 結束後會形成 disposed-scope race。

修復在 `AmbientGatewayOrganizationService` 建立流程區域的 `AsyncLocal<IServiceProvider>` override，
並由 `ToolUtilityFactory.BeginBackgroundScope(scope.ServiceProvider)` 轉發。SaveIntegrate 在建立背景
scope 後、上傳前以 `using` 套用 override。Ambient resolver 先讀取該 override，故上傳器內第二層
`Task.Run` 也解析同一背景 scope；離開 `using` 時只還原流程值，scope/provider 的唯一 Dispose owner
仍是 SaveIntegrate。這保留 F4 DataverseTrace 的 ExecutionContext 關聯，沒有使用
`ExecutionContext.SuppressFlow()`。

另完成：

- 移除不在上傳資料流的 `m_HappyGroup` 快照複製，縮短非必要會員資料的背景保留。
- 對快照並行測試加入 `ManualResetEventSlim` 啟動閘門，並把兩個會改寫 Factory static 的模型測試放入
  非平行 xUnit collection。
- 修正已提交 F2 將 `GetCurrentSessionId` 改名為 `TryGetSessionCacheKey` 後，來源契約測試仍期待舊名稱的
  計數；新斷言驗證 20 個新 helper 呼叫與合計 51 個診斷呼叫。
- 更新 backend isolation spec，將 inherited request services 與 explicit background override 列為強制契約。

最新外部 reviewer 透過 `Start-CcgDualModelRun.ps1` 執行，產物為
`20260822-124510-churchreport-trace-remediation-f1-lifecycle-review-reviewer/`。Gemini 兩次都完成，沒有
Critical/Warning，只建議將 fallback scope 標示為保底冷路徑；已加入說明註解。Claude 兩次皆
`no-usable-output`（非 quota，`ok=false`、`degradedFallback=false`），故本次**不是完整雙模型審查**。

最新本地驗證：

```powershell
dotnet build ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-restore
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore `
  --filter FullyQualifiedName~SmallGroupDataListSnapshotIsolationTests
python .trellis/scripts/check_encoding.py
git diff --check
```

兩個 build 均為 0 warnings / 0 errors；Dataverse 71/71、ToolUtility 63/63、快照 3/3 通過。
完整 MemberInfo suite 的結果仍為 312 passed / 23 failed；失敗皆是既有付款命名期望或測試在輸出目錄
尋找不存在 `ChurchReport.sln`／原始碼路徑，與 F1 檔案或本次新增測試無關。
