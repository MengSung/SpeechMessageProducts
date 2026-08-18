Active task: .trellis/tasks/08-17-toolutility-scoped-lifetime

你要執行 **Run 2 — 改變 ToolUtilityClass 的生命週期**。這是一次性交付，沒有人會在中途
給你補充指示。所有你需要的判斷依據都在這份提示詞與下列文件中，自己讀、自己驗證。

## 0. 先讀（不可略過）

```
.trellis/tasks/08-17-toolutility-scoped-lifetime/prd.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/design.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/implement.md      ← Run 2 章節
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
.trellis/tasks/08-17-toolutility-scoped-lifetime/research/findings-scope-boundaries.md
```

實際要改的程式碼，動手前逐一讀完：

```
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
ToolUtility/Factory/ToolUtilityFactory.cs
ToolUtility/Core/ToolUtilityFacade.cs
ToolUtility/DependencyInjection/ToolUtilityProvider.cs
ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
SpeechMessageProducts.ChurchReport/Startup.cs        ← 特別是 360-500 行
```

## 1. 現況（已驗證，可直接採信）

- Run 0 / Run 1 / Run 1.5 皆已完成。Run 1.5 尚未 commit，工作區有 4 個 .cs 改動。
- `IOrganizationService` 已註冊為 **Scoped**：`Startup.cs:366`
  `services.AddScoped<IOrganizationService, PooledOrganizationService>();`
  `PooledOrganizationService` 在建構時向連線池「租借」，在 scope 結束時由 DI 釋放並歸還租約。
- `IToolUtilityTracer` 已是 Singleton，`ToolUtilityFactory.SetTracer()` 於啟動時設定一次。
- `IToolUtilityProvider` 目前註冊為 **Singleton**（`ServiceCollectionExtensions.cs:35`），
  `ToolUtilityProvider.GetToolUtility()` 內容就是 `return ToolUtilityFactory.GetInstance();`。
- `ToolUtilityClass` 目前的三個建構式：
  - `internal ToolUtilityClass(IConfiguration, IToolUtilityTracer)`
  - `internal ToolUtilityClass(String DiscoveryServiceType, IConfiguration, IToolUtilityTracer)`
  - `public ToolUtilityClass(ref bool ValidFlag)` ← 老舊授權檢查用，未初始化任何東西
  前兩者都呼叫 `InitializeCrmConnection()` 自行建立連線。
- `SpeechMessageProducts.ChurchReport` 內仍有 **35 個** `ToolUtilityFactory.GetInstance(...)`
  呼叫點（多為欄位初始化式）。**它們要到 Run 3 才遷移，Run 2 期間必須繼續正常運作。**

## 2. 六個必踩的雷（實作前先把每一個想清楚，這是本 Run 失敗的主要來源）

### 雷 1：不能在 Startup.cs 用 `services.AddScoped<ToolUtilityClass>()`

`ToolUtilityClass` 的可用建構式是 `internal`，`SpeechMessageProducts.ChurchReport` 是不同組件看不到；
而唯一 `public` 的 `ToolUtilityClass(ref bool)` 帶 `ref` 參數，`ActivatorUtilities` 無法滿足，
會在第一次解析時擲出執行期例外（建置階段不會報錯，你會以為過了）。

**做法**：註冊寫在 **ToolUtility 組件內**的 `ServiceCollectionExtensions.AddToolUtility()`，
用明確的 factory lambda，例如：

```csharp
services.AddScoped(sp => new ToolUtilityClass(
    sp.GetRequiredService<IOrganizationService>(),
    sp.GetRequiredService<IToolUtilityTracer>(),
    sp.GetRequiredService<IConfiguration>()));
```

### 雷 2：Scoped 的 ToolUtilityClass 絕對不可以 Dispose 注入進來的 IOrganizationService

`Dispose(bool)` 目前會 `(m_Crm2011OrganizationService as IDisposable)?.Dispose()`。
注入進來的 `PooledOrganizationService` 由 **DI 容器擁有**，DI 會在 scope 結束時自己釋放它。
若 `ToolUtilityClass` 也去釋放，就是重複釋放 + 短命物件釋放非自己擁有的物件 ——
**前置任務的登入全面失敗就是這個錯誤造成的。**

**做法**：加一個 `private readonly bool _ownsConnection` 旗標。

- 自建連線的 legacy 建構式 → `true`，Dispose 時釋放連線
- 注入連線的 DI 建構式 → `false`，Dispose 時**不碰**連線

