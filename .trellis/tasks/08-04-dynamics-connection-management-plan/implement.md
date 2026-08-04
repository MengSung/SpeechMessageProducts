# 執行計畫

## 權威來源

完整的分階段計畫位於：

**`docs/dynamics-connection-management-plan.md`**（8 階段、時程、風險緩解、明確排除項）

本檔不重複該計畫內容，僅記錄本任務的執行清單、驗證指令與審核關卡。

## 本任務範圍

**P1 → P4。** P5 以後另開任務（使用者決策 D1）。外部 CE 真機量測不屬本任務的阻塞條件；它由 P6 完成後的
跨模式整合驗收負責，且不可被離線結果取代。

| 階段 | 工作天 | 累計 | 產出 |
| --- | --- | --- | --- |
| A1～A3 前置 | 0.5 | 0.5 | 決策確認（使用者執行） |
| P1 契約層 | 2～3 | 3.5 | 型別與守門就緒 |
| P2 Data8 修正 | 1～2 | 5.5 | 無 Memory Leakage 底線成立 |
| P3 連線池 | 3～4 | 9.5 | 池化與世代就緒 |
| **P4 Embedded** | 2～3 | **12.5** | **★ F5 可跑，受控離線組合根就緒** |

## 執行清單

### 前置（阻塞 P2）

- [ ] **A1** 使用者：瀏覽器開啟 8.2 伺服器的 `?wsdl&sdkversion=8` 與 `=9`，比對回應
- [ ] **A2** 使用者：8.2 伺服器執行 `Get-CrmOrganization`，取得 `jesus` 的 OrganizationId

### P1　契約層對齊

- [ ] `DynamicsExecutionMode` → `ConnectionMode`（三值），更新所有引用點
- [ ] 新增 `ConnectorKind`、`CeVersion` 列舉
- [ ] `ProductDynamicsOptions` 精簡為三欄位
- [ ] 新增 `OrganizationCatalog` 型別與載入器，填入五個 Organization
- [ ] 新增 `IProfileResolver` / `ResolvedProfile`
- [ ] 抽出 `IRequestGuard`，補齊 G1 保留字檢查
- [ ] `Gateway/Security/*Authorizer` 接到 `IRequestGuard`
- [ ] 撰寫規格 §10.1（4 項）與 §10.2（4 項）測試

### P2　Data8 連接器修正

- [ ] `OnPremiseClient` 實作 `IDisposable`，保存 channel 與 ChannelFactory
- [ ] Dispose 依序 `Close()`，失敗則 `Abort()`；多個失敗以 `AggregateException` 彙總
- [ ] 檔頭註明本地修改內容與日期，保留 `Copyright © 2021 Data8 Limited`
- [ ] （視 A1）`_sdkMajorVersion` 改實例欄位，建構子可選傳入
- [ ] P6 後真機整合閘門：`sunnyvalechback` 建立→Dispose 100 次，Handle 無單調成長

### P3　連線池抽出與世代化（已完成，2026-08-04）

- [x] 新增 `SpeechMessage.Dynamics.Connectors.Data8` 專案（net10）
- [x] 移植 `CrmConnectionPool` → `Data8ConnectorPool`（複製後改造，`ToolUtility` 原檔不動）
- [x] 池鍵改為 `(ProfileAlias, GenerationId)`
- [x] 實作 `IConnectorLease`（含 `MarkFaulted`）
- [x] 接上既有 `DynamicsProfileRuntimeManager` 世代機制
- [x] 新增 `IConnectorRouter`，只讀 `ResolvedProfile.ConnectorKind`
- [x] 撰寫規格 §10.3（7 項）與 §10.4（8 項）測試
- [x] 以 focused lifecycle 測試驗證借出、歸還、取消、deadline、drain 與 dispose；真機 WhoAmI 不屬 P3 的完成條件。

### P4　Embedded 模式（程式與離線驗收完成；外部 CE 延後至 P6）

- [x] 重寫 `AddSpeechMessageDynamicsEmbedded`
- [x] 實作 `EmbeddedHostAdapter`（同進程呼叫，仍走完整 Guard→Resolver→Admission→Pool）
- [x] 設定映射器：既有 `CrmConnection` → `DynamicsProfiles` ＋ `OrganizationCatalog`
- [x] ChurchReport `appsettings.Development.json` 設為 `ConnectionMode: Embedded`
- [x] 撰寫規格 §10.5 與 P4 lifecycle／isolation 測試
- [x] 準備 opt-in legacy／Embedded p50／p95／p99 對照工具；不提供密碼時安全略過
- [ ] P6 後真機整合閘門：VS 2026 F5 → legacy／Embedded／Dedicated 非破壞性工作負載結果一致，並取得 p50／p95／p99

## 驗證指令

```powershell
# 主測試套件
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --nologo

# Release 建置
dotnet build SpeechMessageProducts.sln --configuration Release --nologo

# SQL 持久協調器（可選，非本任務必要條件）
$env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;TrustServerCertificate=true;"
dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --nologo --filter "FullyQualifiedName~Live_sql_"
```

**基準（2026-08-04 實測）**：411 通過 / 0 失敗 / 7 略過；另 SQL live 8 項通過。任何階段結束時不得低於此基準。

## 審核關卡

| 關卡 | 時機 | 條件 |
| --- | --- | --- |
| G-0 | P2 開始前 | A1 結果已知；`_sdkMajorVersion` 是否必修已確定 |
| G-1 | P3 開始前 | P2 離線 lifecycle／soak 測試通過；100 次真機建立／Dispose 延後至 P6 整合閘門 |
| G-2 | P4 開始前 | P3 的 §10.4 洩漏測試與 soak 全綠 |
| G-3 | 任務完成 | P4 程式、離線測試、Release build 與編碼驗收通過；真機量測已移交 P6 後整合閘門 |

## 回滾點

| 變更 | 回滾方式 |
| --- | --- |
| Data8 `IDisposable` 修正 | 還原 `OnPremiseClient.cs`；legacy 路徑本就未依賴此行為 |
| `Data8ConnectorPool` | 新專案，直接停用註冊即可；`ToolUtility` 原池未被修改 |
| `ConnectionMode` 改名 | 型別層變更，以編譯錯誤定位所有引用點 |
| Embedded 啟用 | `appsettings` 改回 `ConnectionMode: Gateway`；`Package01FeeReadsEnabled` 全程為 `false`，legacy 路徑不受影響 |

## 明確不做

- 移除 Data8（永久保留）
- 刪除 63 檔 Worker 資產（保留為擴充點）
- 強制使用 SQL（非必要條件）
- Central Gateway（只有一個產品時無收益）
- 為 Official Worker 寫平行行為測試（不承諾兩種 Connector 行為一致）
