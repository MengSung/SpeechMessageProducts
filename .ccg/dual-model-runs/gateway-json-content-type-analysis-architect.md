# CCG architect Task: gateway-json-content-type-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Gateway JSON-only Content-Type 邊界分析

## 任務背景

目前 `SpeechMessage.Dynamics.Gateway` 的 operation endpoint 已在驗證 Windows 身分與 alias/operation 授權後，使用自訂 `GatewayOperationRequestBodyReader` 讀取並驗證 JSON body。Reader 已具備 Content-Length、chunked byte ceiling、UTF-8、JSON depth、duplicate/unknown member 等硬邊界，但目前只要 body 本身是合法 JSON，即使 caller 宣告 `Content-Type: text/plain` 仍可能被接受。

Gateway 對外契約是 JSON-only。請分析最小且安全的修正方式，不要直接修改程式。

## 必須維持的契約

- Authentication 與 alias/operation authorization 必須先於 body read，也必須先於媒體型別造成的 body parsing。
- 未授權 caller 不得利用不同 Content-Type 探測 body contract；原有 401/403 ordering 不得退化。
- 已授權 request 若缺少 Content-Type、使用非 JSON media type，或使用不支援的 charset，應 fail closed；請建議精確 HTTP status（預期優先考慮 415）與可測試規則。
- 應接受標準 `application/json`，並評估是否接受大小寫差異、參數（例如 `charset=utf-8`）與 `application/*+json`。
- 不得在錯誤回應、log、exception 中回顯 request body、credential、principal、token 或 session 資料。
- 不得新增未界定 owner 的 stream、buffer、timer、subscription、cache 或 background work。
- 新增 Production/Test 程式必須有完整、深入、詳細的繁體中文 XML／實作註解，說明信任邊界、owner、並行、失敗、取消、cleanup、記憶體與效能取捨。
- 新增／修改檔案必須為 UTF-8 without BOM、CRLF、final CRLF。

## 請檢查的檔案

- `SpeechMessage.Dynamics.Gateway/Program.cs`
- `SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs`
- `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

## 期望輸出

1. 建議的媒體型別與 charset 契約。
2. 最小實作位置與理由。
3. 必須先建立的 RED 測試案例與 assertion。
4. 對 authentication/authorization ordering、資源 owner、取消、記憶體與效能的風險檢查。
5. Critical / Warning / Info 分級結論。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.