`_crmConnectionService` 同理：DI 建構式不得 `new CrmConnectionService()`，Dispose 也不得釋放它。

### 雷 3：`ToolUtilityFacade` 的子服務是否會反手釋放共用的 IOrganizationService

`Dispose(bool)` 會呼叫 `_facade?.Dispose()`，而 `ToolUtilityFacade.Dispose`（`ToolUtilityFacade.cs:102-130`）
會逐一釋放約 20 個 lazy 子服務。**你必須實際讀過那些子服務的 Dispose**，確認沒有任何一個
去 Dispose 建構時傳入的 `IOrganizationService`。若有，同樣要用「不擁有就不釋放」修掉。
查完的結論寫進 `notes.md`，不要略過這一項。

### 雷 4：ToolUtilityFactory 的單例不得從 DI 取得 Scoped 連線

那 35 個呼叫點在 Run 3 前還在。Factory 產出的是**程序級單例**，若讓它持有一份 Scoped 的
`PooledOrganizationService`，等於永久扣住一份池租約（captive dependency），比現況更糟。

**做法**：保留 Factory 現有的「自行建立連線」路徑，改用新的 legacy 建構式，
並在 XML 註解中明確標記為 Run 3 將移除的過渡路徑。Factory 不得接觸 DI 容器。

### 雷 5：`IToolUtilityProvider` 由 Singleton 改為 Scoped 會產生 captive dependency

任何 **Singleton** 服務若注入 `IToolUtilityProvider`，改成 Scoped 後就是 captive dependency。
目前的消費端我看到的都是 Controller（Scoped），但**你必須自己完整驗證**，不要目測。

**做法（機械可驗）**：在建立 Host 時開啟範圍驗證，跑一次啟動：

```csharp
.UseDefaultServiceProvider((ctx, opt) =>
{
    opt.ValidateScopes = true;
    opt.ValidateOnBuild = true;
})
```

若這會改到 `Program.cs`（不在白名單內），改用等效做法：寫一個測試，
用與 `Startup.ConfigureServices` 相同的註冊建出 `ServiceProvider`，
以 `ValidateScopes = true` + `ValidateOnBuild = true` 建置並斷言不擲例外。

`Startup.cs` 內 `#if DEBUG` 的 `TimedToolUtilityProvider` 裝飾器使用
`providerDescriptor.Lifetime`，會自動跟著改成 Scoped，不需另外處理 —— 但要確認。

### 雷 6：`ToolUtilityFactory.ResetInstance()` 會 `_instance.Dispose()`

那是 Factory 自己擁有的 legacy 單例，釋放是對的。但要確認它**永遠不會**拿到 DI 建立的
Scoped 實例。兩條路徑要在型別層面分開，不要共用靜態欄位。

## 3. 要做的事

- [ ] `ToolUtilityClass` 新增 DI 建構式：接收 `IOrganizationService`、`IToolUtilityTracer`、
      `IConfiguration`；**不呼叫** `InitializeCrmConnection()`；`_ownsConnection = false`
- [ ] 保留 legacy 建構式（自建連線，`_ownsConnection = true`）供 `ToolUtilityFactory` 使用，
      XML 註解標明為 Run 3 移除的過渡路徑
- [ ] `Dispose(bool)` 依 `_ownsConnection` 決定是否釋放連線與 `_crmConnectionService`
- [ ] `ServiceCollectionExtensions.AddToolUtility()`：
      `services.AddScoped(sp => new ToolUtilityClass(...))`；
      `IToolUtilityProvider` 由 `AddSingleton` 改為 `AddScoped`
- [ ] `ToolUtilityProvider` 改為建構式注入 `ToolUtilityClass`，`GetToolUtility()` 回傳它，
      **不再呼叫** `ToolUtilityFactory.GetInstance()`
- [ ] 雷 3 的調查結論寫進 `notes.md`
- [ ] 新增測試（放 `ToolUtility.Dataverse.Tests`）：
      1. 以假的 `IOrganizationService` 建構 `ToolUtilityClass`，斷言建構過程中
         `ICrmConnectionService.CreateOnPremiseClient` **未被呼叫**
      2. 該實例 `Dispose()` 後，注入的 `IOrganizationService` **未被 Dispose**
      3. 以 `ValidateScopes = true` + `ValidateOnBuild = true` 建置服務容器不擲例外

## 4. 檔案白名單

