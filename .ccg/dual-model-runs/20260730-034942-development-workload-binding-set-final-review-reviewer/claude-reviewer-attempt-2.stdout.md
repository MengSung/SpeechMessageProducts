# 審查結果：**FAIL**

## 總結判斷

程式碼邏輯本身（`ConfigurationGatewayOperationAuthorizer.cs` 的具名 binding set 解析）設計正確、fail-closed、無 selector fallback／path injection，但**本次變更提交的迴歸測試檔案編譯失敗**，導致所有回報的測試證據（235 passed / 23 passed 等）在目前 worktree 狀態下無法重現。這是 release blocker。

---

## Critical 🔴

### 1. `SpeechMessage.Dynamics.Tests` 專案編譯失敗（CS1674），證明用的迴歸測試無法執行

- **檔案**：`SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs:354`
- **根因**：
  ```csharp
  using var configuration = new ConfigurationBuilder()
      .SetBasePath(gatewayProjectPath)
      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
      .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
      .Build();
  ```
  `IConfigurationBuilder.Build()` 的回傳型別是介面 `IConfigurationRoot`，此介面**未**宣告/繼承 `IDisposable`（只有具體類別 `ConfigurationRoot` 才實作 `IDisposable`）。`using var` 依賴的是運算式的編譯期型別，因此編譯器直接報錯：
  `error CS1674: 'IConfigurationRoot: 在 using 陳述式中使用的類型必須實作 'System.IDisposable'`
- **可重現時序**：在本 worktree 直接執行
  `dotnet build SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj` → 100% 重現，非環境雜訊（無 LangVersion 覆寫、無自訂 Dispose 擴充方法）。
- **影響**：這一行正是本次任務新增、專門用來證明「Development 不再繼承 Central 授權」的迴歸測試
  `Development_configuration_does_not_inherit_central_workload_binding` 的一部分。專案不能編譯 ⇒ 這條測試、以及同檔案內其他所有測試（`GatewayWorkloadBoundaryTests` 23 個案例）**都無法執行**，回報的「GatewayWorkloadBoundaryTests 23 passed」「SpeechMessage.Dynamics.Tests 235 passed / 0 failed」「Release 0 warnings / 0 errors」等證據與目前 worktree 內容不一致，不能作為本次修正已驗證的依據。
- **建議修正**：移除 `using`（`IConfigurationBuilder.Build()` 產生的組態物件在此屬短生命週期本地測試物件，非必要 deterministic dispose），或明確轉型為具體類別：
  ```csharp
  var configuration = new ConfigurationBuilder()
      .SetBasePath(gatewayProjectPath)
      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
      .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
      .Build();
  ```
  修正後必須重新產生完整證據（targeted + full regression + Release build 0 warnings/0 errors），不能沿用舊回報。

---

## Warning 🟡

### 2. `Selected_empty_workload_binding_set_fails_host_startup` 實際只覆蓋 scalar 分支，未覆蓋「object 存在但無 child」分支

- **檔案**：`SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs:410-426`；對應實作 `SpeechMessage.Dynamics.Gateway/Security/ConfigurationGatewayOperationAuthorizer.cs:145-182`
- **根因**：測試用
  ```csharp
  ["DynamicsGateway:ActiveWorkloadBindingSet"] = "Empty",
  ["DynamicsGateway:WorkloadBindingSets:Empty"] = "declared-without-bindings"
  ```
  這會命中 `ReadActiveBindingSections` 的 **`activeBindingSet.Value is not null`**（scalar-only）分支並拋錯，並不會走到 **`bindingSections.Length == 0`**（`Value` 為 `null` 但沒有任何 child section）這條獨立分支。兩者是程式碼中不同的 `throw`，測試名稱／註解宣稱驗證「empty set」，但實際沒有任何案例直接命中「非 scalar、零 child」這條路徑。
- **對應契約**：需求 #2、#6 明確要求「childless...set 必須 fail closed」且「無效／空 set startup 測試也必須覆蓋」——目前只做到一半。
- **建議修正**：新增一筆使用 in-memory 設定、把某個 binding set 名稱的值明確設為 `null`（且不加任何 `:0:...` 子鍵）的案例，直接驗證 `bindingSections.Length == 0` 分支確實會擲出 `InvalidOperationException`；並更新既有測試命名／註解，明確指出它驗證的是 scalar 分支而非「零 child」分支。

