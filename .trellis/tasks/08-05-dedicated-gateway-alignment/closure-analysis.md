# P5 Dedicated Gateway 結案缺口分析

> 稽核日期：2026-08-05。此文件只記錄本機證據與結案規劃；未啟動 P6、未切換 ChurchReport 流量、未執行外部 CE 或外部模型。

## 結論

P5 的 Dedicated Gateway 架構、離線測試、Release build 與 CRLF-only 格式閘門均已具備可重現的正向證據。三個 P5 範圍 C# 檔案原有的 UTF-8／CRLF release blocker 已以純格式化、無語意變更的修復解除；P5 目前維持 `in_progress` 的唯一原因是等待使用者決定是否結案，而不是尚存技術 blocker。

## 已確認的需求證據

| P5 要求 | 目前證據 | 結果 |
|---|---|---|
| Embedded/Dedicated 共用 runtime、各 host 不共用 mutable state | `Data8ProfileRuntime` 是各 host 擁有的 `IAsyncDisposable`；`EmbeddedData8Runtime` 只委派自己的 instance；pool→admission 的 cleanup 順序已實作 | 通過本機程式稽核 |
| Dedicated 僅使用 Data8 + In-Memory coordinator，排除 Official Worker/SQL | `Program.cs` 的 Dedicated branch 只註冊 `Data8ProfileRuntime`、Data8 executor 與 hosted disposal service；Official overlay/worker/SQL 註冊都在非 Dedicated branch | 通過本機程式稽核 |
| Gateway Dedicated origin、principal binding 與 fail-closed request boundary | Dedicated branch 將 request origin 固定為 `RequestOrigin.DedicatedGateway`；guard 在 executor/lease 前執行 | 通過本機程式稽核 |
| ChurchReport 的可切換 Dedicated F5 與 HTTPS localhost | ChurchReport 與 Gateway 各有 `DedicatedGateway` launch profile；文件指定 Gateway 先啟動、ChurchReport 後啟動，並維持一般 Development profile 為 Embedded | 通過本機組態／文件稽核 |
| focused automated tests | Dynamics focused：4 passed；ChurchReport focused：28 passed | 通過 |
| 全套 automated tests | `SpeechMessage.Dynamics.Tests`：446 passed、7 SQL live tests skipped；`ChurchReport.MemberInfo.Tests`：401 passed、1 live test skipped | 通過；skips 為非 P5 外部 live gates |
| Release build | `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：0 warnings、0 errors | 通過 |
| P6/CE 真機不被 P5 偽稱完成 | 未呼叫 CE、未啟用 consumer；P6 與 CE 8.2/9.1 evidence 保持後續閘門 | 正確保持未完成 |

## Release-blocking 缺口（修復前基線，已解除）

位元組層級 audit 對三個 P5 commit 修改的 C# 檔案得到以下結果：

| 檔案 | UTF-8 無 BOM | lone LF | final CRLF | 結論 |
|---|---:|---:|---:|---|
| `SpeechMessage.Dynamics.Gateway/DedicatedGatewayData8Configuration.cs` | 是 | 93 | 否 | 必修 |
| `SpeechMessage.Dynamics.Gateway/Program.cs` | 是 | 74 | 是 | 必修：mixed line endings |
| `ChurchReport.MemberInfo.Tests/CrmConnectionEmbeddedProfileMapperTests.cs` | 是 | 69 | 是 | 必修：mixed line endings |

其餘 7 個由 P5 commits 新增或實質修改的 C# 檔案在修復前已為 UTF-8 無 BOM、CRLF-only、final CRLF。`git diff --check` 不能取代 byte-level line-ending audit；最新完整 byte-level 結果記錄於本文件的「Trellis 最終品質檢查」章節。

## 修復前結案規劃（已完成）

1. 先取得使用者對「只正規化上述三個檔案為 CRLF、不得改變任何 token 或行為」的實作授權。
2. 正規化後以位元組檢查確認所有 10 個 P5 C# 檔案均為 UTF-8 無 BOM、CRLF-only、final CRLF。
3. 依序重跑 P5 focused tests、兩個完整 test projects、Release build 與 `git diff --check`；測試必須串行執行，避免共用 `obj/bin` 造成 MSBuild 檔案鎖定。
4. 檢查 diff 僅含這三個 line-ending-only 變更與本結案記錄；不得修改產品設定、feature flag、Registry、P6 Worker 或 ChurchReport consumer。
5. 將驗收結果交由使用者審閱。取得明確結案／提交通知前，P5 維持 `in_progress`；不 archive、commit、push，也不啟動 P6。

## 非阻擋但不得誤解的事項

- P5 沒有、也不應有真實 CE 8.2/9.1 operation、效能、soak 或跨模式 parity 證據；這些是 P6 後的受控整合閘門。
- `Package01FeeReadsEnabled` 保持 false，沒有 ChurchReport 業務流量被移至 Gateway。
- P5 的 `implement.jsonl` 與 `check.jsonl` 仍是 seed 範例；本 session 使用 Codex inline 模式，依 Trellis 規則不需為 sub-agent dispatch 另行 curate，因此不是本輪結案 blocker。

## CRLF 修復與複驗結果（2026-08-05）

已依明確授權，且只對下列三個檔案執行 byte-preserving 的行結尾正規化：所有既有 `LF` 或 `CRLF` 統一為 `CRLF`，並只為第一個檔案補上缺失的 final CRLF。未修改任何程式 token、設定或測試語意。

| 檔案 | 修復前 lone LF | 修復後 lone LF | 修復後 final CRLF | LF-normalized SHA-256 前後一致 |
|---|---:|---:|---:|---:|
| `SpeechMessage.Dynamics.Gateway/DedicatedGatewayData8Configuration.cs` | 93 | 0 | 是 | 是 |
| `SpeechMessage.Dynamics.Gateway/Program.cs` | 74 | 0 | 是 | 是 |
| `ChurchReport.MemberInfo.Tests/CrmConnectionEmbeddedProfileMapperTests.cs` | 69 | 0 | 是 | 是 |

以 strict UTF-8 decoder 與 byte-level audit 驗證完整 10 個 P5 C# 檔案後，全部皆為 UTF-8 無 BOM、CRLF-only、final CRLF；三個修復檔案的 LF-normalized SHA-256 與修復前基線完全一致，因此可證明沒有 token／語意改變。

本次依序完成的驗證如下：

1. Dynamics focused tests：4 passed。
2. ChurchReport focused tests：18 passed。
3. `SpeechMessage.Dynamics.Tests` 全套：446 passed、7 expected SQL live skips。
4. `ChurchReport.MemberInfo.Tests` 全套：401 passed、1 expected live skip。
5. `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：0 warnings、0 errors。
6. `git diff --check`：通過；三個修復檔案皆以 `git diff --ignore-space-at-eol --exit-code` 驗證為 line-ending-only。

