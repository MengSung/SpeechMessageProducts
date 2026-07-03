# CCG Review: LINE Processor SDK-Backed SendMessage

## Scope

Reviewed the `LineMessagingProcessorClass.SendMessage(string UserId, string Message)` refactor that removes the hand-built RestSharp `/bot/message/push` request and delegates push delivery to `Line.Messaging.LineMessagingClient.PushMessageAsync(...)`.

## External Review Commands

- Gemini: `codeagent-wrapper.exe --lite --backend gemini - <worktree>`
- Claude: `codeagent-wrapper.exe --lite --backend claude - <worktree>`

## External Review Results

- Gemini completed with exit code 0.
- Gemini initially found one valid Critical issue: the legacy confirmation trigger/reply strings had been preserved as mojibake instead of the intended Traditional Chinese literals.
- The Critical issue was fixed: source and tests now use `顯示認證` and `認證:`.
- Gemini rerun via the wrapper still showed transport-encoded mojibake in its captured prompt/output, but direct source scans confirmed the code and tests no longer contain `憿舐內` or `隤`.
- Claude was invoked with the reviewer role as required, but the wrapper exited with status 1 before producing a usable review. The failure was recorded as a CCG toolchain issue, not as code approval.

## Lead Synthesis

### Critical

- None remaining after fixing the legacy confirmation text encoding.

### Warning

- ~~Claude review is not available because `codeagent-wrapper.exe --lite --backend claude` exited with status 1 in stdin mode.~~
  **2026-07-03 補跑完成**（exit 0，全文見 `review-claude-final.txt`）。Claude 結論 REQUEST_CHANGES（C1/C2），主審裁定如下：
  - **C1 成立（真缺口）**：LineMessagingProcessor.Tests 全部 13 個測試都用 DI 建構子（`_requiresChannelAccessToken == false`），生產環境實際使用的建構子路徑（無參數 / string token / IConfiguration）與「token 缺失時拋例外」的保護行為零測試覆蓋。此缺口涵蓋全部四個 P1 adapter 測試檔，不只 SendMessage。→ 收尾前應補：production 建構子 + 空 token 拋例外測試、有效 token 正常送出測試。
  - **C2 前提不成立，降級為 Info**：舊版 RestSharp 為 112.1.0，其 `PostAsync` 便捷方法在非 2xx 時預設即拋例外（`ThrowIfError`），並非「靜默吞錯」；新舊路徑同為「失敗拋例外」，實際差異僅例外型別（`HttpRequestException` → SDK 例外）。補一條非 2xx 行為測試仍值得做，但非阻斷。
  - W1 成立（`LineMessagingProcessorClass.cs:254` 註解點名 ChurchReport，違反可重用模組中立性）；W2/W3 為合理建議，非阻斷。

### Info

- `RestSharp` usage and `_restClient` are removed from `LineMessagingProcessorClass`.
- Normal sends and the legacy confirmation-code flow now share the SDK push path.
- Blank `UserId` / `Message` validation happens before any HTTP call.
- Tokenless normal constructors still fail closed before sending. Injected SDK-client constructors remain usable for tests/DI.
- No ChurchReport/CRM/controller dependencies were introduced into `LineMessagingProcessor`.

## Verification Evidence

- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj --filter LineMessagingProcessorSendMessageTests -v minimal` passed 8/8.
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal` passed 33/33.
- `dotnet test Line.Messaging.Tests\Line.Messaging.Tests.csproj -v minimal` passed 30/30.
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` completed with 0 errors.
- Boundary scan found no product-specific `LineMessagingProcessor` dependencies for `new_lineid`, `LineIdLogin`, `RetrieveContactEntityByLineUserId`, `Controller`, `IActionResult`, `Microsoft.Xrm`, `CRM`, or `Contact`.
- Touched text files were checked as UTF-8 without BOM and CRLF.