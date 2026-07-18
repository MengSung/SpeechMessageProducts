# MemberInfo 受控參考實作

> **Evidence only（僅供證據比對）**：本目錄保存來源版本的功能檔案快照、測試契約與 path-limited 歷史差異。它不是可直接覆蓋目標系統的發行套件，也不是可直接套用的 migration script。

## 來源與邊界

- Snapshot source branch：`Sunny_5.1.2.WorktreeTuneMemberView`
- Snapshot source commit：`2406b126e989cc980e8cada9da0e07a2ede1e08d`；patch 1–5 與其他 snapshot 的歷史來源和既有遮罩狀態維持不變。本次 metadata 排序增量同步 11 份 source/test snapshot，其中 10 份 privacy scan 零命中並與 authoritative files byte-for-byte 相同；`MemberInfoTreeSearchBuilderTests.cs` 的姓名 fixture 已泛化為 sanitized derivative，既有 `DistrictTreeBuilderTests.cs` 也維持 sanitized derivative。
- `feature-files/`：15 份目前來源檔，依 repo-relative path 保存。
- `tests/`：16 份 C# 測試與 1 份 test project，共 17 份檔案。
- 32 份 snapshot 中有兩份明確記錄的 fixture-bearing sanitized derivatives：既有 `DistrictTreeBuilderTests.cs`，以及本次 privacy scan 命中姓名後泛化的 `MemberInfoTreeSearchBuilderTests.cs`；其餘本次 10 份 metadata 排序相關 snapshot 保持 byte identity。
- `host-integration/`：六段特定 commit endpoints 的 path-limited Git diff，只保存 host 整合證據。
- 完整 Controller 與 Razor View 不加入 `feature-files/`；它們仍只存在於 host patches，避免把來源版 host integration 誤當成可覆蓋快照。

本目錄不包含 `appsettings*`、publish profile、`bin/`、`obj/`、runtime log、CRM export、真實照片或憑證值。Patch 中的 `ChannelAccessToken` 是組態 key 名稱，`m_Password` 是既有欄位取用名稱；兩者都不是 literal credential。本次來源 patch 未發現 literal secrets、email、URL/IP、憑證或嵌入內容的 absolute filesystem path，但姓名 fixture 已依政策泛化，因此 patch 06 的交付檔是保留 13-path lineage 的 sanitized derivative；原始 Plan 中的本機路徑、連接埠與 PID 也已泛化。

## 本次 source/snapshot/patch 雜湊

