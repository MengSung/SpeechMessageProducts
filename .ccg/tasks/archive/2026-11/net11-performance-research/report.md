# 本方案 .NET 10 → .NET 11 效能研究報告

## 1. 結論先行

本研究以本方案既有 Release 產物的**同一份 IL**，分別載入 Microsoft.NETCore.App／Microsoft.AspNetCore.App **10.0.15** 與 **11.0.0**，量測實際 Webhook endpoint 及數個產品純資料路徑。這是 runtime-only 對照，不是把方案重新 target 到 `net11.0` 的升級驗收。

在目前固定輸入及本機環境下，結論是：

1. **正式 Webhook 成功路徑沒有可辨識的整體改善。** 6 組程序配對的 loopback endpoint，.NET 11／.NET 10 吞吐比值為：
   - 0 ms 合成等待、併行 1：`0.9942`，約 `-0.6%`，95% CI `0.9483–1.1399`。
   - 0 ms 合成等待、併行 16：`0.9958`，約 `-0.4%`，95% CI `0.9383–1.0289`。
   - 10 ms 合成等待、併行 16：`1.0003`，約 `+0.03%`，95% CI `0.9976–1.0022`。
   三者都不能宣稱實務改善；在有外部等待時，runtime 差異更被等待時間稀釋。`10 ms` 是 `Task.Delay(10)` 的參數，但 Windows 實測 p50 約 `15.6 ms`，主要受計時器粒度影響，因此這一列只作為「有等待時差異消失」的敏感度資料，不作精準 10 ms 模型。
2. **會友樹純資料組裝是目前最有訊號的路徑，但仍屬探索性結果。** 100 組小組的 wall-clock 中位數比值為 `0.9097`，1000 組為 `0.9340`；但程序間變異很大，單輪比值範圍分別為 `0.500–0.968` 與 `0.634–1.577`。因此只能暫定「約 0–10% 的候選改善」，不能把 9% 當成穩定承諾；`tree_1000` 的 7/8 輪方向有利仍不足以通過多重比較後的確認標準。
3. **驗簽、JSON 事件解析、Flex、文字格式化、Newtonsoft 序列化沒有穩定且可泛化的改善。** 例如：
   - `parse_32`：`0.9906`，約 `-0.9%`，CI `0.7236–1.0285`。
   - `flex_20`：`0.9448`，約 `-5.5%`，CI `0.6368–1.0757`。
   - `signature_1`：`0.9736`，約 `-2.6%`，CI `0.9246–1.3821`。
   - `newtonsoft_rows_1000`：`0.9478`，CI `0.9008–1.1234`。
   這些區間跨過 1，應判定為「未檢出可辨識差異」，不是改善。
4. **對整站的合理期待應低於上述純函式上限。** 若假設某個請求的可受 .NET runtime 影響部分只有整體時間的 5%、10%、20%、50%，而且該部分真的得到 6.6%～9.0% 加速，Amdahl 情境的整體改善約為：

   | 受影響本地 CPU 占比（假設） | 整體改善（6.6% 本地加速） | 整體改善（9.0% 本地加速） |
   |---:|---:|---:|
   | 5% | 0.33% | 0.45% |
   | 10% | 0.66% | 0.90% |
   | 20% | 1.32% | 1.81% |
   | 50% | 3.30% | 4.52% |
   | 100% | 6.60% | 9.03% |

   表中的占比是推估參數，不是正式流量測量。SQL、Dataverse、LINE API、TLS、瀏覽器、IIS／反向代理及資料庫鎖等待都不在這次離線研究中。

**建議：現在維持 .NET 10，不為了效能數字立即升級 .NET 11。** 未來若有明確的高 CPU 純資料熱點、或要取得 `net11.0` 專屬的 ASP.NET Core runtime-async／Kestrel 改善，再做正式 target／套件升級分支並以生產流量重驗。

## 2. 研究範圍與公平性

