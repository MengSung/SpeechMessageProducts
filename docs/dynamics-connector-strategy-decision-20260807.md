# Dynamics Connector 路線決策與調研紀錄

> 版本：1.0　｜　日期：2026-08-07　｜　狀態：已決策
> 相關規格：`docs/dynamics-connection-management-spec.md`
> 相關決策：`.trellis/tasks/08-05-official-worker-router-ce-integration/scope-rebaseline-2026-08-07.md`
> 性質：本文是**決策紀錄與調研存檔**，不是規格。規格衝突時以連線管理規格書為準。

---

## 0. 本文要回答的問題

1. Data8 與 Official Worker 兩條路線的技術本質差別是什麼？
2. 我們 vendored 的 Data8 分叉相對上游是新是舊？該不該同步？
3. 網路上有沒有第二個同等功能的解決方案？
4. Data8 這條路可以走多久？什麼時候該檢討？

---

## 1. 兩條路線的技術本質

### 1.1 常見的誤解與更正

本次調研更正了三個先前流通的說法。**這三點是本文最重要的內容**，因為它們會改變決策框架。

| 誤解 | 實際情況 | 依據 |
|---|---|---|
| 「Data8 不是包裝官方 SDK，是完全自己寫的」 | 只對一半。它**站在官方現代 SDK 之上**，引用 `Microsoft.PowerPlatform.Dataverse.Client`，`Microsoft.Xrm.Sdk`／`Entity`／`IOrganizationService` 型別全部來自那顆套件。Data8 自己重寫的是**驗證握手與 SOAP 傳輸層** | `PowerPlatform.Dataverse.Client.csproj` 的 `PackageReference` 與 `Description` |
| 「官方 SDK 只支援到 .NET 4.8」 | 不成立。官方現代 SDK 本來就跑 .NET 6/8/10，我們自己就在 net10 上用它。真正的限制窄得多：**官方在 .NET Core+ 上只走 OAuth**；on-prem 要嘛有 ADFS 2019+，要嘛回 net48 | 見 §3.3 |
| 「版本只是一個參數，一份程式碼同進程支援 8.2 與 9.1」 | 是**設計意圖**，不是現況。`_sdkMajorVersion` 仍是 `private static readonly`，由 `Microsoft.Xrm.Sdk` 組件版本推導，實務上兩個 profile 都送 `sdkversion=9` | `PowerPlatform.Dataverse.Client/OnPremiseClient.cs:77`、`:79-95`、`:139` |

### 1.2 路線 A：Data8（現行主線）

- 手寫 WS-Trust XML（`ADAuthHelpers/`，14 檔）
- SSPI 的 P/Invoke 後備路徑（`NSspi/`，37 檔）—— **在 net10 上為死碼，已於本次排除，見 §2.2**
- `OnPremiseClient` 實作完整 `IOrganizationService`
- 同進程、net10、無 IPC、無子進程
- MIT 授權，原始碼在 repo 內，我們自行維護

### 1.3 路線 B：Official Worker（擴充點，非主線）

- 原廠 `CrmServiceClient`，只能跑 net48 → 必須編成獨立 `.exe`
- net10 Supervisor 啟動子進程，具名管道 IPC
- 契約層 `netstandard2.0`（兩邊唯一都能參考的框架）

**現況規模與能力面**：

| 項目 | Data8 路線 | Official Worker 路線 |
|---|---|---|
| 程式碼量 | 7,162 行協定庫 ＋ 1,337 行連接器 | 9,005 行（Worker×2 ＋ Host ＋ Protocol ＋ Supervisor） |
| 能力面 | 完整 `IOrganizationService`（8 個方法） | **1 個 operation** ＋ WhoAmI 探測 |
| 真機驗證 | 已是 ChurchReport 主線 | **未通過**（兩個 Worker 都在 READY 前 exit code 20） |
| 部署需求 | net10 單進程 | net10 ＋ .NET Framework 4.8 ＋ 子進程管理 |

Official Worker 唯一支援的 operation 是 `fee.dedication.retrieve.by.contact.date.range`，其餘請求在 `SpeechMessage.Dynamics.Crm82Worker/OfficialCrmServiceClientAdapter.cs:307` 直接 `throw`。

### 1.4 各自買到什麼

**Official Worker 的真實價值**（不只是「舊」）：

