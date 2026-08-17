# 執行計畫：產品 A（好牧人 1.5）— 最小可行版

## 目標

只做一件事：**讓連線不可能漏還。**

其餘全部延後（見文末）。這份計畫的成功標準是「能收尾」，不是「做得完整」。

---

## 給執行者的四條硬規則

1. **只改本 Run 檔案清單裡的檔案。** 清單外一律不動。
2. **連續 3 次驗證失敗就停下來回報。** 不要試第 4 次。
3. **發現清單外的問題，寫進 `notes.md`，不要順手修。**
4. **驗證通過才 commit，一個 Run 一個 commit。**

---

## 共用驗證

```bash
dotnet build SpeechMessageProducts.sln -c Debug
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

---

## Run 1 — 小調查（不寫產品程式碼）

只問兩題：

1. `ToolUtility` 中採 `ref IOrganizationService` 的方法，**哪些會在內部重新指派
   該參數**？列出方法名與行號。
2. `OnPremiseClient._service`（`OnPremiseClient.cs:67`）在 `ConnectAD`（`:254`）與
   `ConnectFederated`（`:171`）下的實際型別是什麼？是否為 `ICommunicationObject`？

**檔案清單**：只新增 `research/findings.md`。

**完成判定**：`findings.md` 存在且兩題都有答案；`git status --porcelain` 除該檔外乾淨。

**上限**：一個工作階段。答不出第 2 題就寫「未確認」，不要硬查。

---

## Run 2 — Scoped 連線註冊

**這是本計畫的核心，也是唯一非做不可的一步。**

改動：

- `Startup.cs` 新增
  `services.AddScoped<IOrganizationService>(sp => new PooledOrganizationService(pool))`
  —— 建構時 `AcquireConnection()`，`Dispose()` 時 `ReleaseConnection()`
- 新增 `ToolUtility/ConnectionOperations/PooledOrganizationService.cs`：
  實作 `IOrganizationService` + `IDisposable`，六個方法直接轉發
- `CrmConnectionPool.ReleaseConnection()` 加上 fault 判斷：
  傳入的連線若標記為故障則銷毀不回池（順手修，因為就在同一個方法裡）
- 依 Run 1 第 2 題結果，讓 `OnPremiseClient` 實作 `IDisposable`
  （**若 Run 1 答「未確認」則跳過此項**，記入 `notes.md`）

**檔案清單**：
`SpeechMessageProducts.ChurchReport/Startup.cs`、
`ToolUtility/ConnectionOperations/PooledOrganizationService.cs`（新）、
`ToolUtility/ConnectionOperations/CrmConnectionPool.cs`、
`PowerPlatform.Dataverse.Client/OnPremiseClient.cs`（條件性）

**完成判定**：

```bash
dotnet build SpeechMessageProducts.sln -c Debug && \
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
```

加兩個測試即可，不要多：
- `PooledOrganizationService` 被 Dispose 後，連線已歸還（池的 idle 數 +1）
- 故障連線歸還後不回池

---

## Run 3 — 遷移 23 個池化呼叫點

**一個 Controller 一個子 Run**，做完一個 commit 一個。不要一次做完。

| 子 Run | 檔案 | 呼叫點 |
|---|---|---|
| 3a | `MemberInfoController.cs` | 10 |
| 3b | `AuthenticationController/*.cs` | 8 |
| 3c | `PersonalController.ImageUpload.cs` | 3 |
| 3d | `NewPersonController.cs` | 1 |
| 3e | `BaseChurchController.cs` 刪除 `GetConnection`/`ReleaseConnection` | — |

改法固定為：

```csharp
// 之前
var service = GetConnection();
try { ... } finally { ReleaseConnection(service); }

// 之後（建構式注入的 _service，容器負責歸還）
... 直接用 _service ...
```

**每個子 Run 的完成判定**：

```bash
dotnet build SpeechMessageProducts.sln -c Debug && \
grep -c "GetConnection()\|ReleaseConnection(" <本子Run的檔案>
# 必須為 0
```

**全部完成判定**：

```bash
grep -rn "GetConnection()\|ReleaseConnection(" --include=*.cs SpeechMessageProducts.ChurchReport
# 必須回傳 0 行
```

---

## 本次明確不做（已知殘留風險，記錄備查）

| 延後項目 | 殘留風險 | 何時處理 |
|---|---|---|
| 70+ 個 `m_Crm2011OrganizationService` 呼叫點 | 那條共用的非執行緒安全連線仍在，高併發下可能偶發錯誤 | Run 3 穩定後另立 task |
| Gateway / Manager / Keyed Pool 三層 | 無 —— 現階段用不到 | 產品 B 要接時 |
| Reentrant Lease | 無 —— Scoped 是每 request 一條，本來就不會巢狀重複借 | 同上 |
| `PoolKey` / per-user impersonation | 無 —— 目前無 impersonation 需求 | 需要時 |
| 池統計、健康檢查端點 | 看不到池的即時狀態 | 想調參數時 |
| 明文密碼（`appsettings.json` 與 `Core.cs:51` 的 `?? "hu9840"`） | 憑證外洩 | **另開維運票，不在本 task** |
| `appsettings.Production.json` 的 `ConnectionPool` 區段未被讀取 | 正式環境跑預設 3/20 | 想調參數時一起處理 |

---

## 目前可以開始的只有 Run 1