### 2.1 量測的實際產品路徑

- `SpeechMessageProducts.LineWebhook/LineWebhookEndpoints.cs`
  - body 上限、`ArrayPool<byte>` 讀取、驗簽、正式 `JsonDocument` 事件解析、唯一 ID 檢查、claim／queue handoff、registration `DisposeAsync`。
- `LineMessagingProcessor.AspNetCore/Webhooks/Signatures/LineWebhookSignatureVerifier.cs`
  - 真正產品驗簽 adapter，使用固定合成 channel secret，不接觸正式設定。
- `LineMessagingProcessor.Core/Templates/FlexTemplateEngine.cs`
  - 真正 Newtonsoft `JObject` 模板解析與 placeholder 替換。
- `SpeechMessage.Localization/SystemTextTable.cs`
  - 真正系統文字表查詢與格式化接縫。
- `DistrictTreeBuilder`
  - 以原始來源快照直接編譯的純資料組裝；輸入使用穩定且唯一的合成群組／成員 ID。

未執行產品 `Program.Main`，沒有連線到 SQL、Dataverse、LINE、IIS、正式網域、秘密、appsettings 或使用者資料。

### 2.2 同一份 IL

- 以 SDK 10.0.403 的 `csc.dll` 編譯一次研究 harness。
- 研究 harness IL SHA-256：
  `9dc6644f8e05fc26f3e436426435a1e5b3057f509bc3ffa630bb9f16324eef4c`
- 兩個 runtime 使用同一份 `Net11Research.dll`，只切換 runtimeconfig 的 shared framework 版本。
- 固定 `ServerGC=true`、Tiered Compilation、Tiered PGO、QuickJIT、ReadyToRun。
- micro 與 server 量測程序驗證實際 runtime 版本、`System.Private.CoreLib` 路徑、Server GC、CPU affinity；client 固定使用 .NET 10，並由相同 runtimeconfig 啟動。
- 每個 micro case 先做 correctness oracle，再暖機，最後保存 7 個程序內樣本；統計獨立單位是相鄰 10／11 程序配對，不是單次呼叫。
- micro 8 對程序、HTTP 6 對程序；AB／BA 交錯。

### 2.3 硬體與執行環境

- CPU：Intel Core Ultra 9 275HX，24 physical/logical processors。
- OS：Windows 11 Professional，build 26300。
- 電源方案：Balanced；研究未修改全機電源或其他程序設定。
- 每個研究程序固定在同一個 Windows CPU-set `EfficiencyClass=1` 集合；server 與 client 使用不同集合。
- Runtime：
  - .NET 10：10.0.15
  - .NET 11：11.0.0
- Newtonsoft.Json：13.0.4；未更換套件版本。

## 3. 實測結果

### 3.1 純 CPU／配置案例

比值均為 **.NET 11 / .NET 10**；wall、CPU、bytes 越低越好。CI 是程序級配對 bootstrap 95% CI，但 `n=8` 時只能當描述性區間，不能視為有良好涵蓋率的確認性 95% 信賴區間。`CPU` 是 process-wide `TotalProcessorTime`，可能包含 GC、JIT 或其他背景執行緒，不能單獨當成案例本身的 CPU 證明。