- 協定維護責任在原廠 —— Data8 的 WS-Trust／SSPI 路徑安全修補由我們自己扛
- **版本隔離是硬需求** —— 8.2 與 9.1 的 `Microsoft.Xrm.Sdk` 是同一組件識別的不同版本，技術上不可能同進程共存。Data8 能同進程正是因為它繞開了那些組件
- 故障與資源隔離、可回收

**代價**：每個新查詢都是一次跨進程契約工程（定義 contract → 算 revision hash → Worker 端 operation → Supervisor 端 projector → 兩邊測試），且契約層被 9 種 `WorkerValueKind` 綁死，`Entity`／`EntityReference`／`OptionSetValue`／`Money` 都要手動投影兩次。

**結論**：維持 2026-08-07 scope rebaseline 的決定 —— Data8 是永久主線，Official Worker 保留為擴充點與驗證對照組。

---

## 2. 我們的分叉狀態與本次套用的修正

### 2.1 分叉不是落後，是超前

repo 內的 `PowerPlatform.Dataverse.Client/` 是 [Data8/DataverseClient](https://github.com/Data8/DataverseClient) 的**硬分叉**（無 submodule、無 upstream remote）。

| | 上游 2.4.2 | 我們的分叉 |
|---|---|---|
| TFM | net462 / net6.0 / **net7.0** | **net10.0** |
| Microsoft.PowerPlatform.Dataverse.Client | 1.1.16 | **1.1.32** |
| System.ServiceModel.Federation | 6.2.0 | **10.0.652802** |
| 最後 commit | **2025-02-20**（已凍結） | 持續維護 |

上游 README 自述「not officially supported, either by Data8 or Microsoft」。**在這種前提下 vendored ＋ 自行維護是正確決策**，不是技術債。

### 2.2 本次（2026-08-07）套用的四處修正

均已建置驗證（0 警告 0 錯誤）並跑過測試（463 通過，3 個既有 Kestrel 埠權限失敗，已用 `git stash` 驗證基線相同）。

**修正 1 — `WebException` 遮蔽原始例外**（對齊上游）
`PowerPlatform.Dataverse.Client/ADAuthClient.cs`

```csharp
catch (WebException ex) when (ex.Response != null)
```

原本無條件進入 catch。逾時、DNS 失敗、連線被拒、TLS 交握失敗時 `ex.Response` 為 null，`ex.Response.GetResponseStream()` 丟出 `NullReferenceException`，把真正的 `WebException` 與其 `WebExceptionStatus`（唯一分類依據）整個吃掉。

**修正 2 — WS-Trust 權杖交換的無出口活鎖**（本地強化，上游有等價缺陷）

原本 `while (finalResponse == null) { if (resp is RequestSecurityTokenResponse r) { ... } }` 沒有 else。伺服器回傳非預期訊息時，迴圈條件永遠成立而迴圈內不再更新 `resp` → 無出口、無逾時、100% CPU 空轉，執行緒卡死在驗證階段。改為 fail-fast 拋出。例外訊息不含端點、帳號、權杖或原始回應。

> ⚠️ 此修正**不在上游**。上游寫成 `if (!(resp is ...)) continue;`，一樣空轉。若日後要與上游對齊，這是唯一的差異點。

**修正 3 — `using NSspi.Contexts;` 移入 `#else`**

清掉 net10 建置下的死 using，並移除原本空的 `#else`／`#endif`。

**修正 4 — 排除 NSspi 編譯**
`PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`

```xml
<ItemGroup Condition="'$(TargetFramework)' != 'net462'">
  <Compile Remove="NSspi\**\*.cs" />
</ItemGroup>
```

原因：net10 上 `NET7_0_OR_GREATER` 恆為真，`ADAuthClient` 的 `#else`（NSspi）分支永不編譯，但 37 個 NSspi 檔案仍被 SDK 預設 glob 全數編入輸出組件，其中三個是對 `secur32.dll` 的非受管 P/Invoke 宣告。

**這不是漏洞**（型別無法被觸達），是沒有必要出現在交付組件的非受管介面面積。上游的 net8.0 建置也不含它們。

驗證：Compile 項目 55 → 18 個檔案；輸出 DLL 內 `NSspi` 與 `secur32` 字串皆為 0 次。

### 2.3 上游改動中**不該照抄**的部分

上游 commit [`e58a0ad`](https://github.com/Data8/DataverseClient/commit/e58a0add37082725152e1541e250aaebf650b8ae)（2025-02-20）把 `#if NET7_0_OR_GREATER` 全面改成 `#if NETCOREAPP`。

**不要跟進。** `NETCOREAPP` 對 netcoreapp3.1／net5.0／net6.0 同樣有定義；拿它守衛一個 .NET 7+ 才存在的 API（`System.Net.Security.NegotiateAuthentication`，Microsoft Learn 的 moniker 為 net-7.0 起，**無 net-6.0**）語意上是錯的。上游只是因為 TFM 集合裡已無 .NET 8 以下目標才碰巧不出事。我們的 `NET7_0_OR_GREATER` 精確且自我說明。

同理，`#if !NET7_0_OR_GREATER` 內的 `PlatformNotSupportedException` 守衛在 net10 上已編譯排除，移除純屬美化，保留可自我保護。

### 2.4 我們比上游好的兩處（已確認保留）

- `NegotiateAuthentication` 使用 `using` 釋放 —— 上游未釋放
- `#if NET7_0_OR_GREATER` 比上游的 `#if NETCOREAPP` 正確（見 §2.3）

---

## 3. 替代方案完整調研

調研涵蓋 GitHub fork 網路、NuGet、其他語言生態、Microsoft 官方文件。

### 3.1 結論：沒有第二個 WS-Trust ＋ `IOrganizationService` 的 .NET 實作

這不是搜尋不夠深，是需求交集太窄：**on-prem ＋ Windows AD ＋ .NET Core 以上 ＋ 相容 `IOrganizationService`**，四個條件同時成立的使用者太少，養不出第二個實作。

### 3.2 有人做了與我們相同的移植（重要佐證）

[soroush-abn/DataverseClient](https://github.com/soroush-abn/DataverseClient) 於 **2026-06-21** 合併 `upgrade-net10` PR：

| 項目 | 該 fork | 我們 |
|---|---|---|
| TargetFramework | `net10.0` 單目標 | `net10.0` 單目標 ✅ |
| System.ServiceModel.Federation | 10.0.652802 | 10.0.652802 ✅ 完全相同 |
| System.Security.Cryptography.Xml | 10.0.9 | 10.0.10（我們較新） |
| NSspi | 完全移除 | 已排除（§2.2）✅ |

兩方獨立得出相同結論，是 §2.2 NSspi 排除決策的第三方佐證。該 fork README 另提供一個事實：**.NET 8+ 在 Linux 上原生支援 NTLM，不再需要 `gss-ntlmssp`**，這解釋了 NSspi 在現代 .NET 上徹底失去存在意義。

> ⚠️ 該 fork 為 0 star、單人維護，**不可作為上游依賴**，只作比對基準。

### 3.3 官方 DVSC 其實可以連 on-prem —— 但前提嚴苛

[janis-veinbergs/DataverseServiceClientOnPremSamples](https://github.com/janis-veinbergs/DataverseServiceClientOnPremSamples) 證明官方 DVSC 可連 on-prem，但走 **OAuth against ADFS 2019+**，不是 WS-Trust／Windows AD。

硬性前提：Windows Server 2019+ 且 ADFS 2019+、KB4490481、**必須設定 IFD**（作者實測「僅 claims、非 IFD 的內部存取」不work）、CRM 啟用 OAuth。

已知問題：多重 `WWW-Authenticate` header 導致連線失敗、non-IFD URL 有 open issue、ADFS 2016 不支援 MSAL、ROPC 不支援 MFA。Microsoft **無官方支援**此情境。

### 3.4 真正的架構替代品：Web API ＋ NetworkCredential

唯一**官方文件記載、零第三方依賴**的路徑。Microsoft on-prem 開發者指南直接給出：

```csharp
HttpClient client = new HttpClient(new HttpClientHandler() {
    Credentials = new NetworkCredential(userName, password, domainName)
});
```

- 官方文件，適用 **op-9-0 / op-9-1**（8.2 與 9.1 都涵蓋）
- 不需 WS-Trust、ADFS、OAuth、NSspi、第三方套件
- OData v4，CRM 2016（8.0）起可用
- ⚠️ 文件明說：**IFD 部署必須用 OAuth**，此路僅適用純 AD 部署

**代價**：完全不同的 API 面（OData REST vs `IOrganizationService`），不是 drop-in。實測遷移規模見 §4.3。

### 3.5 查過但不成立

| 專案 | 為何不成立 |
|---|---|
| [ttkoma/CrmNx.Xrm.Toolkit](https://github.com/ttkoma/crmnx.xrm.toolkit) | Web API ＋ AD auth，方向對；但 1 star、不實作 `IOrganizationService`、實質停更 |
| 其他 Data8 fork | `mohsinonxrm`（2025-11）實測無 2025-02 之後的變更；`djissam04` 是 `CreateAndReturnAsync` 貢獻者分支 |
| Python / Node | 僅 `requests` ＋ `requests-ntlm`、`soap-ntlm` 等手搭方案與部落格文章，無成熟函式庫 |

---

## 4. 生命週期：決定這條路能走多久的真正變數

### 4.1 Data8 不是先斷的那一環

Data8 是 7,162 行**我們自己擁有**的協定實作，而 WS-Trust 1.3 與 Dynamics 2011 SOAP 端點是**凍結的規格**。上游 2025-02 停更對我們無影響。協定不會腐壞，會腐壞的是底下的 .NET API。

### 4.2 四個時鐘（Data8 排最後）

| 時鐘 | 到期日 | 距 2026-08-07 |
|---|---|---|
| **CE 8.2 伺服器延伸支援** | **2026-01-13** | 🔴 **已過期** |
| CE 9.1 主流支援 | 2029-01-13 | 2.4 年 |
| CE 9.1 延伸支援 | 2031-01-10 | 4.4 年 |
| Data8 內部 `WebRequest` 被 .NET 移除 | 無公告 | 未知，修補有界 |

> 🔴 **CE 8.2 已於 2026-01-13 結束延伸支援**，此後不再有任何公開安全性更新。此風險與 Data8 無關，屬伺服器本身。
> **待決**：8.2 profile 目前定位（生產／遺留唯讀／測試）尚未確認，決定是否需要獨立風險處理任務。

### 4.3 Web API 遷移的實測規模

```
IOrganizationService 呼叫點：281 處
  RetrieveMultiple 114、Execute 71、Create 36
  Update 24、Retrieve 23、Delete 11、Associate/Disassociate 2
分佈：62 個非測試檔
```

**現在做不划算。** 但關鍵洞察：**架構上的接縫已經存在** —— Official Worker 走的不是 `IOrganizationService`，是 typed capability。P7.1–P7.5 本來就是「以 Data8 完成 ChurchReport 的 typed capability」。

**完成 P7 的 capability 層，等於把未來任何傳輸層替換的成本從 281 個呼叫點降到 capability 的數量。** 這不是為 Web API 而做，是既定工作的副產品。

### 4.4 Data8 內部唯一有日期壓力的東西

```
WebRequest.CreateHttp 用於 3 處：
  PowerPlatform.Dataverse.Client/ADAuthClient.cs:333
  PowerPlatform.Dataverse.Client/ADAuthHelpers/BaseAuthRequest.cs:49
  PowerPlatform.Dataverse.Client/Wsdl.cs:64
```

自 .NET 6 起標為 `SYSLIB0014` 過時，csproj 的 `NoWarn` 正在抑制。Microsoft 的模式是「過時 → 最終移除」。

**這是最可能在未來某個 .NET 版本強迫我們動 Data8 的東西**，不是 Data8 本身。修法明確：換成 `HttpClient`，3 處，約一天，非重寫。

**行動**：現在不改，但**每次 .NET 大版本升級時檢查 `NoWarn` 的 SYSLIB 清單**（目前含 `SYSLIB0004`、`SYSLIB0014`、`SYSLIB0051`）。

---

## 5. 決策與路線

### 5.1 決策

**維持 Data8 為 ChurchReport 永久主線。** Official Worker 保留為擴充點與驗證對照組，不承擔生產流量。Web API 保留為長期後備，現階段不啟動。

### 5.2 修正後的檢討觸發條件

先前的想法是「五年後再檢討」。**修正：檢討的觸發條件不是時間點，是事件。**

| 時間 | 動作 |
|---|---|
| **現在** | 確認 8.2 伺服器定位與風險承擔（§4.2 待決事項） |
| **現在–2027** | 照原計畫走 P7：typed capability 層。此層同時是傳輸替換的逃生門 |
| **每次 .NET 升版** | 檢查 `NoWarn` 的 SYSLIB 清單，特別是 `WebRequest`（§4.4） |
| **2028** | 檢討決策點：9.1 主流支援 2029-01 到期前一年 |
| ~~2031~~ | **不要**把決策點放在延伸支援到期日 |

可用區間是 **~2.4 年安心期 ＋ ~2 年延伸期**，不是 5–10 年。上限由伺服器決定，不由 Data8 決定。

### 5.3 何時才值得把 operation 搬到 Official Worker

需滿足任一：

- (a) Data8 在某個協定角落實作不完整，且我們修不動
- (b) 需要 8.2 與 9.1 真正同時在線，且 `sdkversion` 協商被證實不可行
- (c) 安全稽核要求 on-prem 驗證路徑必須是原廠程式碼

三者目前皆未發生。(b) 的驗證成本最低 —— 即 A1：以瀏覽器比對 8.2 伺服器對 `?wsdl&sdkversion=8` 與 `=9` 的回應。

---

## 6. 待決事項

| 編號 | 事項 | 影響 | 狀態 |
|---|---|---|---|
| D1 | **8.2 伺服器定位確認**（生產／遺留唯讀／測試） | 決定是否需要立即的風險處理任務 | 🔴 待使用者確認 |
| D2 | A1 驗證：8.2 對 `sdkversion=8` 與 `=9` 的回應比對 | 決定 `_sdkMajorVersion` 是否必須改為實例欄位 | 待辦（沿用既有 A1） |
| D3 | `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 → 1.2.26 | 影響 `Microsoft.Xrm.Sdk` 物件模型與 `ToolUtility/Adapters/DataverseServiceClientAdapter.cs:65` 的 `ServiceClient` 路徑 | 待另立任務 |
| D4 | 上游 2.4.2「Fixed errors when using claims based authentication」是否已含於我們的分叉 | 可能影響 `ClaimsBasedAuthClient.cs` | 待比對 |
| D5 | 孤兒 `PowerPlatform.Dataverse.Client/NSspi/NSspi.csproj` 是否刪除 | 純清理；原始碼已排除編譯 | 待使用者決定 |

---

## 7. 參考來源

**專案**
- [Data8/DataverseClient](https://github.com/Data8/DataverseClient) —— 上游，2025-02-20 後凍結
- [上游 commit e58a0ad「Updated DVSC package and target frameworks」](https://github.com/Data8/DataverseClient/commit/e58a0add37082725152e1541e250aaebf650b8ae)
- [soroush-abn/DataverseClient](https://github.com/soroush-abn/DataverseClient) —— 獨立的 net10 移植，比對基準
- [janis-veinbergs/DataverseServiceClientOnPremSamples](https://github.com/janis-veinbergs/DataverseServiceClientOnPremSamples)
- [ttkoma/CrmNx.Xrm.Toolkit](https://github.com/ttkoma/crmnx.xrm.toolkit)

**Microsoft 官方**
- [Dynamics 365 for Customer Engagement Apps, version 9.x (on-premises) — Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/dynamics-365-for-customer-engagement-apps-version-9x-onpremises-update)
- [Authenticate to Dynamics 365 CE with the Web API（on-premises）](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/webapi/authenticate-web-api?view=op-9-1)
- [NegotiateAuthentication Class（Applies to：net-7.0 起）](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.negotiateauthentication)

**社群**
- [Connecting to on-prem Dynamics from .NET Core without OAuth — Mark Carrington](https://markcarrington.dev/2021/11/15/connecting-to-on-prem-dynamics-from-net-core-without-oauth/)
- [On-Prem Dynamics CRM/365: AD authentication from .NET Core — Mark Carrington](http://markcarrington.dev/2022/03/10/on-prem-dynamics-crm-365-ad-authentication-from-net-core/)
- [Using Dataverse Service Client to connect to OnPrem Dynamics 365 CRM (From .NET 6+) — DEV](https://dev.to/janisveinbergs/using-dataverse-service-client-to-connect-to-onprem-dynamics-365-crm-from-net-6-2ic4)
- [End of mainstream support for Dynamics 365 CE On Premises v8.2 (CRM 2016)](https://community.dynamics.com/crm/b/crminthefield/posts/end-of-mainstream-support-for-microsoft-dynamics-365-ce-on-premises-v8-2-crm-2016)