```
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs
ToolUtility/Factory/ToolUtilityFactory.cs                      ← implement.md 原本漏列，雷 4 必須改
ToolUtility/DependencyInjection/ToolUtilityProvider.cs
ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
SpeechMessageProducts.ChurchReport/Startup.cs
ToolUtility.Dataverse.Tests/**
.trellis/tasks/08-17-toolutility-scoped-lifetime/notes.md
```

補 Run 1.5 缺口（第 9.1 節）時，額外准許改動下列兩檔，且只准改 catch 區塊的追蹤呼叫：

```
SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs
SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
```

若雷 3 查出 `ToolUtilityFacade` 或其子服務確實會誤釋放連線，那些檔案**准許納入本 Run**，
但必須在 `notes.md` 逐一列出檔案與理由。除此之外，清單外一律不動。

`.trellis/tasks/08-17-toolutility-scoped-lifetime/run2-codex-prompt.md`（本檔）不需理會，
它是交辦文件，留在工作區屬正常。

## 5. 四條硬規則

1. 只改白名單內的檔案。
2. 連續 3 次驗證失敗 → 走第 8 節的失敗處理程序，不要試第 4 次。
3. 發現清單外的問題 → 寫進 `notes.md`，絕不順手修。
4. 通過第 6 節全部門檻才 commit；本 Run 一個 commit。

## 6. 品質門檻（commit 前必須全過，且要把實際輸出貼進 notes.md）

```bash
dotnet build SpeechMessageProducts.sln -c Debug
```

期望：0 錯誤 0 警告。

```bash
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

期望 63 通過 0 失敗。

```bash
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
```

期望 >= 7 通過 0 失敗（本 Run 會再增加測試）。

```bash
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

**基準線：22 失敗 / 304 通過 / 共 326。**
這 22 個是 `Payments.*` 的既有命名測試失敗，與本任務無關，已在 b627f472 確認同樣失敗。
判定：失敗數不得 > 22，通過數不得 < 304。任何一項惡化即視為本 Run 失敗。

**G3 繁體中文文件**：新增或實質修改的 `.cs`，其 public/internal 型別、介面、建構式、方法、
重要屬性需有完整繁中 XML 註解。一行復述或單獨 `<inheritdoc />` 不算。
擁有資源的型別須寫明：資源最大生命週期、確定性釋放路徑、如何防跨請求洩漏。
**兩個建構式都必須寫明「誰擁有這條連線、誰負責釋放」。**

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

**G4b 行尾一致性**：本專案 `.gitattributes` 為 `*.cs text eol=crlf`，
Run 1.5 曾在 CRLF 檔案中寫入單獨 LF 行。必須輸出 `CRLF OK`：

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

不是 `CRLF OK` 就把你寫入的行改成 CRLF，再跑一次。

## 7. 完成判定（機械可判，全部要有實際輸出）

```bash
grep -n "ToolUtilityFactory" ToolUtility/DependencyInjection/ToolUtilityProvider.cs
```

必須 0 行（Provider 不再走 Factory）。

```bash
grep -n "AddScoped<IToolUtilityProvider\|AddSingleton<IToolUtilityProvider" ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
```

只能看到 `AddScoped<IToolUtilityProvider`，`AddSingleton<IToolUtilityProvider` 必須 0 行。

```bash
grep -n "AddScoped" ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
```

必須看到 `ToolUtilityClass` 的 Scoped 註冊。

```bash
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs
```

必須無輸出（Run 2 不該碰呼叫端）。

```bash
grep -rc "ToolUtilityFactory.GetInstance" --include=*.cs SpeechMessageProducts.ChurchReport | awk -F: '{s+=$2} END {print s}'
```

應仍為 Run 2 前的數量，不得因本 Run 而變動（那 35 個呼叫點屬 Run 3）。

再加上 G1 / G2 / G3 / G4 / G4b 全過。

## 8. 失敗處理程序（絕不使用無範圍的 git clean）

1. `git restore -- <本 Run 清單中原已存在的檔案>`
2. `rm -f <本 Run 新建立的檔案，逐一列出路徑>`
3. `notes.md` 記錄 Run 編號、失敗原因、最後的完整錯誤訊息
4. 標記 SKIPPED，**停止**。不要跳過 Run 2 去做 Run 3 —— 三個 Run 有前後依賴。

## 9. commit

Run 1.5 的 4 個 .cs 改動尚未 commit。**先補完 Run 1.5 的三項缺口，再單獨 commit Run 1.5**，
然後才做 Run 2。兩者不可混在同一個 commit。