| 案例 | n | wall 比值 | 95% CI | CPU 比值 | 配置比值 | 判讀 |
|---|---:|---:|---|---:|---:|---|
| `tree_100` | 8 | 0.9097 | 0.8831–0.9564 | 0.9142 | 0.9907 | 探索性方向有利，變異大 |
| `tree_1000` | 8 | 0.9340 | 0.6895–0.9900 | 0.8967 | 1.0028 | 探索性方向有利，證據弱 |
| `flex_20` | 8 | 0.9448 | 0.6368–1.0757 | 0.8893 | 1.0000 | 未檢出可辨識差異 |
| `text_lookup` | 8 | 0.8254 | 0.5806–1.0196 | 0.8526 | 0.8750 | 方向有利但 CI 跨 1 |
| `newtonsoft_rows_1000` | 8 | 0.9478 | 0.9008–1.1234 | 1.2862 | 1.0000 | 未檢出可辨識差異 |
| `text_format` | 8 | 0.9684 | 0.5975–1.0765 | 1.0025 | 0.9967 | 未檢出可辨識差異 |
| `signature_1` | 8 | 0.9736 | 0.9246–1.3821 | 0.9767 | 1.0000 | 未檢出可辨識差異 |
| `parse_32` | 8 | 0.9906 | 0.7236–1.0285 | 0.9924 | 1.0000 | 未檢出可辨識差異 |
| `flex_1` | 8 | 0.9925 | 0.8297–1.0379 | 1.0065 | 1.0000 | 未檢出可辨識差異 |
| `parse_1` | 8 | 1.0518 | 0.9613–1.5008 | 1.0631 | 1.0000 | 未檢出可辨識差異 |
| `signature_32` | 8 | 1.0340 | 0.9682–1.3254 | 1.0287 | 1.0000 | 未檢出可辨識差異 |

`tree_100`／`tree_1000` 的改善不能直接解讀為「JIT 固定改善 7%～9%」。Builder 同時使用 LINQ、Dictionary、HashSet、排序、字串複製及大量候選建立；量測只能說這個完整純資料 workload 在該環境有改善訊號，不能把收益歸因給單一 JIT pass。

### 3.2 真正 Webhook endpoint

這一段使用真實 `MapLineWebhook`、真實驗簽器與真實 JSON parser；deduplicator／queue 是明確標示的 bounded synthetic sink，只用來隔離 endpoint 的本地成本，不代表 SQL 去重、可靠佇列、背景 worker 或外部 I/O。

| 合成 claim 等待 | 併行 | .NET 11/.NET 10 吞吐 | 95% CI | p50 比值 | p95 比值 | p99 比值 | 判讀 |
|---:|---:|---:|---|---:|---:|---:|---|
| 0 ms | 1 | 0.9942 | 0.9483–1.1399 | 0.9501 | 1.0118 | 0.8319 | 未檢出可辨識差異 |
| 0 ms | 16 | 0.9958 | 0.9383–1.0289 | 0.9991 | 1.0427 | 0.9808 | 未檢出可辨識差異 |
| 10 ms | 16 | 1.0003 | 0.9976–1.0022 | 0.9984 | 0.9990 | 1.0699 | 未檢出可辨識差異 |

固定成本與限制：

- endpoint 為每次請求 rent body，驗簽／解析後將整個 rented buffer 清零再歸還；小 body 也可能承擔固定清零成本。
- 合成 body 是 compact JSON；沒有刻意製造官方 JSON benchmark 中的長 whitespace runs 或大量需要 escaping 的 writer 輸入。
- 這次測量的是 valid request 主路徑；ASP.NET Core 11 對大量 malformed HTTP/1.1 request 的 non-throwing parser 改善不會自然反映在 valid request 結果。
- client 使用固定 .NET 10；server 才在 10／11 間切換。每輪完成後檢查 accepted、committed、active、scopeErrors、pending 及 process shutdown。

## 4. 官方 .NET 11 改善如何映射到本方案

官方材料：

