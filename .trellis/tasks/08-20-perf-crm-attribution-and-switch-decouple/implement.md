# 執行計劃

## 順序

第 2 項（開關解耦）較小且獨立，先做可先取得一次乾淨建置。
第 1 項（CRM 歸因）動 DI 圖，風險較高，後做。
兩項都完成後才進行單次實跑驗收。

---

## Step 1：Session 開關解耦

      - 新增 `public bool SessionVerbose { get; }`
      - 建構式增加對應參數；`Create(directory, enabled)` 既有簽章若被外部呼叫，
        必須維持相容（新增多載或以預設值處理），不得破壞既有呼叫端
      - `FromConfiguration`：讀 `section.GetValue("SessionVerbose", false)`，
        以 `allowEnabled && configuredSessionVerbose` 收斂
      - `CreateDisabled`：`SessionVerbose` 為 `false`
      改為指派 `_diagnosticTraceOptions.SessionVerbose`（157 行的 `ProfilingSwitch` 不動）
      （`appsettings.Development.json` 不加此鍵）

## Step 2：CRM 歸因修復

      在 ToolUtility DI 擴充方法**之後**、`#if DEBUG` 區塊內，
      沿用 425–451 行既有 descriptor 置換模式，將 `IOrganizationService`
      置換為回傳 `new TimedOrganizationService(inner, http)` 的工廠，
      **維持原 `Lifetime`（Scoped）**
      （若判定移除風險過高，改為在檔頭註解標記失效並說明理由——二選一，不得沉默保留）
      斷言其型別為 `TimedOrganizationService`，且 `Inner` 不為 null

## Step 3：全量檢查

      SHA-256 仍為 `C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B`
      且首 3 bytes 仍為 `EF BB BF`

## Step 4：實跑收集 trace（AC-1 / AC-2 的唯一證據來源）

**執行前置（順序不可顛倒）**

      **改名移走**（例：加 `.before` 後綴）。**不要清空**——兩者皆為 Append 模式
      `curl.exe -s -o /dev/null -w "%{time_total}s\n" https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
      （已知冷啟動約 4.0s、暖機後約 0.05s）

**執行**

      例如 `/SmallGroup/IntegrateView` 或 `/MemberInfo/Detail`）
      否則不會觸發 flush 與 `Cleaning up trace listener`，AutoFlush 那段就驗不到

**收集**

      產生新的 `ChurchReport-Trace-Report.md`

| 指標 | 值 |
|---|---|

- [ ] 4.10 挑 **3 個**有 CRM 活動的請求，逐一列出
      `traceId` / `[Perf] crm.n` / JSONL `crmCount` / `[Perf] crm.ms` / JSONL `crmMs`，

- [ ] 4.11 再跑一次：把 `DiagnosticsTrace:SessionVerbose` 設為 `true`，
      確認四個 Session 標籤重新出現（AC-2 後半）。驗完改回 `false`
      （未達成：true 重跑觀測 `[InMemoryDataContext]` 1 次，其餘三個標籤 0；來源設定已恢復 false）

## Step 5：交接


---

## 回滾點

- Step 1 完成後可獨立 commit（低風險）
- Step 2 若造成 DI 解析失敗或啟動例外，單獨還原 Step 2 的改動即可，
  Step 1 不受影響

## 誠實回報要求

- 任何 AC 未達成，直接寫「未達成」並附實際數字。**不得調整門檻或改寫驗收條件來湊過。**
- 若 `[Perf] crm.n` 與 JSONL `crmCount` 出現系統性落差，如實回報落差幅度，
  並說明是否存在不經 `IOrganizationService` 的 CRM 路徑（design.md 已列為已知限制）。
- 若外部審查工具（Gemini / Claude reviewer）執行失敗，回報實際 stderr，
  **不得宣稱「雙模型審查通過」**。
  已知既有故障：runner 以空值傳入 `--setting-sources` 導致 `claude` exit 1，
  此為工具問題，非本任務範圍。
