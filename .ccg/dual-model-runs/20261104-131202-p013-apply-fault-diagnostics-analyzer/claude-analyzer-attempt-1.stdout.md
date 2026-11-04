## Analysis: P0-13 apply FaultException 安全診斷補修方案

### Context
- 目標程式碼：`SpeechMessage.Dynamics.Deployment/DeploymentCli.cs` 第 164-177 行既有 `catch (Exception exception)` 區塊。
- 已驗證事實（讀碼，未連線任何外部服務）：
  - `using var lease = connector.Connect(...)`（第 99 行）與 `using var writeLock = ...`（第 92 行）皆在同一個 `try` 區塊內以 `using var` 宣告；C# 編譯器會把這類宣告展開成巢狀 `try/finally`，因此例外往外傳播時，lease／writeLock 的 Dispose 一定在外層 `catch` 區塊程式碼執行**之前**完成。方案只在既有 `catch` 內加邏輯，不會更動這個既有保證。
  - `apply` 對應的 `DeploymentEngine.Apply`（`DeploymentEngine.cs:162`）是同步方法，整個檔案唯一用到 `Task`/`await` 的地方是 `SmokeAsync`；`apply` 路徑不會把 SDK 擲出的 `FaultException<OrganizationServiceFault>` 包成 `AggregateException`，所以在外層 `catch (Exception exception)` 直接用 `exception is FaultException<OrganizationServiceFault>` 偵測是有效的。
  - `System.ServiceModel.Primitives`（`FaultException<T>` 所在套件）與 `Microsoft.Xrm.Sdk`（`OrganizationServiceFault` 所在套件）都已經透過 `ToolUtility` → `PowerPlatform.Dataverse.Client` 傳遞引用到 `SpeechMessage.Dynamics.Deployment` 專案；`DeploymentEngine.cs`、`Contracts.cs` 已直接 `using Microsoft.Xrm.Sdk;`。方案不需要新增任何 `PackageReference`／`ProjectReference`。
  - 既有測試（`DeploymentContractTests.cs:632-657`）已經有同樣模式：`Assert.Same(failure, reported)` + `Assert.DoesNotContain(Fixture.SecretValue, output.ToString())`，可直接延伸出新案例,不需新建測試基礎設施。

### Recommendation
方案本身方向正確、範圍夠小，可以採用；但實作時有兩個 **Critical** 與一個 **High** 細節若不特別處理,會破壞方案自己設下的安全與可靠性目標,必須在 code review 時擋下。

### Risks & Mitigations

**1. [Critical] 錯誤碼列印段落若排在 `report(exception)` 之前或與其共用同一個 try，會讓既有的「原始例外保留並上報一次」失效**
現有程式碼結構：
```csharp
catch (Exception exception)
{
    try { report(exception); }
    catch { WriteFixedError(output); }
    output.WriteLine("Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
    return 1;
}
```
若新增的 Fault 偵測／列印邏輯（讀 `fault.Detail.ErrorCode`、走 `InnerFault`）被放進 `try { report(exception); }` 這個 try 裡面、或放在它之前，一旦該段落本身丟例外（例如 `Detail` 為 null 造成 NRE），就會被既有 `catch { WriteFixedError(output); }` 吞掉，`report(exception)` 可能完全沒被呼叫到，等於這次失敗**沒有落 Exception.log**——直接違反方案自訂的「保留原始 exception 經既有 report 委派上報一次」要求，且是比現狀更差的回歸。
→ **必要修法**：`report(exception)` 呼叫必須保持在最前面、不被新邏輯包住；錢碼偵測/列印要包在自己獨立的 `try { ... } catch { }`（吞掉任何例外、不呼叫 `report`、不中斷既有流程），並放在 `report(exception)` 之後、"Command failed..." 那行之前或之後都可以，但不能讓它有機會阻擋 `report` 執行或改變 `return 1`。