---

## Info 🟢

### 3. `ReadActiveBindingSections` 的大小寫比對與 `Take(2)` 屬防禦性冗餘，非缺陷

`configuration.GetSection(BindingSetsPath).GetChildren()` 在 .NET Configuration 內部已用 `OrdinalIgnoreCase` 對各 provider 的 child key 去重，理論上不會出現同一 selector 大小寫不同卻視為兩個獨立 child 的情形。程式碼中額外的 `Where(...OrdinalIgnoreCase).Take(2)` 加上 `Length != 1` 判斷屬於 belt-and-suspenders，不影響正確性，但可在文件中註明這是刻意的縱深防禦而非依賴 framework 行為的必要條件，避免未來維護者誤以為此檢查在正常情境下真的能命中「模糊」分支。

---

## 針對必查問題的明確回答

1. **Development→Central authorization inheritance 是否仍存在？**
   否。程式碼審查確認：`ReadActiveBindingSections` 只 enumerate `WorkloadBindingSets` 的直接 children，並以 `ActiveWorkloadBindingSet` 做 exact case-insensitive 比對後只回傳單一 set 的 child sections；base 與 Development JSON 中的 `Central`／`Local` 是不同 key，不會被 .NET Configuration 逐葉合併聯集。但**此結論目前只能靠靜態程式碼閱讀佐證，因為驗證用的迴歸測試（Critical #1）無法編譯執行**，尚未有可執行證據。

2. **Selector fallback／configuration-path injection 是否存在？**
   否。Selector 字串（`ActiveWorkloadBindingSet`）只用於和 `GetChildren()` 回傳的 `section.Key`（單一 segment，不含冒號）做 `string.Equals`，不會被串接進 `GetSection(path)`；即使 selector 含冒號企圖跨 section 存取，也只會導致比對不到任何 child（`matchingBindingSets.Length == 0`）而 fail closed，不會產生路徑穿越。空白／wildcard／未知／歧義／scalar／childless 皆在 constructor 同步拋出 `InvalidOperationException`，沒有 fallback 到 Central、第一組或全部集合的程式路徑。

3. **Testing→Central 繼承是否存在？**
   否。所有 Testing 相關 Factory（`GatewayWorkloadBoundaryTests`、`GatewayRequestBodyBoundaryTests`、`GatewayReadinessTests`、`GatewayKestrelNegotiateTests`）皆明確將 `ActiveWorkloadBindingSet` 設為 `"Testing"`，並自建獨立、非空的 `WorkloadBindingSets:Testing:*` binding，未見任何測試 fixture 隱含依賴或繼承 `Central`。

4. **Lifecycle／resource leak 是否存在？**
   審查範圍內（authorizer 與 startup validator）沒有發現新增 lock、reload subscription、principal cache、timer、background Task、socket、connection 或額外 disposal owner；request 熱路徑（`Authorize`／`AuthorizeOperationCatalog`／`ResolveAuthenticatedBinding`）維持純 frozen dictionary 唯讀查找。`GatewayOperationAuthorizationStartupValidator` 沒有新增可 dispose 資源。（Program.cs 中與本次授權修正無關的 request-body reader 改動不在審查範圍內，未深入評估。）

5. **註解或 UTF-8 契約缺口？**
   註解方面：新增/修改的 production 與測試程式碼（`ReadActiveBindingSections`、相關測試案例）皆有完整繁體中文說明 trust boundary、fail-closed、ownership、無 cleanup 需求等考量，符合要求。編碼方面：對全部 10 個範圍檔案逐一檢查，皆為 **UTF-8 without BOM、全文 CRLF、檔尾 CRLF**，未發現缺口。

---

## 結論

僅有 1 項 Critical（測試專案編譯失敗，導致宣稱的證據不可信）與 1 項 Warning（空集合分支測試覆蓋不完整）。在 Critical #1 修正並重新產生完整、可重現的證據（targeted + full regression + Release build）之前，本次修正**不可視為已驗證通過**，建議標記為 release blocker 並打回修正。

---
SESSION_ID: 5678bbba-8958-4ae2-ab95-7c3a015e1c94
