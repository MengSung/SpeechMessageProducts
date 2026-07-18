# MemberInfo host integration source map

> **只供閱讀／適配，不可直接 `git apply` 到其他教會版本。** 本文件索引來源證據；所有 paths 都相對於來源 repo root，內容不含 secrets、真實 CRM records 或真實影像。

## Snapshot map

| 類別 | 來源 | 受控副本 | 檔案數 |
|---|---|---|---:|
| Feature services/view models | `ChurchReport/Services/MemberInfo/*.cs`、指定 ContactAvatar helpers、`MemberInfoDetailViewModel.cs`、`MemberInfoTree/*.cs` | `../feature-files/` | 15 |
| Test contracts | `ChurchReport.MemberInfo.Tests/*.cs` 與 test project | `../tests/` | 17 |

Snapshot source branch 是 `Sunny_5.1.2.WorktreeTuneMemberView`，目前封裝來源 commit 是 `2406b126e989cc980e8cada9da0e07a2ede1e08d`。既有 patch 1–5 與其他 snapshot 的歷史來源、遮罩狀態不變；本次 metadata 排序增量同步 11 份 source/test snapshot，其中 10 份 privacy scan 零命中並保持 source/snapshot byte-for-byte 相同，`MemberInfoTreeSearchBuilderTests.cs` 因姓名 fixture 泛化而是 sanitized derivative。既有 `DistrictTreeBuilderTests.cs` 也維持有記錄的 sanitized derivative。完整 Controller 與 Razor View 不加入 `feature-files/`，仍只以 host patch 保存整合證據。

| Authoritative source | Snapshot | Lineage／SHA-256 |
|---|---|---|
| `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs` | [DistrictTreeInputs.cs](../feature-files/ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs) | `c389db6833ab3ee4d87284f034886725c42bb1a4696389a12e5bb4a6108691ba` |
| `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs` | [DistrictTreeBuilder.cs](../feature-files/ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs) | `e0ade55eaa70ad0a6f64d2ebd8e17d6aa3c02532660adc251afd8a7c6c569500` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs` | [MemberInfoCommitmentTypeMetadataProvider.cs](../feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs) | `e8d5165b4fe48d13272fee9a6d0c2f63d0b54af9ac54e782fe876a9a63c6a834` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs` | [MemberInfoCommitmentTypeCountQuery.cs](../feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs) | `48746baa2379db90ee51219ba140bbfbd425718674a33a53b3f4112ea4cc5a97` |
| `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs` | [MemberInfoCommitmentTypeSort.cs](../feature-files/ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs) | `980bfa2bb115f9e0503be78d3e595cc3dcf73c06e8eebf7d0528f089d4ba6303` |
| `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs` | [MemberInfoTreeSearchBuilder.cs](../feature-files/ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs) | `08cab1abe638cdcc9d244b475bb8e255a294d1283564fa11a6000d2172627d83` |
| `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs` | [DistrictTreeViewModels.cs](../feature-files/ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs) | `c36ea1a53a305bc666e60c507283b84d1857cb1dedf46a9bd965096541a62298` |
| `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs` | [DistrictTreeBuilderTests.cs](../tests/ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs) | sanitized derivative；source `bbc03c46c202ff6c8b1b2bc9f5083b477ca277a8f8a10b9b7fb7de0b86dd87d5` → snapshot `c3eff05c59e13938faf27372c42a6f4ceed49c5b93e416a7dc0bb15009650ba9` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs` | [MemberInfoCommitmentTypeMetadataProviderTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs) | `205317760b5751cc851034d22c6eaa6a77889ec2c9595f195d66d1bbeffae977` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs` | [MemberInfoCommitmentTypeCountQueryTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs) | `385e5f5b6d8bb4995834bfa59cac5a5295d61c304314811059e0b6ddace52eb9` |
| `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs` | [MemberInfoCommitmentTypeSortTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs) | `1c4c20782fc9d73c0522164d02e25cd0fe46ce4e34c2c1d71c27bd1300a2bfbe` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs` | [MemberInfoTreeControllerContractTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs) | `c31c47b0d915d0047f75a56300ec8451dd64d4c07acf13eb8ba292674e6430ce` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs` | [MemberInfoTreeSearchBuilderTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs) | sanitized derivative；source `b752fdf81ab343738499f313eec2139bc1dedda853b01f2b3a5ac30cfcd8e9f8` → snapshot `a365b2f0f41184bd042c5339685fa44c920526c6b6eb4f29a1148123a6b993d5` |
| `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs` | [MemberInfoTreeViewContractTests.cs](../tests/ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs) | `ed638c2554a479deecb31779dbc5ebebfebd8944b2ba8bc805357e5403528483` |

