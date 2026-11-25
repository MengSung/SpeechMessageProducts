# FS 結構分層與三組織佈署

更新：2026-11-26 07:27 Asia/Taipei (UTC+08:00)
工作包起點：`ffcccffcba89cc57d72337189da684df2c2933b8`
FS 實作提交：`64b801dbb28c9d29c5fdd3613c417fb2bdd6442c`
狀態：完成（自動測試通過）；三個測試組織的實機佈署與 UI 發布待端點核定後補做。

## 驗收結果

| 條件 | 結果與證據 |
|---|---|
| 1. `construction`、`association` 的 plan 不含教會專屬實體 | 通過離線驗證。`Plan_ConstructionOrg_ContainsNoChurchEntities`、`Layers_DuplicateEntityAcrossLayers_Rejected` 與 `Layers_PlatformFolder_EntityExtendedAcrossFiles_Merged` 均由 `SchemaLayerTests` 覆蓋；construction／association 只組合 `platform` 與各自空白產品層，沒有 `new_fee`、`new_offeringcategory`、`new_dedication_booking` 或 `new_stor_lessons`。 |
| 2. 同一平台層清單可供三個組織使用並驗證 | 通過離線組合與衝突守門。`LoadForOrganization` 依 `layers.json` 及平台資料夾排序載入，並驗證實體、欄位、ownership、檢視表與唯一鍵。三組織的 D365 `plan`／`apply`／`verify` 尚未執行，實機待補。 |
| 3. 部署指令與 Plugin 註冊依 `--org` 選組織，既有 goodshepherdback 行為保留 | 通過。`Catalog_MultipleTestOrganizations_ResolvedByName`、`Catalog_MultipleTests_WithoutOrg_Refused`、`Registration_OrgFlag_OnlyEnabledStepsRegistered` 通過；catalog 不再假設唯一 test 組織，Plugin 明確 `enabledByOrganization=false` 時不建立新 step。完整 `SpeechMessage.Dynamics.Deployment.Tests` 為 `1085/1085` 通過。 |
| 4. 正式組織被擋下，兩個工作樹對同一組織的寫入會排隊 | 通過離線驗證。`Catalog_ProductionKind_NeverReturnedAsTest`、`Quiesce_ProductionOrganization_Refused`、`WriteLock_TwoRepositoryRoots_SameOrganization_Serialized` 通過；共享鎖採 local → shared 取得、shared → local 釋放，取消及 I/O 失敗會釋放已取得的 stream。 |
| 5. 平台檢視表由程式產生，發布範圍以實體限縮 | 通過離線驗證。`Views_GeneratedFetchXml_OnlyAllowListedAttributes`、`Views_UnknownAttribute_Rejected`、`Views_SecondDefaultPublicView_Refused`、`Views_SpecialViewCreate_Refused`、`Publish_UsesEntityScopedPublishXml_NotPublishAll` 通過；檢視產生器固定 QueryType 0，發布不使用 `PublishAllXml`。 |
| 6. 計畫外未發布變更與 XML 指紋漂移拒絕，部分失敗只重試缺項 | 通過離線驗證。`Publish_UnplannedPendingChange_Refused`、`Ui_FingerprintDrift_Refused`、`Ui_PartialFailure_ReplanOnlyMissing` 通過；ledger 以正式元件 ID 與 XML SHA-256 維持穩定身份，不按顯示名稱覆蓋。 |
| 7. 新實體 ownership 必填且租戶業務資料不可使用 OrganizationOwned | 通過離線驗證。`Schema_NewEntityWithoutOwnershipType_PlanFails`、`Schema_TenantBusinessEntityOrganizationOwned_PlanFails` 及 `Layers_DuplicateAttributeAcrossFiles_Rejected` 通過；legacy schema 的 nullable ownership 僅保留既有 canonical hash，不讓新分層實體繞過檢查。 |

## 自動閘門

- `dotnet build .\SpeechMessageProducts.sln -c Release`：通過，0 errors；既有 warnings 共 65 個，沒有因本包放寬警告或排除檢查。
- `dotnet test .\SpeechMessageProducts.sln -c Release --filter "Category!=P7Live&Category!=P8Live&Category!=PaymentsLive&Category!=SqlIntegration&Category!=Live3P"`：通過，所有測試回合均 0 失敗；`SpeechMessage.Dynamics.Deployment.Tests` 為 `1085/1085`，`SpeechMessage.Hosting.Tests` 為 `168/168`，`SpeechMessageProducts.GoodShepherd.Hosting.Tests` 為 `137/137`。命令取得 exit 0。
- `python tools/RepoQuality/check_file_documentation.py`：`checked=1920, failures=0`。
- `Test-TextEncoding.ps1 -Scope Changed -Base ffcccffcba89cc57d72337189da684df2c2933b8`：`checked=22, filesWithIssues=0`。
- `dotnet sln .\SpeechMessageProducts.sln list`：通過；方案內每個專案各列一次，既有專案未移除。
- 外部審查：本輪依使用者明確指示不執行雙模型審核；不把未執行外部審查寫成已完成審查，也不因此放寬上述自動閘門。終點仍須依使用者後續授權及 02 §9 規則處理。

## 實機待補

使用者尚未核定 `goodshepherd20`、`construction`、`association` 的 IFD HTTPS 端點，因此本包沒有連線任何 D365 組織，也沒有執行下列操作：

- FS Step 3b：`quiesce --org goodshepherd20` 的盤點與套用。
- 三個組織的 `plan --org`、`apply --org`、`verify --org`。
- `goodshepherd20` 的 `ui-export --solution SpeechMessagePlatformUi` 與 construction 的 `ui-import`。
- 讀回三組織已發布 `savedquery` 的 FetchXml、LayoutXml 與啟用狀態。

上述缺項已依 02 §6 記在 `C:\ProgramData\SpeechMessage\lanes\需要你協助.md`；離線結果不代表實機結果。端點核定後，在取得測試環境鎖且不連正式組織的前提下補做。

## 手機待驗

FS 沒有直接手機操作或手機驗收條件。D365 UI 的畫面與已發布檢視表以機器證據及 M1 前的實機補驗為準；手機畫面仍集中於 M1，未在本報告宣稱完成。

## 範圍與安全

本包只修改部署工具、schema／UI 離線候選、測試、測試組織清單及文件 companion；未連線正式組織、正式 Channel 或正式金流。現有 `.ccg` 狀態檔未加入提交。新組織目前保留空 host，connector 在建立 client 前拒絕待補端點，避免猜測 URL 或意外連線。
