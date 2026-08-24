# 執行計劃

## 順序

第 2 項（開關解耦）較小且獨立，先做可先取得一次乾淨建置。
第 1 項（CRM 歸因）動 DI 圖，風險較高，後做。
兩項都完成後才進行單次實跑驗收。

---

## Step 1：Session 開關解耦

- [x] 1.1 `ToolUtility/Diagnostics/DiagnosticTraceOptions.cs`
      - 新增 `public bool SessionVerbose { get; }`
      - 建構式增加對應參數；`Create(directory, enabled)` 既有簽章若被外部呼叫，
        必須維持相容（新增多載或以預設值處理），不得破壞既有呼叫端
      - `FromConfiguration`：讀 `section.GetValue("SessionVerbose", false)`，
        以 `allowEnabled && configuredSessionVerbose` 收斂
      - `CreateDisabled`：`SessionVerbose` 為 `false`
- [x] 1.2 `SpeechMessageProducts.ChurchReport/Startup.cs:158`
      改為指派 `_diagnosticTraceOptions.SessionVerbose`（157 行的 `ProfilingSwitch` 不動）
- [x] 1.3 `appsettings.json` 的 `DiagnosticsTrace` 補 `"SessionVerbose": false`
      （`appsettings.Development.json` 不加此鍵）
- [x] 1.4 `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs` 補測（見 design.md 三）
- [x] 1.5 驗證：`dotnet build -c Debug` 0 error

## Step 2：CRM 歸因修復

- [x] 2.1 `SpeechMessageProducts.ChurchReport/Startup.cs`
      在 ToolUtility DI 擴充方法**之後**、`#if DEBUG` 區塊內，
      沿用 425–451 行既有 descriptor 置換模式，將 `IOrganizationService`
      置換為回傳 `new TimedOrganizationService(inner, http)` 的工廠，
      **維持原 `Lifetime`（Scoped）**
- [x] 2.2 移除 `TimedToolUtilityProvider` 型別及其 `Startup.cs` 註冊區塊
      （若判定移除風險過高，改為在檔頭註解標記失效並說明理由——二選一，不得沉默保留）
- [x] 2.3 補 DI 組裝測試：從建好的 `ServiceProvider` 解析 `IOrganizationService`，
      斷言其型別為 `TimedOrganizationService`，且 `Inner` 不為 null
- [x] 2.4 驗證：`dotnet build -c Debug` 與 `-c Release` 皆 0 error

## Step 3：全量檢查

- [x] 3.1 `dotnet test ToolUtility.Dataverse.Tests`（基準 58，不得低於）
- [x] 3.2 `dotnet test ToolUtility.Tests`（基準 63，不得低於）
- [x] 3.3 `git diff --check` 無輸出
- [x] 3.4 本次改動的 `.cs` 檔：UTF-8 without BOM + 純 CRLF + 檔尾 CRLF
- [x] 3.5 確認 `Analyze-ChurchReportTraces.ps1` **未被更動**：
      SHA-256 仍為 `C131E43EB048B8904DF51CDFD601407E6286B0DC61E45949D52C21A292D7302B`
      且首 3 bytes 仍為 `EF BB BF`

## Step 4：實跑收集 trace（AC-1 / AC-2 的唯一證據來源）

**執行前置（順序不可顛倒）**

- [x] 4.1 確認應用程式行程未在執行
- [x] 4.2 將 `D:\除錯追蹤\Trace.log` 與 `D:\除錯追蹤\dataverse-trace.jsonl`
      **改名移走**（例：加 `.before` 後綴）。**不要清空**——兩者皆為 Append 模式
- [x] 4.3 先對 CRM 端點送一次暖機請求，避免把冷啟動誤記成程式問題：
      `curl.exe -s -o /dev/null -w "%{time_total}s\n" https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc`
      （已知冷啟動約 4.0s、暖機後約 0.05s）

**執行**

- [x] 4.4 以 Debug 組態啟動應用程式
- [x] 4.5 登入一次（09:02 authenticated 實跑已完成）
- [x] 4.6 操作多個會觸發 CRM 查詢的頁面：`/SmallGroup/IntegrateView`、
      `/MemberInfo/Index` 與其 `LoadDistrictTree` 資料載入。
      例如 `/SmallGroup/IntegrateView` 或 `/MemberInfo/Detail`）
- [x] 4.7 **正常關閉應用程式**（不可直接砍行程）——
      否則不會觸發 flush 與 `Cleaning up trace listener`，AutoFlush 那段就驗不到

**收集**

- [x] 4.8 執行 `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1`
      產生新的 `ChurchReport-Trace-Report.md`

- [x] 4.9 記錄下列原始數字（**不得估算，取不到就寫「未取得」**）：

| 指標 | 值 |
|---|---|
| 09:02 `SessionVerbose=false` Trace.log 總行數 | 274 |
| `Trace listener initialized` 出現次數 | 1 |
| `[Perf]` 行數 | 16 |
| `crm{n=0,ms=0}` 行數 | 9 |
| `crm{n=` 非零行數 | 7 |
| `[Perf-Gap]` 行數 | 2 |
| `[GetCurrentSessionId]` 行數（AC-2 應為 0） | 0 |
| `[GenerateCurrentRequestFingerprint]` 行數（AC-2 應為 0） | 0 |
| `[SetSessionDirtyFlag]` 行數（AC-2 應為 0） | 0 |
| `[InMemoryDataContext]` 行數（AC-2 應為 0） | 0 |
| `Cleaning up trace listener` 出現次數 | 1 |
| JSONL `request.end` 的 `crmCount` 總和 | 46 |
| `[Perf]` 的 `crm.n` 總和 | 46 |
| `[Perf]` 的 `crm.ms` 總和 | 9,571 |
| JSONL `request.end` 的 `crmMs` 總和 | 5,026 |
| JSONL 完全重複額外行數 | 0 |

- [x] 4.10 挑 **3 個**有 CRM 活動的請求，逐一列出
      `traceId` / `[Perf] crm.n` / JSONL `crmCount` / `[Perf] crm.ms` / JSONL `crmMs`，

| path | traceId | Perf crm.n | JSONL crmCount | Perf crm.ms | JSONL crmMs |
|---|---|---:|---:|---:|---:|
| `/Home/ProcessLogin` | `0HNNV3PMLLIEV:00000002` | 30 | 30 | 5,287 | 2,150 |
| `/SmallGroup/IntegrateView/{LoginParameter}` | `0HNNV3PMLLIF5:00000001` | 10 | 10 | 3,322 | 2,671 |
| `/MemberInfo/LoadDistrictTree` | `0HNNV3PMLLIF7:0000000E` | 2 | 2 | 834 | 77 |

- [x] 4.11 再跑一次：把 `DiagnosticsTrace:SessionVerbose` 設為 `true`，
      確認四個 Session 標籤重新出現（AC-2 後半）。驗完改回 `false`
      （以程序環境變數重跑；四個標籤依序為 899／621／44／8；來源 `appsettings.json`
      維持 `SessionVerbose=false`，未修改。）

## Step 5：交接

- [x] 5.1 產出報告，包含 Step 4.9 / 4.10 的**原始數字**
- [x] 5.2 明確標示哪些 AC 通過、哪些未通過、哪些未取得
- [x] 5.3 依使用者確認，僅可提交移除誤納入 `.ccg/dual-model-runs/` 的暫存檔；
      所有程式碼與設定檔改動均維持未提交，待使用者確認。


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
