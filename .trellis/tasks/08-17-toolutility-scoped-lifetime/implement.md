# 執行計畫：ToolUtilityClass 改為 Scoped 並抽離追蹤資源

## 怎麼用這份文件

**一次只做一個 Run。上一個 Run 沒收尾，不准開下一個。**

每個 Run 都有：檔案白名單、可執行的完成判定、上限。缺一不可。

## 給執行者的四條硬規則

1. 只改本 Run 檔案清單裡的檔案。清單外一律不動。
2. 連續 3 次驗證失敗 → 走失敗處理程序，不要試第 4 次。
3. 發現清單外的問題 → 寫進 `notes.md`，絕不順手修。
4. 通過品質門檻才 commit；一個 Run 一個 commit。

## 一條鐵律（前置任務的教訓，代價很高）

**短命物件不得 Dispose 長命物件。**

per-request 的 Controller、operation 範圍的工具類別，絕對不可以呼叫程序級單例的
`Dispose()`。前置任務就是因為這個錯誤造成登入全面失敗。

本任務新增任何 `Dispose` 呼叫前，先確認呼叫端的生命週期**不短於**被釋放的物件。

## 失敗處理程序（絕不使用無範圍 git clean）

1. `git restore -- <本 Run 清單中原已存在的檔案>`
2. `rm -f <本 Run 新建立的檔案，逐一列出路徑>`
3. `notes.md` 記錄 Run 編號、失敗原因、最後錯誤訊息
4. 標記 SKIPPED，**停止**（本任務三個 Run 彼此有前後依賴，不可跳過續做）

## 品質門檻（每次 commit 前必須全過）

```bash
# G1 建置
dotnet build SpeechMessageProducts.sln -c Debug

# G2 測試（67 個）
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

**G3 繁體中文文件**：新增或實質修改的 `.cs`，其 public/internal 型別、介面、
建構式、方法、重要屬性需有完整繁中 XML 註解。一行復述或單獨 `<inheritdoc />` 不算。
擁有資源的型別須寫明：資源最大生命週期、確定性釋放路徑、如何防跨請求洩漏。

**G4 編碼**：必須輸出 `ENCODING OK`

```bash
python - <<'PY'
import subprocess
fs=[f for f in subprocess.run(["git","diff","--name-only","HEAD"],
    capture_output=True,text=True).stdout.split() if f.endswith(".cs")]
bad=[]
for f in fs:
    b=open(f,"rb").read()
    if b.startswith(b"\xef\xbb\xbf"): bad.append((f,"BOM"))
    if b"\n" in b and b"\r\n" not in b: bad.append((f,"LF-only"))
    if b and not b.endswith(b"\r\n"): bad.append((f,"no final CRLF"))
    try: b.decode("utf-8")
    except Exception: bad.append((f,"invalid utf-8"))