第一次完整 Dynamics 測試曾有一個未修改的 `OfficialWorkerSoakAndPerformanceTests` private-bytes 趨勢斷言因測試序列中的瞬時採樣波動失敗。隨後該測試獨立重跑通過，且最終完整 suite 亦通過；期間未修改該測試或其他任何非授權產品檔案。

P5 依使用者指示維持 `in_progress`，等待審閱決定；未啟動 P6、未啟用 feature flag、未切換 ChurchReport 流量、未執行外部 CE 或雙模型 review，亦未 commit、archive、push。

## Trellis 最終品質檢查（2026-08-05）

`trellis-check` 已通過。本輪只稽核與補充本 task 的 closure 記錄，未修改產品程式、設定、測試語意或 P5 task status。

| 品質閘門 | 最終證據 | 結果 |
|---|---|---|
| 10 個 P5 C# 檔案格式 | strict UTF-8 decoder、無 BOM、0 lone LF／CR、final CRLF | 通過 |
| 三個產品檔語意範圍 | LF-normalized SHA-256 與修復前基線一致；`git diff --ignore-space-at-eol --exit-code` 無差異 | 僅 line-ending-only |
| focused tests | Dynamics 4 passed；ChurchReport 18 passed | 通過 |
| 完整 tests | Dynamics 446 passed、7 expected SQL live skips；ChurchReport 401 passed、1 expected live skip | 通過 |
| Release build | `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo`：0 warnings、0 errors | 通過 |
| 工作區與 whitespace | `git diff --check` 通過；產品範圍只剩三個授權 CRLF 檔案 | 通過 |

第一次完整 Dynamics suite 曾在未修改的 `GatewayRequestBodyBoundaryTests.Kestrel_http11_rejects_declared_and_chunked_limit_plus_one` 收到 `HttpIOException: ResponseEnded`。該檔案沒有工作區 diff，測試獨立重跑通過，之後完整 suite 亦通過；因此記為 Kestrel 測試序列中的瞬時傳輸層波動，而非 P5 CRLF 修復造成的行為回歸。未為此進行任何非授權程式修改。

本次沒有發現需沉澱到 `.trellis/spec/` 的新程式契約或可重複使用模式：變更只涉及既有檔案的無語意行結尾正規化。P5 維持 `in_progress`，不 commit、archive、push，也不啟動 P6 或 consumer 流量。

## Phase 3.3 Spec Update 判斷（2026-08-05）

不更新 `.trellis/spec/`。本次只將三個既有 C# 檔案正規化為既有規格已要求的 UTF-8 無 BOM、CRLF-only 與 final CRLF，LF-normalized SHA-256 也證明沒有任何程式 token、API、資料流、資源所有權、設定或測試契約變化。

現有 `backend/dynamics-gateway-hosting-version-routing.md` 的「Source documentation and text encoding」與「Documentation and encoding gates」已明確規定本次所驗證的格式與失敗條件；新增或重述規格只會造成重複，無法提供未來實作所需的新可執行契約。P5 因而保持 `in_progress`，等待使用者的結案決定。
