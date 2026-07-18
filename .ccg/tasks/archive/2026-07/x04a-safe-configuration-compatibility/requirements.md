# X04A 安全設定相容性修復：需求

## 目標

解除 Wave 2 中 X04A-SEC-001 與 X04A-SEC-002 的阻擋，同時保證每一個
舊式 runtime consumer 都從 ASP.NET Core host 的有效設定取得值，而不是重新
讀取 base `appsettings.json`。

## 已確認事實

- 先前修復移除提交的 secret 後，唯讀審核發現舊 consumer 仍只讀取 base
  設定，會在 Production 取得空值。
- 已盤點 13 個 ad-hoc `ConfigurationBuilder` consumer；它們都沒有載入
  Production overlay 或環境變數 provider。
- `Program` 已透過 `WebApplication.CreateBuilder(args)` 建立正確的 host
  `IConfiguration`，且 `Startup` 已將同一份設定交給 `ToolUtilityFactory`。
- 既有 consumer 大量保留無參數建構式、static 欄位和直接 `new` 的 legacy
  呼叫，全面 DI 重構不適合作為解除本次 P0 blocker 的最小修復。

## 必要結果

1. 所有 13 個已盤點 consumer 不再自行建立 `ConfigurationBuilder` 或讀取
   `appsettings.json`。
2. 它們都取得由 host 建構、包含環境變數與 Production overlay 的同一份
   `IConfiguration`。
3. X04A-SEC-001 的 21 個提交 secret literal 可安全清空，且既有 LINE、CRM
   與 payment runtime 路徑仍可取得部署期注入的 synthetic test values。
4. X04A-SEC-002 的 Production 安全控制與 secret manifest 於 consumer 可用前
   完成驗證；設定未初始化時不得靜默回退到檔案設定。
5. `X04A-PERF-001` 以「13 個 product runtime ad-hoc builder 為 0」作為本次
   必要前置量測，正式納入修訂後的 Wave 2 X04A 範圍。
6. bridge 只可由 host 初始化一次；不得由 legacy consumer、測試以外的程式碼或
   reload callback 替換有效設定。

## 非目標

- 不在此修訂中進行完整 constructor/DI graph 重寫。
- 不旋轉或回填任何真實 credential。
- 不改變 LINE、CRM 或 payment 業務流程。
- 不處理未盤點的設定消費端；若掃描發現新 consumer，合同必須先回到規劃。

## 驗收

- source contract test 覆蓋 13 個路徑，驗證沒有 `new ConfigurationBuilder`、
  `AddJsonFile("appsettings.json")` 或本機 configuration cache。
- bridge contract test 證明 host/in-memory overlay 的值可被 consumer path 讀取，
  未初始化時拋出不含 secret 的明確例外。
- lifecycle test 證明第二次不同設定初始化會被拒絕；測試 fixture 不依賴工作目錄
  或 process environment 中的設定值。
- X04A secret scanner 為 `0/21`；Production eight-control matrix 為 `8/8`。
- focused tests、ChurchReport build、allowlist、`git diff --check` 與 Claude-only
  review（無輸出時唯一 Codex fallback）皆通過。