## Patch 1：photo prerequisite

- 檔案：[01-photo-prerequisite.patch](01-photo-prerequisite.patch)
- Base：`3fcd5d7ed11df16c1b487ac6917534291de2914a`（解析結果等於 `2471ea4e^`）
- End：`1704380554c9539135bfb6ca793793ae89369487`
- 產生方式：Git 先以 `--output` 寫入 exact path，未使用 shell redirection；封裝前再做隱私遮罩。下列命令說明 raw lineage，無法 byte-for-byte 重建 sanitized 交付檔。

```powershell
git diff --output=docs/portable/member-info-portable-kit/reference-implementation/host-integration/01-photo-prerequisite.patch 3fcd5d7ed11df16c1b487ac6917534291de2914a 1704380554c9539135bfb6ca793793ae89369487 -- ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs ChurchReport/ViewModels/MemberInfoDetailViewModel.cs ChurchReport/ChurchReport.csproj
```

Allowlisted paths，也是本 patch 實際出現的 7 個 diff headers：

- `ChurchReport/ChurchReport.csproj`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs`
- `ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs`
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`

此 base 尚未包含完整 MemberInfo host files：controller、兩個 Razor views、兩個 ContactAvatar helpers 與 detail view model 在 diff 中都是新增，只有 project file 是修改。因此 patch 約 189 KB 且包含大量整檔新增；這不代表在另一個版本整份覆蓋是安全的。

## Patch 2：MemberInfo changes from 2026-07-15-plus range

- 檔案：[02-member-info-2026-07-15-plus.patch](02-member-info-2026-07-15-plus.patch)
- Base：`8ebb47a0e1615b3a6f0e5425ec7b42b813433dc2`
- End/package source commit：`320ab43851c8ca5194dae02840c710c0e921bc83`
- `Startup.cs`、ContactAvatar helpers、部分目前 snapshot/test files 位於 allowlist，但 endpoints 間若沒有差異，Git 不會產生對應 header。
- 下列命令說明 raw lineage；交付 patch 已依隱私政策泛化 fixture，不能 byte-for-byte 重建，也不可直接套用。

```powershell
git diff --output=docs/portable/member-info-portable-kit/reference-implementation/host-integration/02-member-info-2026-07-15-plus.patch 8ebb47a0e1615b3a6f0e5425ec7b42b813433dc2 320ab43851c8ca5194dae02840c710c0e921bc83 -- ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs ChurchReport/ViewModels/MemberInfoDetailViewModel.cs ChurchReport/ChurchReport.csproj ChurchReport/Startup.cs ChurchReport/Services/MemberInfo ChurchReport/ViewModels/MemberInfoTree ChurchReport.MemberInfo.Tests
```

完整 pathspec allowlist：

- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`
- `ChurchReport/Services/ContactAvatar/ContactAvatarUrl.cs`
- `ChurchReport/Services/ContactAvatar/DefaultAvatarSvg.cs`
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `ChurchReport/ChurchReport.csproj`
- `ChurchReport/Startup.cs`
- `ChurchReport/Services/MemberInfo/**`
- `ChurchReport/ViewModels/MemberInfoTree/**`
- `ChurchReport.MemberInfo.Tests/**`

本 patch 實際出現 22 個 diff headers：

- `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoCurrentContactCounterTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoDetailContractTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardListTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- `ChurchReport.MemberInfo.Tests/RelationGoalFormatterTests.cs`
- `ChurchReport/ChurchReport.csproj`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoCurrentContactCounter.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- `ChurchReport/Services/MemberInfo/RelationGoalFormatter.cs`
- `ChurchReport/ViewModels/MemberInfoDetailViewModel.cs`
- `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport/Views/MemberInfo/_MemberDetailPopup.cshtml`

## Patch 3：2026-07-17 fixed identity columns increment

- 檔案：[03-member-info-fixed-identity-columns.patch](03-member-info-fixed-identity-columns.patch)
- Base：`320ab43851c8`
- End：`b3c50550deefb9cb7031ea938fce592366459448`
- 涵蓋範圍：只固定左側頭像（`ContactId`）與姓名（`FullName`）欄、把 fixed overlay 的明確水平 touch gesture 轉送至單一 DataGrid scrollable，以及對應 contract tests。
- 此檔由下列 path-limited 命令機械產生；未手工編輯應用程式來源或 patch 內容。

```powershell
git diff --output=docs/portable/member-info-portable-kit/reference-implementation/host-integration/03-member-info-fixed-identity-columns.patch 320ab43851c8 b3c50550deefb9cb7031ea938fce592366459448 -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

本 patch 實際且僅有 2 個 diff headers：

- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

> **只供閱讀／適配，不可直接 `git apply` 到其他教會版本。** 固定欄橋接針對來源端 DevExtreme 22.1.6 的 fixed overlay DOM 與 touch 行為；移植前必須確認目標 client 版本、`.dx-datagrid-content-fixed` 結構、`getScrollable()`／`scrollBy()` API、`onContentReady` 重繪生命週期與實機 gesture/click 行為，再逐 hunk 適配。

## Patch 4：2026-07-17 resizable and sortable columns increment

- 檔案：[04-member-info-resizable-sortable-columns.patch](04-member-info-resizable-sortable-columns.patch)
- Base：`526b533d4b37644df8ed7bd6332ac5df2e4336f6`（解析自 `526b533d4`）
- End：`b238d96871fdd490a2a0493e27869753e86baae8`
- 涵蓋範圍：姓名欄改為 `width: 96` 與 `minWidth: 80`、頭像欄禁止調寬，並在兩個 DataGrid mount 啟用 DevExtreme `widget` 調寬與 `single` 排序，同時增加對應 contract tests。
- 此檔由下列 path-limited 命令機械產生；未手工編輯應用程式來源或 patch 內容。

```powershell
git diff --output=docs/portable/member-info-portable-kit/reference-implementation/host-integration/04-member-info-resizable-sortable-columns.patch 526b533d4b37644df8ed7bd6332ac5df2e4336f6 b238d96871fdd490a2a0493e27869753e86baae8 -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

本 patch 實際且僅有 2 個 diff headers：

- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

> **只供閱讀／適配，不可直接 `git apply` 到其他教會版本。** 這個增量綁定來源端 DevExtreme 22.1.6 client 的欄寬、fixed columns、header resize handles 與 remote sorting 行為。目標版本必須保留 remote-paged `RelationGoals` 的排序禁用邏輯，並分別驗證 header 調寬與 fixed rows touch bridge，不可假設目標 DOM 與來源版一致。

## Patch 5：2026-07-17 column order and group metadata increment

- 檔案：[05-member-info-column-order-group-metadata.patch](05-member-info-column-order-group-metadata.patch)
- Base：`a7f497bd2ac69cd7c2af2bcc76be40bc71967a63`
- End/package source commit：`589f0baa3d53588ffd60c6c602472bd0779ef2e8`
- SHA-256：`56890aeadf06daaa4dd7424ef4aec69f5c2063fd8558981eb398affbcdd68b3b`
- 涵蓋範圍：姓名欄縮為 `width: 62` 且移除應用程式 `minWidth`；九欄順序固定為頭像、姓名、行動電話、生日、地址、信仰狀態、會員身份、關係目標、性別；區標頭依序顯示小組數與本區人數；小組標頭僅在非空時顯示經 trim 的小組時間／地點。
- 資料流：既有小組 descriptor 查詢同批讀取 CRM `new_group_time`／`new_group_place`，DTO 與 tree view model 傳遞 `GroupTime`／`GroupPlace`；`DistrictTreeBuilder` 以完整、已排序的 `Groups` 計算 `GroupCount`，不新增逐組查詢。
- 產生方式：以 Git 的 `--binary --full-index` stdout bytes 機械寫入，未編輯 raw patch、未遮罩內容；fresh regeneration 與交付檔逐 byte 相等，且恰有 8 個 `diff --git` headers。

```powershell
git diff --binary --full-index a7f497bd2ac69cd7c2af2bcc76be40bc71967a63 589f0baa3d53588ffd60c6c602472bd0779ef2e8 -- ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
```

本 patch 實際且僅有下列 8 個 diff headers：

- `ChurchReport.MemberInfo.Tests/DistrictTreeBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Services/MemberInfo/DistrictTreeBuilder.cs`
- `ChurchReport/Services/MemberInfo/DistrictTreeInputs.cs`
- `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

> **Evidence only：不可直接 `git apply`。** Controller 與 `MemberInfoGrid.cshtml` 只存在於本 host patch，不是 `feature-files/` 的可覆蓋快照。移植時應逐 hunk 對照目標 Controller、CRM schema、DevExtreme client 與欄位契約，再由目標版本重建並驗證。

## Patch 6：2026-07-18 commitment-type metadata order increment

- 檔案：[06-member-info-commitment-type-metadata-order.patch](06-member-info-commitment-type-metadata-order.patch)
- Base：`589f0baa3d53588ffd60c6c602472bd0779ef2e8`
- End/package source commit：`2406b126e989cc980e8cada9da0e07a2ede1e08d`
- SHA-256 lineage：raw source `bd12b70d6d465ebe00da7aa1b4dc11eeb5e09a5a6096bf6ea2b508c6e79d988b` → sanitized delivery `45b86e0185329b8db94129f3a1296b426d6c61be90c476e0bfca192c4e611240`
- 涵蓋範圍：metadata provider、aggregate count query、Configured／Unknown／Empty 共用排序與測試；DTO rank／has-value；一般小組與搜尋授權後排序；Ungrouped counts／segments／slices；DataGrid visible label 與 local／remote rank selector。
- 產生方式：先以 Git `--binary --full-index` 產生 path-limited mechanical diff，再只泛化可識別姓名 fixture；paths、hunks、技術契約與 13 個 `diff --git` headers 均保留。交付檔是 sanitized derivative，不能由下列 raw 命令 byte-for-byte 重建。

```powershell
git diff --binary --full-index 589f0baa3d53588ffd60c6c602472bd0779ef2e8 2406b126e989cc980e8cada9da0e07a2ede1e08d -- ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml
```

> **Evidence only：不可直接 `git apply`。** 目標教會必須讀取自己的 `OptionSet.Options` sequence，不能從本 patch 複製 Sunny 的 raw values、labels、cache identity 或 configured order。

## Current controller action index

來源：`ChurchReport/Controllers/MemberInfoController.cs` at package source commit。

| Action | Verb | Route | Current responsibility |
|---|---|---|---|
| `Index` | GET | `/MemberInfo/Index` | 建立 MemberInfo view、解析 Church/Shepherd scope，設定同步按鈕與 layout context。 |
| `LoadDistrictTree` | GET | `/MemberInfo/LoadDistrictTree` | 依授權取得 district/group tree、目前成員計數與未分組入口，並使用批次 CRM/cache 路徑。 |
| `SearchDistrictTree` | GET | `/MemberInfo/SearchDistrictTree` | 在 Church/Shepherd 可見範圍內批次搜尋、授權與去重成員，建立完整 rows 後依 metadata rank 排序。 |
| `LoadGroupMembers` | GET | `/MemberInfo/LoadGroupMembers` | 驗證 list scope、批次 contact 授權後載入指定 group 的目前成員列，依 metadata rank／姓名／ContactId 排序。 |
| `LoadUngroupedMembers` | GET | `/MemberInfo/LoadUngroupedMembers` | Church-only；排除已分組成員。會員身份排序時先 aggregate counts、建立 Configured／Unknown／Empty segments，再只查當頁 slices；其他欄維持既有 remote query。 |
| `Detail` | GET | `/MemberInfo/Detail` | 透過 conventional controller/action route；驗證 contact scope，讀取 detail fields、relation goals、OptionSet options 與 avatar source，回傳 `_MemberDetailPopup` partial。 |
| `GetContactImage` | GET | `/MemberInfo/GetContactImage` | 驗證 contact scope，依 CRM entity image、LINE URL、gender SVG fallback 的順序回傳 avatar，並使用 private cache。 |
| `GetContactImagesBatch` | POST | `/MemberInfo/GetContactImagesBatch` | 一次完成 contact batch authorization、CRM retrieval、thumbnail/cache 與 avatar source map，避免 per-row authorization/CRM calls。 |
| `ResyncLineCandidateIds` | GET | `/MemberInfo/ResyncLineCandidateIds` | Church-only；列出目前且有 LINE ID 的候選 contact IDs，供前端分批同步。 |
| `ResyncLineProfiles` | POST | `/MemberInfo/ResyncLineProfiles` | Church-only；以安全組態 lookup 取得 LINE token，分批探測/更新或清除 LINE profile fields。 |
| `UploadContactImage` | POST | `/MemberInfo/UploadContactImage` | 驗證 contact scope、檔案大小/type，經 ImageSharp 正規化後更新 CRM `entityimage` 並清除 image cache。 |
| `UpdateContactInfo` | POST | `/MemberInfo/UpdateContactInfo` | 驗證 contact scope，更新非空手機/地址與選定 OptionSet，並清除 Church rows cache。 |

## Dependency map

| Area | Current source dependency | Porting check |
|---|---|---|
| Framework/project | Main project與 test project 皆為 `net10.0`；test project reference 指向 `ChurchReport.csproj`。 | 先盤點目標 target framework、nullable/language behavior 與 reference topology。 |
| Serializer | `Startup.ConfigureServices` 使用 `AddNewtonsoftJson` 與 `DefaultContractResolver`；DTO contract 是 PascalCase。 | 不可在未調整 client contract/tests 時改成 camelCase。 |
| DevExtreme | `DevExtreme.AspNet.Core` 23.1.5、`DevExtreme.AspNet.Data` 5.1.0；controller 使用 `DataSourceLoadOptions`/`DataSourceLoader`。 | 對齊目標 client/server versions 與 DataGrid load shape。 |
| ImageSharp | `SixLabors.ImageSharp` 3.1.6；controller 使用 decode、EXIF orientation、resize/pad 與 JPEG encode。 | 驗證目標 API version、上傳限制與 memory behavior。 |
| ContactAvatar | `ContactAvatarUrl` 限制 absolute HTTP(S) URL；`DefaultAvatarSvg` 提供 gender/neutral fallback。 | 保留 URL validation、CRM-first/LINE/fallback ordering 與 SVG content type。 |
| CRM/Dataverse | `IOrganizationService`、`ICrmConnectionPool`、Dataverse client；查詢至少涉及 `contact`、`list`、`listmember`、`connection`、`RetrieveAttributeRequest`、QueryExpression→FetchXML 與 aggregate counts。 | 確認目標 logical names、relationships、roles、indexes、metadata/read/write permissions、aggregate／paging 支援；`OptionSet.Options` sequence 才是 configured order，不可臆造 schema 或用 raw value。 |
| CRM fields | Current contract 使用標準 contact/list fields、`entityimage`、`gendercode`、`birthdate`，以及目標環境的 district/group、spiritual identity、relation-goal 與 LINE custom fields。 | 逐欄位盤點；任何缺失都先停止並取得 mapping。不要從 patch 推斷所有教會 schema 相同。 |
| Authorization/ListManager | `BaseChurchController` context、`MemberInfoAccessResolver`、`MemberInfoScopeGuard`、session、`ListManager.LoginType`/group records 與 Church/Shepherd scope。 | 保留 list/contact scope checks；batch endpoint 必須先算 allowed set，不可退回 per-row service authorization。 |
| LINE | Current code讀取 `LineMessaging:{Organization}:ChannelAccessToken` 組態 key，並使用 LINE user/profile fields。 | 僅沿用 lookup contract，不攜帶值；由目標 secret store/environment 提供憑證並驗證 403/404/timeout。 |
| Cache | ASP.NET `IMemoryCache` 用於 tree、grouped IDs、rows、image thumbnails 與 OptionSet metadata；metadata 成功／失敗採不同有限 TTL。 | 對齊目標 cache registration、expiration/size policy、multi-instance／multi-organization key isolation 與 invalidation。 |
| Razor/UI | `MemberInfoGrid.cshtml`、`_MemberDetailPopup.cshtml`、jQuery、DevExtreme Popup/DataGrid、AJAX routes、唯一水平 scrollbar、native touch 與 `MembershipStatusOrder` local／remote selector。 | 核對 layout/scripts/CSS ownership、partial route、mobile/reduced-motion、custom sort selector 與 remote payload，避免 raw sorting／重複 scrollbar。 |
| Tests | xUnit 2.6.6、FluentAssertions 6.12.0、Microsoft.NET.Test.Sdk 17.8.0；另有 metadata provider／sort／count query、Razor/controller static contract tests。 | 先調整 fixture/path 與 package compatibility，再把非數值 configured order、Unknown／Empty、跨 segment paging 與 UI contracts 當作 acceptance 執行。 |

## Security and data note

Patch 只包含各節 allowlisted source paths。Patch 5 是未遮罩、byte-exact 的 raw source evidence；Patch 6 的 raw diff 掃描命中姓名 fixture，因此交付檔只泛化姓名並保留 13-path/hunk lineage。掃描未發現 literal secrets、email、URL/IP、憑證或嵌入內容的 absolute filesystem path。本次 11 份 metadata 排序 snapshots 中 10 份零替換且與來源 byte-identical；`MemberInfoTreeSearchBuilderTests.cs` 與既有 `DistrictTreeBuilderTests.cs` 保留角色化 sanitized fixture。組態 key 名稱 `ChannelAccessToken` 與既有欄位名 `m_Password` 會出現在其他歷史 diff，但沒有 token/password/connection-string literal value。若目標環境的 credential contract、CRM schema、authorization source 或 OptionSet metadata order 無法確認，停止對應 capability；不要把任何值寫進 source 或本 kit。
