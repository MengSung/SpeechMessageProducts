# 單位 1 調查結果：`ref IOrganizationService` 與 `OnPremiseClient._service`

## Q1：ToolUtility 中含 `ref IOrganizationService` 的方法

逐一檢查方法本體及其直接轉發路徑；下表的「是否重新指派」只判定該方法本體是否出現 `參數 = ...;`。所有列出的方法都沒有重新指派參數。

| 方法名稱 | 檔案路徑 | 行號 | 是否重新指派 |
|---|---|---:|---|
| `DownloadAttachment` | `ToolUtility/AttachmentOperations/IAttachmentService.cs` | 21 | 否 |
| `UploadAttachment` | `ToolUtility/AttachmentOperations/IAttachmentService.cs` | 23 | 否 |
| `DownloadAttachment` | `ToolUtility/AttachmentOperations/AttachmentService.cs` | 38 | 否 |
| `UploadAttachment` | `ToolUtility/AttachmentOperations/AttachmentService.cs` | 78 | 否 |
| `RetrieveContactByContactId` | `ToolUtility/Core/ToolUtilityFacade.cs` | 361 | 否 |
| `RetrieveContactByName` | `ToolUtility/Core/ToolUtilityFacade.cs` | 370 | 否 |
| `RetrieveContactByName_ReturnString` | `ToolUtility/Core/ToolUtilityFacade.cs` | 373 | 否 |
| `AddMembersToMarketingList` | `ToolUtility/Core/ToolUtilityFacade.cs` | 418 | 否 |
| `RemoveMembersToMarketingList` | `ToolUtility/Core/ToolUtilityFacade.cs` | 424 | 否 |
| `RetrieveMemberListCollectionByListId` | `ToolUtility/Core/ToolUtilityFacade.cs` | 434 | 否 |
| `RetrieveMemberListCollectionByListIdCrm2011` | `ToolUtility/Core/ToolUtilityFacade.cs` | 440 | 否 |
| `RetrieveDynamicMemberList` | `ToolUtility/Core/ToolUtilityFacade.cs` | 468 | 否 |
| `RetrieveDynamicMemberListDynamics365` | `ToolUtility/Core/ToolUtilityFacade.cs` | 471 | 否 |
| `RetrieveDynamicMemberListCrm2011` | `ToolUtility/Core/ToolUtilityFacade.cs` | 474 | 否 |
| `DownloadAnAttachment` | `ToolUtility/Core/ToolUtilityFacade.cs` | 532 | 否 |
| `UploadAnAttachment` | `ToolUtility/Core/ToolUtilityFacade.cs` | 535 | 否 |
| `RetrieveContactByContactId` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs` | 30 | 否 |
| `RetrieveContactByName` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs` | 39 | 否 |
| `RetrieveContactByName_ReturnString` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs` | 42 | 否 |
| `DownloadAnAttachment` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs` | 91 | 否 |
| `UploadAnAttachment` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs` | 103 | 否 |
| `RetrieveMemberListCollectionByListId` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 33 | 否 |
| `RetrieveMemberListCollectionByListIdCrm2011` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 42 | 否 |
| `RetrieveDynamicMemberList` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 66 | 否 |
| `RetrieveDynamicMemberListCrm2011` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 69 | 否 |
| `AddMembersToMarketingList` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 77 | 否 |
| `RemoveMembersToMarketingList` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs` | 89 | 否 |
| `CreateEntityCrm2011` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 56 | 否 |
| `UpdateEntity`（`ref Entity` 多載） | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 88 | 否 |
| `UpdateEntity`（非 `ref Entity` 多載） | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 100 | 否 |
| `UpdateEntityCrm2011`（`ref Entity` 多載） | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 112 | 否 |
| `UpdateEntityCrm2011`（非 `ref Entity` 多載） | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 124 | 否 |
| `DeleteEntity` | `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs` | 186 | 否 |

**Q1 結論：** 共 35 個宣告（含介面宣告、Facade 委派及 legacy partial wrapper），沒有任何一個在方法本體重新指派 `ref IOrganizationService` 參數。因此沒有需要因 ref 重新指派而跳過的呼叫點。

## Q2：`OnPremiseClient._service` 的實際型別與釋放介面

證據位置：`PowerPlatform.Dataverse.Client/OnPremiseClient.cs:67,157,161,171,224,254-257`、`PowerPlatform.Dataverse.Client/ADAuthClient.cs:36`、`PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs:46,55,224`。

| 連線路徑 | `_service` 實際型別 | `ICommunicationObject` | `IDisposable` |
|---|---|---|---|
| `ConnectAD()` | `PowerPlatform.Dataverse.Client.ADAuthClient`（`new ADAuthClient(...)`，以 `IOrganizationService` 回傳） | 否；類別宣告只有 `IOrganizationService` | 否；類別宣告未實作 `IDisposable` |
| `ConnectFederated()` | `ChannelFactory<IOrganizationService>.CreateChannel()` 建立的 WCF 服務通道代理；執行時為 `System.ServiceModel.Channels.ServiceChannelProxy` 的 WCF/DispatchProxy 代理 | 是；`ServiceChannelProxy` 實作 `ICommunicationObject`（亦經由 `IClientChannel`/`IContextChannel`） | 是；`ServiceChannelProxy` 實作 `IClientChannel`，而 `IClientChannel` 繼承 `IDisposable` |

Federated 路徑的 `CreateChannel()` 回傳值在原始碼宣告上是 `IOrganizationService`，具體代理由 WCF `ChannelFactory` 在執行期產生；已從目前套件（`System.ServiceModel.Primitives` 10.0.652802）確認其代理型別與介面繼承關係，並非猜測。

**Q2 結論：** 答案明確，未進入「未確認」分支。單位 2 可依條件規則讓 `OnPremiseClient` 實作 `IDisposable`；AD 路徑需另行處理底層 `ADAuthClient`（它本身沒有 IDisposable），Federated 路徑可透過 `ICommunicationObject`/`IDisposable` 確定性關閉通道。