### 9.1 Run 1.5 缺口（已由外部驗證找出，必須先補）

**缺口 A —— catch 區塊在 scope 釋放後使用 toolUtility（兩個檔案都有）**

`PersonalController.cs` 與 `SmallGroupController.Save.cs` 目前的形態是：

```csharp
ToolUtilityClass toolUtility = null;
try
{
    using var scope = _scopeFactory.CreateScope();
    toolUtility = ...;        // 在 scope 內取得
    ...
}
catch (Exception ex)
{
    toolUtility?.TraceByLevel(...);   // ← 此時 scope 已釋放
}
```

`using var` 的釋放點是 try 區塊結束，所以 catch 執行時 scope 已經 Dispose。
現在 `ToolUtilityClass` 還是單例所以看不出問題，**但 Run 2 一落地就是 use-after-dispose**，
正是本任務要消滅的模式。

修法：把 try/catch 移到 scope 之內，或改用 `ToolUtilityClass.TraceByLevelStatic(...)`
（它委派給程序級 tracer，不依賴任何 scope）。擇一即可，但兩個檔案都要修。

**缺口 B —— `SmallGroupController.Save.cs` 的背景 scope 沒有被真正的工作使用**

該背景工作真正做事的是 `weeklyReportRef.UploadIntegrateDataAsync(...)`，
而 `weeklyReportRef` 是進入 `Task.Run` 前從 request 捕獲的
`InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport`，其內部
（`ListSmallGroupWeeklyReport` / `UploadIntegrateData`）仍以 `ToolUtilityFactory` 取得舊單例。
新建的 scope 目前只被用來寫錯誤日誌。

你在 `notes.md` 已誠實記錄此事，判斷「留到 Run 3」也合理 —— **維持該判斷即可，不要在此擴大範圍**。
但要在 `notes.md` 把它升級成一條明確的 Run 3 阻擋項，寫明：
在 Run 3 遷移 `ListSmallGroupWeeklyReport` / `UploadIntegrateData` 之前，
SmallGroup 的 fire-and-forget 路徑仍會使用程序級連線。

**缺口 C —— Run 1.5 核取清單中的測試沒有寫**

`implement.md` 的 Run 1.5 要求「新增測試：背景 scope 於工作結束時被釋放，且不與請求 scope 共用連線」。
`ToolUtility.Dataverse.Tests` 目前只有 Run 1 的 `FileToolUtilityTracerTests.cs` 與既有的
`PooledOrganizationServiceTests.cs`，該測試不存在。補上它，或在 `notes.md` 明確寫出
「此測試延到 Run 2 一併涵蓋」及理由 —— 不可以無聲略過。

**缺口 D —— 行尾**

Run 1.5 在四個 CRLF 檔案中寫入了單獨 LF 行（PersonalController 42 行、
SmallGroupController.Core 25 行、SmallGroupController.Save 23 行、
InMemoryDataContextSmallGroup 12 行）。commit 前跑第 6 節的 G4b，改到輸出 `CRLF OK`。

### 9.2 commit 訊息

Run 1.5：

```
fix(toolutility): 移除跨請求持有者，背景工作自建 DI scope
```

Run 2 完成後：

```
refactor(toolutility): ToolUtilityClass 改為 request 範圍
```

## 10. 明確不做

- 不要動那 35 個 `ToolUtilityFactory.GetInstance()` 呼叫點（那是 Run 3）
- 不要刪除 `ToolUtilityFactory`（那是 Run 3 的 3e）
- 不要碰明文密碼與憑證輪替（使用者已表示現況可接受）
- 不要碰 `appsettings.Production.json` 的 `ConnectionPool` 區段
- 不要重新設計 `ToolUtilityClass` 的公開 API（本任務只改生命週期與取得方式）
- 不要修那 22 個既有失敗的 Payments 測試

## 11. 交付

完成後在 `notes.md` 寫一節「Run 2 結果」，內容包含：

- 每個核取項的 DONE / 未做 與理由
- 雷 1～雷 6 各自的處理方式與查證結論（雷 3 必須有實際讀過的檔案清單）
- 第 6、7 節每一道指令的**實際輸出原文**，不要摘要、不要改寫
- 範圍外發現（不要順手修）

> 人工回歸（登入、會友、奉獻、影像上傳、LINE 綁定）由使用者執行，不列為你的完成條件，
> 但你要在 `notes.md` 標明「等待人工回歸」。