**2. [Critical] ErrorCode 轉 16 進位若發生隱性型別提升（int → long/ulong）會跳出「固定 8 位」的安全契約**
`OrganizationServiceFault.ErrorCode` 是 `int`，典型值如 `unchecked((int)0x80040217)` 是負數。直接 `fault.Detail.ErrorCode.ToString("X8")`（int 上呼叫）會正確輸出 8 位（如 `80040217`）。但若中途被提升成 `long`（例如寫成 `((long)fault.Detail.ErrorCode).ToString("X8")`、或透過 `object`/格式化字串插補不小心做了寬化），負數會做符號延伸，`"X8"` 只保證「最少 8 位」而非「固定 8 位」，結果會變成 16 位（如 `FFFFFFFF80040217`）——不只違反方案宣告的輸出格式契約，也會讓離線測試對「固定 8 位」的斷言失敗。
→ **必要修法**：直接在 `int`（或先 `unchecked((uint)errorCode)`）上呼叫 `.ToString("X8", CultureInfo.InvariantCulture)`，禁止任何先轉 `long`/`object`/字串插補再格式化的寫法；code review 時應明確檢查這一行的型別。

**3. [High] InnerFault 四層界限必須用「有界迭代」而不是「遞迴＋visited-set」實作，否則離層測試中的 cycle 案例可能繞過界限**
方案要求「有 InnerFault 時最多 4 層」且離線測試會注入帶 cycle 的 Fault 鏈。若實作用遞迴函式呼叫（即使有 depth 參數）而非單純的 `for` 迴圈,日後有人「優化」成 `while (current.InnerFault != null)` 忘記帶 depth 檢查，cycle 會造成無窮迴圈/StackOverflow。
→ **必要修法**：用簡單的有界 `for (var i = 0; i < 4 && current is not null; i++) { ...; current = current.InnerFault; }`。這個寫法本身就與是否有 cycle 無關（迴圈次數硬性 ≤4），比「遞迴+visited 集合」更簡單也更不容易被後續修改破壞邊界。

**4. [Warning] `fault.Detail` 為 null 的防呆**
`FaultException<OrganizationServiceFault>` 理論上允許 `Detail` 為 null（雖然 `FaultReader.cs` 正常路徑一定給非 null 值）。偵測分支讀 `fault.Detail.ErrorCode` 前應先判斷 `fault.Detail is not null`，避免 NRE——這個風險與第 1 點（NRE 吞掉 report）是同一類問題的具體觸發點，建議一起處理。

**5. [Info] 不需新增套件／專案參考**
已確認 `System.ServiceModel.Primitives`、`Microsoft.Xrm.Sdk` 透過既有 `ToolUtility`→`PowerPlatform.Dataverse.Client` 鏈路已可直接 `using`，`DeploymentCli.cs` 只要加兩行 `using` 即可，不會把依賴面擴大，符合「最小補修」原則。

**6. [Info] 取消分類無額外風險**
`FaultException<OrganizationServiceFault>` 不是 `OperationCanceledException`，既有 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` 分支在前，新邏輯只影響落到第二個 `catch (Exception exception)` 的案例,不會誤將真正的使用者取消歸類成 Fault，也不會反過來讓 Fault 被誤判成取消（回傳碼仍是 `1`，非 `130`）。

**7. [Info] 資源釋放無需额外處理**
由於方案明確限定「僅在既有 catch 中」修改，不觸碰 `try`/`using` 結構，lease 與 writeLock 的釋放時序（在 catch 本體執行前，透過巢狀 `finally` 完成）不受影響，不需要額外的 dispose 保證。

### Action Items
1. [ ] 錯誤碼偵測/列印邏輯獨立包一層 `try { } catch { }`，確保不會吞掉或延後既有 `report(exception)` 呼叫（對應風險 1、4）。
2. [ ] 格式化碼明確寫成 `fault.Detail.ErrorCode.ToString("X8", CultureInfo.InvariantCulture)`，不經過 `long`/`object` 中轉（對應風險 2）。
3. [ ] InnerFault 走訪用有界 `for` 迴圈（上限 4），不要用遞迴（對應風險 3）。
4. [ ] 新增測試沿用 `DeploymentContractTests.cs:632-657` 既有 `Assert.Same(reported, exception)` + `DoesNotContain(secret)` 模式，額外補上「cycle 不掛住」「深度剛好截在第 4 層」「`Detail` 為 null 時不丟例外且仍完成上報」三個案例。
5. [ ] 不需新增 `PackageReference`/`ProjectReference`，確認 diff 只動 `DeploymentCli.cs` 與其測試檔。

(本次僅讀取檔案進行離線分析，未連線任何 D365／LINE 服務，未讀取 secrets/設定值，未掃描其他 worktree。)

---
SESSION_ID: d9c156e3-841f-4517-92fc-2f7ff7f017b6
