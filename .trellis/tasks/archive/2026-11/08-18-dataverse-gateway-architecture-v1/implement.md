# 執行計畫：產品 A 完整實現 Dataverse 連線架構圖（v1）

## 怎麼用這份文件

**5 個 Run，固定不變。依序 A → B → C → D → E 連續執行到完成。**

Run 之間**不要停下來等指示**：一個 Run 的品質門檻與完成判定全過、commit 完成後，
立刻接著跑下一個 Run。全部五個做完才算交付。

## 給執行者的五條硬規則

1. 只改本 Run 檔案清單裡的檔案。清單外一律不動。
2. **不准新增 Run、不准擴大範圍、不准衍生子任務。** 計畫就是這 5 個 Run。
   遇到細節上的取捨（命名、內部結構、測試寫法），**自己決定並在 `notes.md` 記錄理由**，
   不要停下來問。
3. 連續 3 次驗證失敗 → 走失敗處理程序，不要試第 4 次。
4. 發現清單外的問題 → 寫進 `notes.md` 的「範圍外發現」，絕不順手修。
5. 通過品質門檻才 commit；一個 Run 一個 commit。

## 唯三的停止條件

除下列三種情況外，**一律繼續執行到 Run E 完成**：

1. 同一個 Run 的品質門檻連續 3 次失敗
2. 必須修改白名單以外的檔案才能繼續（先在 `notes.md` 寫清楚是哪個檔案、為什麼）
3. `prd.md` 的 F1～F4 四項查證有任何一項在實作時被證明為假
   （例如發現第二處 `as OrganizationServiceProxy`、或發現 `m_OrganizationService` 其實有被指派）

停止時：`notes.md` 寫明 Run 編號、停止條件編號、完整錯誤訊息或反證，然後結束。
不要自行改設計繞過，也不要縮小範圍硬做完。

## 兩條鐵律

**一、短命物件不得 Dispose 長命物件。** 前置任務因此造成登入全面失敗。

**二、任何持有 `IOrganizationService` 的物件，都不得假設它是 raw client。**
本任務之後它一定是代理；`as` 具體型別只會得到 null（設計已驗證此為既有行為）。

## 品質門檻（每次 commit 前必須全過，輸出原文貼進 notes.md）

```bash
dotnet build SpeechMessageProducts.sln -c Debug
```

0 錯誤 0 警告。

```bash
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

ToolUtility 63 全綠；Dataverse 全綠（每 Run 只增不減）；
**MemberInfo 基線 22 失敗 / 305 通過 / 327，失敗不得 > 22、通過不得 < 305。**

**G3 繁體中文文件**：新增或實質修改的 `.cs`，其 public/internal 型別、介面、建構式、方法、
重要屬性需有完整繁中 XML 註解。擁有資源的型別須寫明：資源最大生命週期、確定性釋放路徑、
如何防跨請求洩漏。一行復述或單獨 `<inheritdoc />` 不算。

**G4 編碼**：輸出 `ENCODING OK`

```bash
python - <<'PY'
import subprocess
fs=[f for f in subprocess.run(["git","diff","--name-only","HEAD"],
    capture_output=True,text=True).stdout.split() if f.endswith(".cs")]
bad=[]
for f in fs:
    b=open(f,"rb").read()
    if b.startswith(b"\xef\xbb\xbf"): bad.append((f,"BOM"))
    if b and not b.endswith(b"\r\n"): bad.append((f,"no final CRLF"))
    try: b.decode("utf-8")
    except Exception: bad.append((f,"invalid utf-8"))
print(bad if bad else "ENCODING OK")
PY
```

**G4b 行尾**：輸出 `CRLF OK`

```bash
python - <<'PY'
import subprocess
fs=[f for f in subprocess.run(["git","diff","--name-only","HEAD"],
    capture_output=True,text=True).stdout.split() if f.endswith(".cs")]
bad=[]
for f in fs:
    b=open(f,"rb").read()
    lone=b.count(b"\n")-b.count(b"\r\n")
    if lone: bad.append((f,lone))
