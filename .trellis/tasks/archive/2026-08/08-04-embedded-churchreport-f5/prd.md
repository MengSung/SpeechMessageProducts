# P4：ChurchReport Embedded F5 PRD

## 目標

讓 ChurchReport 在 Visual Studio 2026 以 `ConnectionMode=Embedded` 與 `ProfileAlias=sunnyvalechback` 按 F5 時，透過既有受控 Dynamics 管線存取資料；Embedded 只省略 HTTP transport，不得省略任何授權、設定解析、容量或連線池生命週期規則。

## 使用者價值

開發者不必先啟動獨立 Gateway，即可在本機偵錯 ChurchReport；產品仍只選擇 ProfileAlias，不能攜帶 OrganizationId、CRM endpoint、ConnectorKind 或 Credential。

## 範圍

1. 實作 `AddSpeechMessageDynamicsEmbedded` 與 `EmbeddedHostAdapter`。
2. 走完整順序：`RequestGuard → ProfileResolver → Organization Admission → IConnectorRouter → Data8ConnectorPool`。
3. 由既有 ChurchReport `CrmConnection` 設定衍生受控 `DynamicsProfiles`／`OrganizationCatalog` 設定；不建立產品端第二份 Profile 檔。
4. Development 設定可選 `ConnectionMode=Embedded` 與 `ProfileAlias=sunnyvalechback`，且 Embedded 不要求 `Gateway.Endpoint`。
5. P4.1 將已取得的 CE 8.2／9.1 Organization GUID 寫入既有 `CrmConnection:OrganizationCatalog`；產品只選 alias，
   Catalog 再決定 GUID、版本、Enabled 狀態與已核准的 ServiceUri。

## 排除項

不實作 Dedicated Gateway、Central Gateway、Official Worker、Web API、IFD、D365APP01、SQL、IIS、DNS 或 ADFS 管理與診斷；不移除 Data8 或 ToolUtility legacy pool，並維持 `Package01FeeReadsEnabled=false`。

## 驗收條件

- Embedded 與 Gateway 使用相同的 Guard 判定；未授權／保留參數在取得 permit 或 client 前 fail closed。
- Profile 與 Organization 不會跨租用、跨快取或跨連線池混用。
- cancellation、deadline、drain、dispose 後，沒有 permit、client、timer、task、handle 或 session 遺留。
- 同一能力操作的 Embedded 結果與 legacy 對照路徑一致。
- 量測 legacy 與 Embedded p50／p95／p99；Embedded p95 不差於 legacy。
- 所有新增或實質修改的 C# 檔均有深入繁體中文生命週期註解、UTF-8 無 BOM、CRLF 與最後 CRLF。
- CE 8.2 組織若尚未提供對應 HTTPS ServiceUri，選取時必須在 permit／client 建立前 fail closed；不得使用 9.1 的 URI 猜測連線。
- P4 的離線程式與設定驗收完成後，可以進入 P5／P6；真實 CE WhoAmI、legacy／Embedded／Dedicated 的結果一致性，
  以及 p50／p95／p99 比較，統一延後到 P6 完成後的整合量測閘門，不可由模擬結果取代。