- [Performance Improvements in .NET 11](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/)
- [.NET 11 what's new](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview)
- [ASP.NET Core 11 release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11?view=aspnetcore-11.0)
- [Migrate ASP.NET Core 10 to 11](https://learn.microsoft.com/en-us/aspnet/core/migration/100-to-110?view=aspnetcore-11.0)

### 4.1 JIT、bounds check、deabstraction

官方文章展示了 deabstraction、bounds-check removal、register allocation、LINQ／collection 等改善。這些改善最可能對本方案的：

- `DistrictTreeBuilder` 的 `HashSet`、`Dictionary`、LINQ 篩選／分組／排序；
- 大量純記憶體資料投影；
- 某些 `Span`／字串／陣列熱點；

產生效果。實測樹組裝是唯一出現較一致訊號的完整產品純資料 workload，與這個映射相符，但不能證明是哪一個 JIT 改動造成。

### 4.2 System.Text.Json

官方文章對 .NET 11 的 JSON 改善包含更有效率的掃描、default encoder 的 `SearchValues`，以及讀取 whitespace 的向量化處理。官方展示的 micro benchmark 可達很大幅度，但那是特定的 writer escaping 或長 whitespace 輸入。

本方案的 Webhook：

- 主要使用 `JsonDocument.Parse`／reader；
- body 通常是 compact JSON；
- 回應不是大型 JSON writer workload；
- 驗簽、ArrayPool body 管理、整個 buffer zeroing、事件 envelope 建立也佔時間。

因此官方 JSON micro benchmark 不能轉換成 Webhook 整體百分比；本次 `parse_32` 約 0.99，HTTP endpoint 約 1.00，正是「局部 parser 改善被完整請求流程稀釋」的實測結果。

### 4.3 Newtonsoft.Json／Flex

Flex 路徑使用釘定的 Newtonsoft.Json 13.0.4。runtime 升級沒有替換 Newtonsoft 演算法或版本；只有 JIT、GC、字串、集合與基礎 runtime 可能影響它。`flex_20` 的中位數方向約 -5.5%，但 CI `0.6368–1.0757` 跨 1，不足以支持穩定收益。

若未來要比較 Newtonsoft → System.Text.Json，必須另立 migration benchmark；不能把那個套件遷移收益歸給 .NET 11 runtime。

### 4.4 ASP.NET Core／Kestrel／runtime-async

ASP.NET Core 11 release notes 提到：

- HTTP/1.1 malformed request parser 改為 non-throwing path，特定大量錯誤請求情境可改善 throughput；
- HTTP logging response buffering stream pooling；
- HTTP/3 首個 request 較早開始處理；
- shared-framework-only libraries 在 `net11.0+` 使用 runtime-async，可能減少部分 async／await 固定成本。

本次同一份 `net10.0` IL runtime-only 對照**不包含完整的 runtime-async target effect**。ASP.NET Core 11 的正式 target／package migration，仍需：

1. 把 host／shared libraries target 改為 `net11.0`；
2. 依 migration guide 更新 `Microsoft.AspNetCore.*`／`Microsoft.Extensions.*` package；
3. 重新檢查 API、分析器、序列化、ExecutionContext／AsyncLocal、錯誤堆疊及部署 runtime；
4. 以正式 HTTP trace 及壓測驗證。

所以「未測到 endpoint 變快」不等於「完成 net11.0 retarget 後永遠沒有 async benefit」；只是目前尚未有證據能量化那個額外 benefit。

### 4.5 ThreadPool、Timer、資源生命週期

官方也包含 ThreadPool small work item、lock／Monitor、async 等固定成本改善。這個方案對資源生命週期有嚴格要求：request body、registration、queue、timer、stream、connection 必須有界並 deterministic cleanup。本次 synthetic sink 每輪確認 `active=0`、accepted／committed 計數一致、沒有 scope error，且 host process 正常停止；但它不是 SQL／背景 worker soak test。

## 5. 哪些專案不會受益

本 solution 有 net10 Host／library、net10 + .NET Framework 雙目標，以及 net462／net48-only 專案。

- Dynamics plugins、workflows 與其他 net462／net48 執行路徑不會因 .NET 11 runtime 得到收益；它們執行於 .NET Framework／平台既定 host。
- 雙目標的 `net10.0` 分支才可能受益；Framework 分支不會。
- 釘定套件的演算法不會自動變成新版套件。
- 方案的整體平均收益不能用所有 project 數量平均；應按實際流量、CPU time、request type 加權。

## 6. 風險、統計與不能宣稱的事項

1. 8 對 micro 及 6 對 HTTP 足以揭露方向與噪音，但不是完整 production capacity plan；尤其 tree workload 有明顯 outlier。`n=8/6` 的 bootstrap 只作描述，不能把跨 1 的差異解讀成「沒有差異」，也不能把未跨 1 的 exploratory CI 解讀成已證實改善。
2. CI 是程序級配對 CI；同一程序內的迭代不是獨立樣本。
3. Balanced 電源、IDE／其他工作樹背景活動、熱降頻及本機共享資源仍可能造成變異；micro 單輪比值可跨約 `0.2–1.8`，這是本研究可信度的主要限制；沒有關閉 Defender 或停止其他工作樹。
4. 沒有正式 SQL／Dataverse／LINE／TLS／IIS trace，因此不能宣稱網站 p95、Webhook production throughput 或雲端成本會按本報告同比改善。
5. 沒有把 malformed request、HTTP/3、Zstandard、runtime-async target effect 當成已測結果。
6. 正確性測試驗證了合成 A／B 頻道隔離、錯誤簽章拒絕、竄改 body 拒絕、duplicate stable event ID 拒絕、Flex／文字輸出 hash 一致及 registration drain；這些是研究工具的離線契約，不是正式外部服務驗收。

## 7. 最終建議

### 現在

- 保持 `global.json` 指向 .NET 10 SDK 10.0.403。
- 保持現行 target framework；先完成 .NET 10 版本與 P5。
- 不因官方微基準的 20%、50% 或更高單點數字直接升級。

### 未來若要評估 .NET 11

以獨立分支執行兩階段：

1. **先做 net11.0 retarget compatibility pass**：更新 TFM／Microsoft packages，建立完整 solution、net10／net462 交叉相容、部署、錯誤通知、序列化與 isolation regression。
2. **再做 production-shaped benchmark**：用匿名化真實 request mix、Dataverse／SQL stub latency distribution、LINE client stub、valid／invalid traffic mix、HTTP/1.1／HTTP/2／HTTP/3、cold start／steady state、CPU／allocation／GC／p95／p99／soak。

只有當正式 trace 顯示 CPU 純本地段占比高，或 net11.0 runtime-async／Kestrel 變更在實際流量可辨識，才值得把升級成本轉成效能投資。

## 8. 可重現證據

研究工具：

- `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P5.Worktree\.ccg\tasks\net11-performance-research\harness\Research.cs`
- `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P5.Worktree\.ccg\tasks\net11-performance-research\harness\Workloads.cs`
- `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P5.Worktree\.ccg\tasks\net11-performance-research\run_research.py`
- `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P5.Worktree\.ccg\tasks\net11-performance-research\analyze_results.py`

原始證據：

- micro（正式 8 對）：`W:\artifacts\net11-performance-research\20261116-111329-micro`
- HTTP（正式 6 對）：`W:\artifacts\net11-performance-research\20261116-113032-http`
- 分析摘要：`W:\artifacts\net11-performance-research\analysis-final`
- `20261116-110344-micro`（4 對 pilot）及 `20261116-112800-http`（2 對 pilot）保留在 artifacts 供查核，但因樣本較少、離群值較多且已被後續正式批次取代，不納入本報告主表；`analysis-8` 同樣是早期摘要，不是最終摘要。
- Release publish／manifest：`W:\artifacts\net11-performance-research\app` 與 `W:\artifacts\net11-performance-research\harness\manifest.json`

外部模型分析狀態：依專案 self-healing entrypoint 執行兩次；Claude 兩次均產生可用分析，Gemini 分別回傳 403／503，故本研究是**單模型 fallback／外部雙模型未完成**，不能宣稱 dual-model complete。研究結論以本機可重現量測、來源檢查及保守統計為準。