| Authoritative source | Byte-identical snapshot | SHA-256 |
|---|---|---|
| `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs` | [DistrictTreeInputs.cs](feature-files/ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs) | `c389db6833ab3ee4d87284f034886725c42bb1a4696389a12e5bb4a6108691ba` |
| `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs` | [DistrictTreeBuilder.cs](feature-files/ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs) | `e0ade55eaa70ad0a6f64d2ebd8e17d6aa3c02532660adc251afd8a7c6c569500` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs` | [MemberInfoCommitmentTypeMetadataProvider.cs](feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs) | `e8d5165b4fe48d13272fee9a6d0c2f63d0b54af9ac54e782fe876a9a63c6a834` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs` | [MemberInfoCommitmentTypeCountQuery.cs](feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs) | `48746baa2379db90ee51219ba140bbfbd425718674a33a53b3f4112ea4cc5a97` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs` | [MemberInfoCommitmentTypeSort.cs](feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs) | `980bfa2bb115f9e0503be78d3e595cc3dcf73c06e8eebf7d0528f089d4ba6303` |
| `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs` | [MemberInfoTreeSearchBuilder.cs](feature-files/ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs) | `08cab1abe638cdcc9d244b475bb8e255a294d1283564fa11a6000d2172627d83` |
| `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs` | [DistrictTreeViewModels.cs](feature-files/ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs) | `c36ea1a53a305bc666e60c507283b84d1857cb1dedf46a9bd965096541a62298` |
| `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs` | [DistrictTreeBuilderTests.cs](tests/ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs) | source `bbc03c46c202ff6c8b1b2bc9f5083b477ca277a8f8a10b9b7fb7de0b86dd87d5` → sanitized snapshot `c3eff05c59e13938faf27372c42a6f4ceed49c5b93e416a7dc0bb15009650ba9` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs` | [MemberInfoCommitmentTypeMetadataProviderTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs) | `205317760b5751cc851034d22c6eaa6a77889ec2c9595f195d66d1bbeffae977` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs` | [MemberInfoCommitmentTypeCountQueryTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs) | `385e5f5b6d8bb4995834bfa59cac5a5295d61c304314811059e0b6ddace52eb9` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs` | [MemberInfoCommitmentTypeSortTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs) | `1c4c20782fc9d73c0522164d02e25cd0fe46ce4e34c2c1d71c27bd1300a2bfbe` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs` | [MemberInfoTreeControllerContractTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs) | `c31c47b0d915d0047f75a56300ec8451dd64d4c07acf13eb8ba292674e6430ce` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs` | [MemberInfoTreeSearchBuilderTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs) | source `b752fdf81ab343738499f313eec2139bc1dedda853b01f2b3a5ac30cfcd8e9f8` → sanitized snapshot `a365b2f0f41184bd042c5339685fa44c920526c6b6eb4f29a1148123a6b993d5` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs` | [MemberInfoTreeViewContractTests.cs](tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs) | `ed638c2554a479deecb31779dbc5ebebfebd8944b2ba8bc805357e5403528483` |
| Patch `a7f497bd…589f0baa`（8 paths） | [05-member-info-column-order-group-metadata.patch](host-integration/05-member-info-column-order-group-metadata.patch) | `56890aeadf06daaa4dd7424ef4aec69f5c2063fd8558981eb398affbcdd68b3b` |
| Patch `589f0baa…2406b126`（13 paths） | [06-member-info-commitment-type-metadata-order.patch](host-integration/06-member-info-commitment-type-metadata-order.patch) | raw source `bd12b70d6d465ebe00da7aa1b4dc11eeb5e09a5a6096bf6ea2b508c6e79d988b` → sanitized delivery `45b86e0185329b8db94129f3a1296b426d6c61be90c476e0bfca192c4e611240` |

## 使用前先盤點目標版本

在移植任何內容前，先記錄並確認目標 repo 的：

1. branch、HEAD、working-tree 狀態，以及現有 MemberInfo/ContactAvatar 實作；
2. target framework、C# language/runtime、ASP.NET MVC/Razor 與 DevExtreme client/server 版本；
3. namespace、project layout、project references、DI registration 與 controller base class；
4. Newtonsoft JSON contract resolver；此來源依賴 PascalCase DTO contract；
5. CRM/Dataverse table、logical field、OptionSet、relationship 與 metadata 權限；不可臆造欄位；
6. Church/Shepherd 授權來源、`ListManager` 載入契約與 session/claims 行為；
7. avatar 儲存、ImageSharp、LINE profile 欄位與安全的 token 組態 lookup；
8. `IMemoryCache` 行為、Razor partial、jQuery/DevExtreme DataGrid 與現有 routes；
9. 可執行的 test project、fixture 路徑與 package versions。

若 CRM logical names、授權來源、照片儲存方式或 LINE credentials contract 任一項無法確認，應停止實作並向目標系統維護者取得資料；不要以 Sunny 的假設填補缺口。

## Snapshot 不是 drop-in source

`feature-files/` 保存的是來源證據。移植到其他教會版本時，仍必須依目標系統調整 namespace、framework/API、schema、authorization、DI、cache、Razor/route 與 package versions。保留目標系統原有安全邊界，並以 `tests/` 的契約逐項重建或調整測試；不要整批覆蓋 application source。

## Host patches 不是套用指令

六份 patch 都不可盲目執行 `git apply`。Patch 01/02 與 patch 06 先由 Git 產生再做隱私遮罩，已明確不是可套用的原始 Git patch；patch 03/04/05 雖是機械產生的原始 path-limited diff，仍綁定精確 endpoints、DevExtreme DOM、CRM schema 與來源版互動行為。先依 [SOURCE-MAP](host-integration/SOURCE-MAP.md) 核對 endpoints、path scope、controller contract 與 dependency，再逐 hunk 對照目標版本，人工重建必要變更並執行目標 repo 的測試與 diff review。

[01-photo-prerequisite.patch](host-integration/01-photo-prerequisite.patch) 很大，是因為其 base `3fcd5d7e…` 尚無完整的 MemberInfo controller、Razor views、detail view model 與 ContactAvatar host files；Git 因而把多個檔案呈現為整份新增。這只解釋 patch 大小，不表示整份覆蓋目標版本是安全的。

[03-member-info-fixed-identity-columns.patch](host-integration/03-member-info-fixed-identity-columns.patch) 只保存 2026-07-17 增量：固定左側頭像與姓名欄、讓 fixed overlay 的水平 touch gesture 轉送到同一個 DataGrid scrollable，並加入契約測試。這個橋接依賴來源端 DevExtreme 22.1.6 的 `.dx-datagrid-content-fixed` overlay 結構、`getScrollable()`／`scrollBy()` API 與 `onContentReady` 重繪行為；目標版本若不同，DOM 或 gesture/click propagation 都可能不同。移植前必須先確認實際 DevExtreme client 版本，再以目標裝置驗證從固定欄開始的左右滑、頁面上下滑、滑動後 click 抑制與唯一水平捲軸，**不可直接盲套**。

[04-member-info-resizable-sortable-columns.patch](host-integration/04-member-info-resizable-sortable-columns.patch) 是後續欄位增量：姓名欄改為 96px／最小 80px，頭像欄禁止調寬，兩個 DataGrid mount 都啟用 `allowColumnResizing: true`、`columnResizingMode: 'widget'` 與 `sorting: { mode: 'single' }`。這些設定依賴來源端 DevExtreme 22.1.6 client；`widget` 模式會改變 grid 總寬，目標版必須重驗單一水平捲軸與 fixed columns。Remote-paged Ungrouped grid 的 `RelationGoals` 是 client 組合顯示值，必須保留 `allowSorting: !remotePaging` 以避免送出無效 remote sort。欄寬拖曳來自 header resize handles，patch 3 的 touch bridge 則只綁定 fixed rows overlay；移植時要分別驗證 header drag 與 fixed rows 水平滑動，不可讓橋接攫截表頭調寬事件，**不可直接盲套**。

[05-member-info-column-order-group-metadata.patch](host-integration/05-member-info-column-order-group-metadata.patch) 以 base `a7f497bd2ac69cd7c2af2bcc76be40bc71967a63` 到 End `589f0baa3d53588ffd60c6c602472bd0779ef2e8` 保存最新 8-path 增量：姓名欄改為 `62px` 並移除應用程式 `minWidth`，欄位順序固定為頭像、姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別；區標頭同時顯示小組數與本區人數，小組時間／地點只有非空時才顯示。時間、地點與 `GroupCount` 經既有 descriptor/tree 流程一次傳遞，不新增逐組查詢。此 raw patch 與 fresh Git diff byte-for-byte 相同，但仍只是 evidence，**不可直接盲套**。

[06-member-info-commitment-type-metadata-order.patch](host-integration/06-member-info-commitment-type-metadata-order.patch) 接續 patch 05 的 end，以 base `589f0baa3d53588ffd60c6c602472bd0779ef2e8` 到 End `2406b126e989cc980e8cada9da0e07a2ede1e08d` 保存 13-path 增量：新增 metadata provider、aggregate count query、Configured／Unknown／Empty 共用排序與 tests；DTO 加入 rank／has-value；一般小組與搜尋在授權後排序；Ungrouped 先計數／分段再跨頁；DataGrid visible label 不變而 local／remote selector 改用 rank。交付檔只把姓名 fixture 泛化，未改動 paths、hunks 或技術契約，因此屬可追溯的 sanitized derivative；它仍只是 **EVIDENCE-ONLY**，目標教會必須讀自己的 metadata，不可複製 Sunny values／labels／順序。

## 閱讀順序

1. 先讀本檔，確認 evidence-only 邊界與目標版本盤點項目。
2. 讀 [host-integration/SOURCE-MAP.md](host-integration/SOURCE-MAP.md)，理解 endpoints、routes、dependencies 與 allowlist。
3. 依底層到上層閱讀 [MemberInfo services](feature-files/ChurchReport/Services/MemberInfo/)、[ContactAvatar helpers](feature-files/ChurchReport/Services/ContactAvatar/)、[view models](feature-files/ChurchReport/ViewModels/)。
4. 讀 [test project](tests/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj) 與同目錄測試，將它們當作行為契約而非可直接複製的 build 設定。
5. 最後才逐 hunk 比對 [photo prerequisite patch](host-integration/01-photo-prerequisite.patch)、[July-15-plus patch](host-integration/02-member-info-2026-07-15-plus.patch)、[fixed identity columns patch](host-integration/03-member-info-fixed-identity-columns.patch)、[resizable/sortable columns patch](host-integration/04-member-info-resizable-sortable-columns.patch)、[column order/group metadata patch](host-integration/05-member-info-column-order-group-metadata.patch) 與 [commitment-type metadata order patch](host-integration/06-member-info-commitment-type-metadata-order.patch)。