print(bad if bad else "CRLF OK")
PY
```

**G5 組件歸屬**（本任務特有）：新型別必須在 `ToolUtility` 組件內，B/C/D 才能引用。

```bash
grep -rln "IDataverseGateway\|IDataverseConnectionManager\|IBoundedClientPool\|IClientLease\|DataverseConnectionKey" --include=*.cs SpeechMessageProducts.ChurchReport/
```

除 `Startup.cs` 外必須 0 行。

---

## Run A — 型別契約與 Pool 核心（純新增，不接線）

完成後**系統行為完全不變**：新型別沒有任何人使用。

- [ ] `ToolUtility/Dataverse/DataverseConnectionKey.cs`
      —— `Product` / `Environment` / `OrganizationUrl` / `EffectiveIdentity`，值相等語意
- [ ] `ToolUtility/Dataverse/IClientLease.cs` ＋ `ClientLease.cs`
      —— `Service`、`MarkFaulted()`、`Dispose()` 冪等
- [ ] `ToolUtility/Dataverse/PooledClient.cs`
      —— 顯式狀態機 `Idle / Leased / Faulted / Disposed`，含最後驗證時間
- [ ] `ToolUtility/Dataverse/DataversePoolMetrics.cs`
      —— Idle / Leased / Faulted / Waiting / AcquireTimeouts / Created / Discarded
- [ ] `ToolUtility/Dataverse/DataversePoolOptions.cs`
      —— 五個參數（`MinSize` / `MaxN` / `AcquireTimeout` / `IdleTimeout` / `HealthInterval`）
- [ ] `ToolUtility/Dataverse/IBoundedClientPool.cs` ＋ `BoundedClientPool.cs`
      —— keyed 子池；`SemaphoreSlim(MaxN)`；idle 集合；idle cleanup timer；
      出借前若超過 `HealthInterval` 以健康檢查委派驗證；Faulted 一律不回池

**測試（`ToolUtility.Dataverse.Tests`，用假的 `IOrganizationService`）**

- [ ] A6：同一條 client 不可能同時被兩個 lease 持有
- [ ] A7：`MarkFaulted` 的 client 不回池，池大小正確遞減
- [ ] A8：不同 key → 不同子池；相同 key → 同一子池
- [ ] A9：超過 `MaxN` 時在 `AcquireTimeout` 內擲出明確逾時例外，Metrics 逾時計數 +1
- [ ] lease `Dispose()` 兩次不擲例外且只歸還一次

**允許修改**

```
ToolUtility/Dataverse/**            （全部新建）
ToolUtility.Dataverse.Tests/**      （新增測試）
```

**完成判定**：G1～G5 全過，加上

```bash
grep -rn "IBoundedClientPool\|DataverseConnectionKey" --include=*.cs SpeechMessageProducts.ChurchReport/
# 必須 0 行（本 Run 不接線）
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/
# 必須無輸出
```

**commit**：`feat(dataverse): 新增 Keyed Bounded Pool 與 Lease 型別契約`

---

## Run B — Manager、Gateway 與代理（純新增，不接線）

完成後**系統行為仍然完全不變**。

- [ ] `ToolUtility/Dataverse/IDataverseConnectionManager.cs` ＋ `DataverseConnectionManager.cs`
      —— Singleton；解析 Pool Key（`EffectiveIdentity` 今天取組態服務帳號）；
      唯一建立 client 的地方（呼叫 `ICrmConnectionService.CreateOnPremiseClient`）；
      健康檢查以 `WhoAmI` 實作；`GetMetrics()`；`Dispose()` 做 shutdown cleanup
- [ ] `ToolUtility/Dataverse/IDataverseGateway.cs` ＋ `DataverseGateway.cs`
      —— Scoped；`Execute` / `Execute<T>`；**reentrant 深度計數**；
      例外時 `MarkFaulted` 後 rethrow；`finally` 保證遞減與釋放
- [ ] `ToolUtility/Dataverse/GatewayOrganizationService.cs`
      —— 實作 `IOrganizationService` 全部 8 個方法，逐一委派 `_gateway.Execute(...)`
- [ ] `ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
      —— 依 `design.md` §7；無 HttpContext 時自建 scope；
      註解寫明這是 legacy 持有者的過渡橋樑與移除條件

**測試**

- [ ] A5：巢狀 `Execute` 三層只取得 1 條 lease
- [ ] Gateway 在 `work` 擲例外時 `MarkFaulted` 且不吞例外
- [ ] `GatewayOrganizationService` 的 8 個方法各自確實委派（可用計數 spy）
- [ ] A11：ambient 在無 HttpContext 時自建 scope，工作結束即釋放

**允許修改**

```
ToolUtility/Dataverse/**            （續新建）
ToolUtility.Dataverse.Tests/**
```

**完成判定**：同 Run A 的兩道「不接線」檢查仍須為 0 行／無輸出。

**commit**：`feat(dataverse): 新增 ConnectionManager、Gateway 與 per-operation 代理`

---

## Run C — 切換 DI（唯一的不可回退點）

- [ ] `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs`：
      註冊 `IBoundedClientPool`（Singleton）、`IDataverseConnectionManager`（Singleton）、
      `IDataverseGateway`（Scoped）、`IOrganizationService` → `GatewayOrganizationService`（Scoped）
- [ ] `SpeechMessageProducts.ChurchReport/Startup.cs`：
      移除 `AddScoped<IOrganizationService, PooledOrganizationService>()` 與
      `AddSingleton<CrmConnectionPool>(...)`；
      `ICrmConnectionPool` 改由薄 adapter 實作，只轉接 `GetStats()`（依 `design.md` §4）
- [ ] 刪除 `ToolUtility/ConnectionOperations/PooledOrganizationService.cs`
- [ ] 新增 `ConnectionPoolStatsAdapter`（放 `ToolUtility/Dataverse/`）

**測試**

- [ ] A10：request scope 結束後 Leased 歸零
- [ ] A12：五個參數由組態覆寫後生效
- [ ] 以 `ValidateScopes = true` + `ValidateOnBuild = true` 建出容器不擲例外
      （Manager 為 Singleton，不得注入任何 Scoped）

**允許修改**

```
ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
ToolUtility/ConnectionOperations/PooledOrganizationService.cs   （刪除）
ToolUtility/Dataverse/**
SpeechMessageProducts.ChurchReport/Startup.cs
ToolUtility.Dataverse.Tests/**
```

**完成判定**：G1～G5 全過，加上

```bash
grep -rn "PooledOrganizationService" --include=*.cs . --exclude-dir=obj --exclude-dir=bin | grep -vE ":[0-9]+:\s*(//|///)"
# 必須 0 行
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/Controllers/
# 必須無輸出（16 個 Controller 建構式零改動）
```

**commit**：`refactor(dataverse): 切換為 per-operation Gateway，淘汰 per-request 租約`

> **切換點自我驗證（取代人工回歸，因為本任務要求全自動執行到底）**
>
> 人工回歸無法由 agent 執行，因此本 Run 必須以自動化檢查補償，全部通過即續跑 Run D：
>
> - C1 以 `ValidateScopes = true` + `ValidateOnBuild = true` 建出**與 Startup 相同的**服務圖，不擲例外
> - C2 模擬 3 個並行 scope 各執行 10 次操作，結束後 Leased 歸零、池大小 ≤ MaxN
> - C3 模擬操作擲例外，確認 client 被標記 Faulted 且不回池，後續 Acquire 仍可成功
> - C4 `ToolUtilityClass` 經由 DI 取得後，其 `m_Crm2011OrganizationService` 不是 `OnPremiseClient`
>       （證明應用程式再也拿不到 raw client）
>
> 任一項不過即視為本 Run 失敗，走失敗處理程序。

---

## Run D — 清除 legacy 連線建立（圖 ⑨）

- [ ] `ToolUtilityClass.Core.cs`：刪除 `InitializeCrmConnection()`、
      刪除兩個自建連線的 legacy 建構式、刪除 `_ownsConnection` 與 `_crmConnectionService` 欄位；
      `Dispose` 不再釋放任何連線
- [ ] `ToolUtilityClass.Core.cs`：依 F3 刪除恆為 null 的 `public OrganizationServiceProxy m_OrganizationService`
- [ ] **D-a `ToolUtilityClass.ActivityAttachment.cs` 的兩處死分支**（`:57`、`:76`）

      形態如下，`CRM_TYPE` 是常數 `"DYNAMICS365-9.0"`（`Core.cs:36`），
      因此 `CRM_TYPE == "DYNAMICS365"` 是**編譯期恆假**，if 分支永遠進不去：

      ```csharp
      if (CRM_TYPE == "DYNAMICS365")
          _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_OrganizationService);   // 死分支
      else
          _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_Crm2011OrganizationService);
      ```

      改法：**刪除整個 if/else，只保留 else 的那一行呼叫。**
      這是零行為改變，且可由常數比較證明。不要改 `_facade` 或 `IActivityService` 的簽章。
- [ ] `ToolUtilityFactory.cs`：legacy 單例改為以 `AmbientGatewayOrganizationService` 建構；
      `SetConfiguration` / `SetTracer` 之外新增 `SetAmbientService`，由組合根於啟動時設定一次
- [ ] `Startup.cs`：啟動時呼叫 `ToolUtilityFactory.SetAmbientService(...)`
- [ ] 刪除 `ToolUtility/ConnectionOperations/CrmConnectionPool.cs`
      與 `ICrmConnectionPool` 的舊實作（介面保留，由 Run C 的 adapter 實作）
- [ ] 清除 `m_OrganizationService` 的 24 處死分支
      —— **只准刪除 `if (... != null)` 的死分支與其 `ref` 傳遞，不准改動 else 路徑的商業邏輯**

**測試**

- [ ] `ToolUtilityClass` 建構時不呼叫 `CreateOnPremiteClient`（沿用既有測試，確認仍成立）
- [ ] `ToolUtilityFactory.GetInstance()` 取得的單例，其操作會解析到當前 scope 的 gateway

**允許修改**

```
ToolUtility/ToolUtilityPartials/**            （Core.cs 與 ActivityAttachment.cs，見下方 D-a）
ToolUtility/Factory/ToolUtilityFactory.cs
ToolUtility/ConnectionOperations/CrmConnectionPool.cs           （刪除）
ToolUtility/ConnectionOperations/ICrmConnectionPool.cs
SpeechMessageProducts.ChurchReport/Startup.cs
SpeechMessageProducts.ChurchReport/WebServiceConnector/**        （只刪 m_OrganizationService 死分支）
SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/**      （同上）
ToolUtility.Dataverse.Tests/**
```

**完成判定**：G1～G5 全過，加上

```bash
grep -rn "m_OrganizationService" --include=*.cs . --exclude-dir=obj --exclude-dir=bin | grep -vE ":[0-9]+:\s*(//|///)"
# 必須 0 行（A2）
grep -rn "CreateOnPremiseClient" --include=*.cs ToolUtility/ToolUtilityPartials/
# 必須 0 行（A1）
```

**commit**：`refactor(dataverse): 清除 ToolUtilityClass 的 legacy 連線建立路徑`

> **legacy 路徑自我驗證（取代人工回歸），全部通過即續跑 Run E**
>
> - D1 `ToolUtilityFactory.GetInstance()` 的單例，在有 scope 時解析到該 scope 的 gateway
> - D2 同一個單例在無 HttpContext 時自建 scope，工作結束後該 scope 已釋放、Leased 歸零
> - D3 連續 100 次跨 scope 操作後，池中 client 數不成長（證明 ambient 不洩漏）
> - D4 全專案 grep `m_OrganizationService` 為 0 行
>
> 任一項不過即視為本 Run 失敗，走失敗處理程序。

---

## Run E — 參數、Metrics 與結案

- [ ] 五個參數全部外部化到 `appsettings`（含 `HealthInterval`，前置任務未外部化）
- [ ] `appsettings.Production.json` 孤立的 `ConnectionPool` 區段：**刪除**（新的 `Dataverse:Pool` 取代它），並在 `notes.md` 記錄
- [ ] Metrics 由 `BaseChurchController.cs:1063` 既有的診斷端點可讀取（不新增端點）
- [ ] 撰寫 `docs/architecture/dataverse-gateway-v1.md`：
      對照架構圖逐格說明「圖上的元件 → 實作型別 → 測試」，作為 B/C/D 套用時的依據
- [ ] 更新 `prd.md` 驗收表，逐條標註達成與證據
- [ ] `notes.md` 寫出本任務對圖上 ①～⑩ 每一格的達成狀態

**允許修改**

```
SpeechMessageProducts.ChurchReport/appsettings*.json
SpeechMessageProducts.ChurchReport/Startup.cs
ToolUtility/Dataverse/**
docs/architecture/dataverse-gateway-v1.md          （新建）
.trellis/tasks/08-18-dataverse-gateway-architecture-v1/**
```

**完成判定**：`prd.md` 的 A1～A14 全部有實際輸出佐證（A15 人工回歸除外）。

**commit**：`docs(dataverse): Gateway 架構 v1 收斂與驗收紀錄`

---

## 失敗處理程序（絕不使用無範圍 git clean）

1. `git restore -- <本 Run 清單中原已存在的檔案>`
2. `rm -f <本 Run 新建立的檔案，逐一列出路徑>`
3. `notes.md` 記錄 Run 編號、失敗原因、最後的完整錯誤訊息
4. 標記 SKIPPED 並**停止**（5 個 Run 有前後依賴，不可跳過續做）

## 本任務明確不做

- 產品 B / C / D 的實作
- `InMemoryDataContextSmallGroup` 的 13 個 session 鍵快取重新設計
- per-user impersonation 的啟用
- 明文密碼與憑證輪替
- `ToolUtilityClass` 的公開 API 重新設計
- 那 22 個既有失敗的 Payments 命名測試

## 交付

Run A～E 全部完成後，在 `notes.md` 寫一節「本任務結案」，
內容依 `codex-charter.md` 的「全部完成後」一節。

人工回歸由使用者執行，不是 agent 的完成條件；
沿用 `.trellis/tasks/08-17-toolutility-scoped-lifetime/regression-checklist.md`。

## 從 Run A 開始，一路做到 Run E
