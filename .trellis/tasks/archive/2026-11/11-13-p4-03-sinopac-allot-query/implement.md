# P4-03 執行清單

依 `04A_階段4_執行細則.md` §5 P4-03 執行；測試名稱、產品檔案位置與提交訊息固定，不改名、不提前處理 P4-05/P4-06。

1. **啟動與基線：** 以 `task.py start` 啟動本子任務；讀取 `SpeechMessage.Payments`、Core、Workflow、ToolUtility、ASP.NET Core integration、LineWebhook 的既有程式和測試模式；依 backend index 完成 package/layer specs。不得連 D365、永豐或部署站。
2. **先寫 provider 測試並確認紅燈：** 在 `SpeechMessage.Payments.Tests\Providers\Sinopac\SinopacAllotQueryTests.cs` 加入 `AllotQuery_Request_MatchesGolden`、`AllotQuery_Response_MapsBatchesToMoney`，使用 P4-01 已核准的 `SpeechMessage.Payments.Tests\Golden\Sinopac\allot-query-response.json`、固定 nonce 及合成 credential。
3. **實作 Payments provider：** 依 04A 指定新增 `Abstractions\IPaymentAllotGateway.cs`、內部 `Abstractions\IPaymentAllotProvider.cs`、`Models\PaymentAllot*.cs`；修改 `Providers\Sinopac\SinopacModels.cs`、`SinopacRequestMapper.cs`、`SinopacPaymentProvider.cs`、`Gateway\PaymentGateway.cs`、`DependencyInjection\ServiceCollectionExtensions.cs`。完成 AllotQuery request mapping、簽章驗證、nonce/decryption、`Money` batch mapping 與 Gateway delegation。
4. **先寫 Core 規則測試並確認紅燈：** 在 `SpeechMessageProducts.GoodShepherd.Core.Tests\Fees\AllotReconciliationRuleTests.cs` 加入 `Reconcile_LegacyCases_SameFourFields`、`ResolvePayType_ByPayWayAndPrefix`、`ResolveWindow_ByPayDateAndToday`、`Match_BlankOrderNumbers_NeverMatch`，驗證兩個 target；fixture 使用既有 `allot-legacy-cases.json`。
5. **實作 Core 規則與資料存取：** 新增 `SpeechMessageProducts.GoodShepherd.Core\Fees\AllotReconciliationRule.cs`、`FeeAllotRecords.cs`。以 server-owned 教會範圍讀取 fee/member/parentcustomerid/timezone，空白時區用 `Asia/Taipei`；僅以 row version 條件更新四欄，撥款日期寫同日午夜。
6. **先寫 store 測試並確認紅燈：** 在 `ToolUtility.Tests\DataOperations\OutboundTaskClaimTests.cs` 新增 `Store_AllowedTypes_QueriesOnlyThoseTypes`。
7. **限制待辦查詢：** 修改 `ToolUtility.Core\Outbound\D365OutboundTaskStore.cs` 與 `ToolUtility\Outbound\D365OutboundWorkStoreAdapter.cs`，以 `allowedTypes` 將類型限制送到 D365 查詢；未指定時保持舊行為及既有測試斷言。
8. **先寫 handler 測試並確認紅燈：** 在 `SpeechMessage.Payments.Tests\Workflows\AllotQueryHandlerTests.cs` 加入 `Handler_NoData_CompletesWithoutUpdate`、`Handler_Http5xxOrNetwork_IsRetryable`、`Handler_ProviderRejected_NotRetried`、`Handler_FeeOfOtherChurch_Rejected`、`Handler_SameTaskTwice_SameResult`、`Handler_UnreadableResponse_IsRetryable`、`Handler_InvalidPayload_Rejected`、`Handler_UsesChurchToday_AcrossUtcMidnight`、`Update_AllotDate_IsSameCalendarDay`。
9. **實作 handler：** 新增 `SpeechMessage.Payments.Workflows\Allot\AllotQueryHandler.cs`。嚴格照 design 的流程順序讀取授權資料與 profile、解析 pay type/window、查詢、比對、條件更新；遵守錯誤分類及無資料成功無操作契約。
10. **先寫 host registration 測試並確認紅燈：** 在 `SpeechMessageProducts.LineWebhook.Tests\PaymentOutboundHostTests.cs` 加入 `Worker_Disabled_NotRegistered`、`Factory_ScopeDisposed_ResourcesReturnToBaseline`、`Host_WithoutPaymentSection_StillStarts`。100 次建立/釋放後 scope 與 handler counters 必須歸零；缺 Payment 區段以 `Program.BuildApplication` 驗證。
11. **實作 AspNetCore 註冊：** 新增 `SpeechMessage.Payments.AspNetCore\Outbound\PaymentOutboundRegistration.cs`，含內部 store adapter 與 handler factory；修改該專案 csproj 以參考 `SpeechMessage.Hosting`、`ToolUtility`。Disabled 時不註冊；enabled 時 handler scope 明確有界並確定性釋放。
12. **整合 LineWebhook：** 修改 `SpeechMessageProducts.LineWebhook.csproj` 參考 `SpeechMessage.Payments.AspNetCore`，修改 `Program.cs` 加入 `AddSpeechMessagePayments(builder.Configuration.GetSection("Payment"))` 和 `AddSpeechMessagePaymentOutbound(builder.Configuration)`；同步更新檔首繁中說明，刪除已失效的「不啟動階段四 Payments」敘述。禁止啟動本機整台 LineWebhook 或部署 P309。
13. **執行指定測試：** `dotnet test SpeechMessage.Payments.Tests -c Release`；`dotnet test SpeechMessageProducts.GoodShepherd.Core.Tests -c Release`；`dotnet test ToolUtility.Tests -c Release`；`dotnet test SpeechMessageProducts.LineWebhook.Tests -c Release`；`dotnet test SpeechMessage.Hosting.Tests -c Release --filter "FullyQualifiedName~Architecture"`。確認 Core 兩個 framework 都執行。
14. **整合檢查：** 程式差異限定於 04A §5 清單；不修改 ChurchReport 設定、既有金鑰、LegacyGolden 或無關工作樹檔案。跑 file documentation、secret scan、bytes/line endings、測試和名稱核對。記錄未實測的沙箱狀態為 P4-06 未驗證。
15. **提交與守門：** 以 `P4-03 新增永豐撥款查詢處理器並由 LineWebhook 承載對外待辦` 提交程式；提交後更新完整一頁回報與進度頁 DATA。固定 `C:\smv\guard` 執行 00 §8.1，遇錯以新提交修正並重跑完整守門。
16. **驗收、推送與封存：** 00 §7.1 五項及本任務全部正式測試同名通過後，封存本子任務；只改紀錄的提交跑 §8.3。推送 P4 分支並以 `git ls-remote origin refs/heads/1.0.0.11.Integration.P4.Worktree` 核對遠端 SHA，然後才進 P4-05。

**時間盒：** 1 天；最多 15 個提交。超過時依 04A §7 一次提出三個可執行選項，未受影響的工作包仍照核定順序處理。