print(bad if bad else "ENCODING OK")
PY
```

---

## Run 0 — 調查（不改任何 .cs）

回答兩題（PRD 的 Q1／Q2），產出
`research/findings-scope-boundaries.md`：

**Q1** 哪些使用 `ToolUtilityClass` 的路徑**沒有 DI scope**？
逐一列出：背景執行緒、`Task.Run`、`IHostedService`、計時器、靜態進入點。
給出檔案與行號。

**Q2** `WeeklyReportProcessor`、`RecurringDonationPaymentProcessor`、
`LineNotifyUtility` 等工具類別，是由 Controller 的請求路徑觸發，
還是由背景排程觸發？

查 3 輪仍無結論 → 寫「未確認」＋卡點，不要猜。

**允許修改**：只能新增
`.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-scope-boundaries.md`

**完成判定**：兩題都有答案；`git status --porcelain` 除該檔外乾淨。
本 Run 免 G1～G4。

**commit**：`research(toolutility): 盤點無 DI scope 的使用路徑`

---

## Run 1 — 抽離追蹤資源（行為不變）

`ToolUtilityClass` **仍維持單例**，本 Run 純粹搬移職責，行為零變化。

- [ ] 新增 `ToolUtility/Diagnostics/IToolUtilityTracer.cs`
- [ ] 新增 `ToolUtility/Diagnostics/FileToolUtilityTracer.cs`
      擁有 `FileStream` / `StreamWriter` / `TextWriterTraceListener`；
      建構時 `Trace.Listeners.Add(...)` **恰好一次**；
      `Dispose` 時 `Trace.Listeners.Remove(...)` 並釋放串流
- [ ] `ToolUtilityClass` 移除 `m_TraceLogFile`、三個 `Lazy<>`、`TRACE_DIRECTOR`、
      `InitializeTracing()`；`TraceByLevel` 改為委派給 tracer，**簽章不變**
- [ ] `TraceByLevelStatic` 依 `design.md` §4 選項 A 處理，並在註解寫明為刻意例外
- [ ] `Startup.cs` 註冊 `services.AddSingleton<IToolUtilityTracer, FileToolUtilityTracer>()`
- [ ] 新增測試（放 `ToolUtility.Dataverse.Tests`）：
      建立 100 個 tracer 消費者後 `Trace.Listeners.Count` 不成長

**允許修改**：
```
ToolUtility/Diagnostics/IToolUtilityTracer.cs          （新建）
ToolUtility/Diagnostics/FileToolUtilityTracer.cs       （新建）
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
SpeechMessageProducts.ChurchReport/Startup.cs
ToolUtility.Dataverse.Tests/**                          （新增測試）
```

**完成判定**：G1～G4 全過，加上
```bash
grep -c "TraceByLevel" ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
# 方法仍存在（簽章未變）
grep -rn "Lazy<FileStream>\|Lazy<TextWriterTraceListener>" ToolUtility/ToolUtilityPartials/
# 必須 0 行
```

**commit**：`refactor(toolutility): 追蹤資源抽離為程序級 Singleton`

> ⛔ 審查閘門：於測試環境確認 `D:\除錯追蹤\CHURCH_REPORT_TRACE.TXT` 仍正常寫入
> 且**無重複行**，再進入 Run 2。

---

## Run 1.5 — 移除跨請求的持有者（Run 2 的硬性前置）

> 依 Run 0 的調查結果新增。原規劃未預見此阻礙。
> 詳見 `research/findings-scope-boundaries.md`。

若不先做這一步，Run 2 會直接重現前置任務的登入失敗（`ObjectDisposedException`），
並造成跨請求共用連線。

- [ ] **移除 Session 鍵快取**：`Models/InMemoryDataContextSmallGroup.cs:1293` 的
      `ToolUtilityClass` 屬性，目前把實例存入程序級 `IMemoryCache`
      （鍵 = SessionId，存活 30 分鐘）。改為直接回傳注入的實例。
      理由：快取一個取得成本近乎為零的 DI 服務沒有收益，卻讓 request 範圍物件
      跨請求存活 30 分鐘。
- [ ] **修正兩處 fire-and-forget**，於 lambda 內自建 scope：
      - `Controllers/PersonalController.cs:971`
      - `Controllers/SmallGroupController/SmallGroupController.Save.cs:84`

      形式：
      ```csharp
      _ = Task.Run(async () =>
      {
          using var scope = _scopeFactory.CreateScope();
          var toolUtility = scope.ServiceProvider.GetRequiredService<ToolUtilityClass>();
          // ... 背景工作 ...
      });
      ```
      兩個 Controller 需注入 `IServiceScopeFactory`。
- [ ] 新增測試：背景 scope 於工作結束時被釋放，且不與請求 scope 共用連線

**允許修改**：
```
SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs
SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
ToolUtility.Dataverse.Tests/**
```

**完成判定**：G1～G4 全過，加上
```bash
grep -n "_ToolUtilityClass\"" SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
# 必須 0 行（Session 鍵快取已移除）
grep -c "CreateScope()" SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
# 兩者皆須 >= 1
```

**commit**：`fix(toolutility): 移除跨請求持有者，背景工作自建 DI scope`

> ⛔ 審查閘門：此時 `ToolUtilityClass` 仍是單例，行為應與現況一致。
> 需完成人工回歸（登入、小組週報儲存、個人資料批次上傳）再進 Run 2。

---

## Run 2 — 改變生命週期

- [ ] `ToolUtilityClass` 建構式改為接收 `IOrganizationService`、`IToolUtilityTracer`、
      `IConfiguration`；移除 `InitializeCrmConnection()` 的自行建立連線
- [ ] `Startup.cs`：`services.AddScoped<ToolUtilityClass>()`；
      `IToolUtilityProvider` 由 Singleton 改為 **Scoped**
- [ ] `ToolUtilityProvider.GetToolUtility()` 回傳注入的 Scoped 實例，
      不再呼叫 `ToolUtilityFactory.GetInstance()`
- [ ] 新增測試：`ToolUtilityClass` 建構時不呼叫 `CreateOnPremiseClient`

**允許修改**：
```
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
ToolUtility/DependencyInjection/ToolUtilityProvider.cs
ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
SpeechMessageProducts.ChurchReport/Startup.cs
ToolUtility.Dataverse.Tests/**
```

**完成判定**：G1～G4 全過。`BaseChurchController` 的呼叫端不應有任何改動
（`git diff --stat` 確認）。

**commit**：`refactor(toolutility): ToolUtilityClass 改為 request 範圍`

> ⛔ 審查閘門：完整人工回歸（登入、會友、奉獻、影像上傳、LINE 綁定）通過後再進 Run 3。

---

## Run 2.5 / Run 3 — 依持有者生命週期遷移

Run 3.0 調查確認 39 處文字出現中有 35 處實際呼叫，分類為 A 7、B 19（含
`InMemoryDataContextSmallGroup` 的 legacy static getter）、C 9。原本按目錄切分會把
per-request、session cache 與未確認死碼混在一起；在沒有先處理 B 類 holder 的情況下，直接
注入 scoped `ToolUtilityClass` 會重現 `ObjectDisposedException` 與跨請求共用連線。
詳細證據與兩種 B 類設計見 `research/findings-run3-holder-lifetimes.md`。

**Run 2.5 — C 類與設計閘門（先於任何呼叫點遷移）**

- 逐一處理 7 個 C 類：`DedicationInfo`、`EquipmentStatusCalculator`、`HappyGroupUtility`、
  `LineBindingUtility`、`RegisterConnector`、`UploadData`、`WebServiceConnector`。每一個
  必須找到實際入口並指定 request/scoped 依賴，或確認為死碼並建立移除票；未確認者不得
  直接改成 constructor injection。
- 在 13 個 session cache model 上完成方向選擇與狀態保留矩陣：方向 1（方法參數傳遞）或
  方向 2（先移除 session key cache）。未選定前不得進入 B 類實作。

**Run 2.5 完成判定**：C 類皆有明確處置；方向已選定；沒有新增
`ToolUtilityFactory.GetInstance`；沒有 scoped ToolUtility 或包含它的 connector 寫入
`IMemoryCache`；研究輸出與阻擋項寫入 notes。

**Run 3-A — A 類 request holder**

遷移 `DonationFeePaymentProcessor`、`RecurringDonationPaymentProcessor`、四個 QR utility
與 `GalleryViewModel` 共 7 處。`InMemoryDataContextSmallGroup.ToolUtilityClass` 只有在
legacy Factory getter 改為注入的 scoped 實例後，才能加入本批；目前不得把它當成安全 A 類。

**Run 3-A 完成判定**：本批 `ToolUtilityFactory.GetInstance` 為 0；建構式/Controller 只
持有當前 request 的 scoped 服務；async 工作在 scope 結束前完成；沒有 scoped instance
寫入 cache；G1、G4 與聚焦測試輸出原文寫入 notes。

**Run 3-B — B 類前置與直接 holder**

先依選定方向處理 `InMemoryDataContextSmallGroup` 與直接 holder：
`DonationPaymentManager`、`EquipmentDataManager`、`ListManagementDataManager`、
`PollManager`，並處理 13 個 cache entry。方向 1 先把 ToolUtility 從 Controller/workflow
沿方法鏈傳入；方向 2 先完成 model 重建與跨請求狀態轉移。

**Run 3-B 完成判定**：`IMemoryCache` 不再持有 scoped ToolUtility 或含有它的 connector；
本批所有 Factory 呼叫為 0；跨請求 A/B 隔離與 dispose/lifecycle 測試通過。

**Run 3-C — B 類傳遞鏈 connector**

按完整 holder chain 分批遷移：

1. `ListManager` 鏈：`DownloadListManager`、`DownloadIntegrateData`、`UploadIntegrateData`、
   `WeeklyReportRecord`。
2. 付款/通知鏈：`DonationPaymentProcessor`、`LineNotifyUtility`。
3. 其餘 cache 鏈：`ChurchListDataProcessor`、`DownloadHappyGroup`、`DownloadEquipment`、
   `FeeDownUpLoader`、`AppointmentsDownUpLoader`、`NewPerson`、`PersonalInfomatioManager`、
   `WeeklyReportManager`。

每次只處理一條完整鏈，避免下層已改為 scoped 而上層仍把它放進 cache。每小批一個 commit。

**Run 3-C 完成判定**：該鏈 Factory 呼叫為 0；所有 async/background 工作有明確 scope；
沒有 `ObjectDisposedException` 或跨 request connection reuse；G1、G4、聚焦測試與
`ChurchReport.MemberInfo.Tests` 的 22 失敗 / 304 通過基準不惡化。

**Run 3-D — 全域清理（最後才做）**

所有 A/B/C 可遷移路徑完成後，才移除 `ToolUtilityFactory` 的 static singleton/legacy
建構式。完成判定：

```bash
grep -rn "ToolUtilityFactory.GetInstance" --include=*.cs SpeechMessageProducts.ChurchReport   # 0 行
grep -rn "m_Crm2011OrganizationService" --include=*.cs SpeechMessageProducts.ChurchReport | grep -v "///"   # 0 行
```

全域 grep 兩條均為 0；G1-G4 與隔離/生命週期測試通過；每個生命週期批次均有獨立
commit，且不修改調查白名單外檔案。

---

## 本任務明確不做

- 明文密碼與憑證輪替（使用者已表示現況可接受）
- `appsettings.Production.json` 孤立的 `ConnectionPool` 區段
- `ToolUtilityClass` 的 API 重新設計
- 產品 B / C / D

## 目前可以開始的只有 Run 0
