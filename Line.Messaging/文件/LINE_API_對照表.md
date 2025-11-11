# LINE Messaging API 對照表

快速查找 LINE 官方 API 與 Line.Messaging SDK 方法對應關係

---

## ?? 訊息相關 API

### 傳送訊息

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/message/reply` | `ReplyMessageAsync()` | 回覆訊息 |
| `POST /v2/bot/message/push` | `PushMessageAsync()` | 推播訊息 |
| `POST /v2/bot/message/multicast` | `MultiCastMessageAsync()` | 多播訊息 |
| `POST /v2/bot/message/narrowcast` | `NarrowcastMessageAsync()` | 窄播訊息 |
| `POST /v2/bot/message/broadcast` | `BroadcastMessageAsync()` | 廣播訊息 |

### 訊息互動

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/chat/markAsRead` | `MarkAsReadAsync()` | 標記已讀 |
| `POST /v2/bot/chat/loading/start` | `ShowLoadingAnimationAsync()` | 顯示載入動畫 |

### 訊息驗證

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/message/validate/reply` | `ValidateReplyMessageAsync()` | 驗證回覆訊息 |
| `POST /v2/bot/message/validate/push` | `ValidatePushMessageAsync()` | 驗證推播訊息 |
| `POST /v2/bot/message/validate/multicast` | `ValidateMulticastMessageAsync()` | 驗證多播訊息 |
| `POST /v2/bot/message/validate/narrowcast` | `ValidateNarrowcastMessageAsync()` | 驗證窄播訊息 |
| `POST /v2/bot/message/validate/broadcast` | `ValidateBroadcastMessageAsync()` | 驗證廣播訊息 |

---

## ?? 內容相關 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/message/{messageId}/content` | `GetContentStreamAsync()` | 取得內容串流 |
| `GET /v2/bot/message/{messageId}/content` | `GetContentBytesAsync()` | 取得內容位元組 |
| `GET /v2/bot/message/{messageId}/content/transcoding` | `VerifyContentPreparationAsync()` | 驗證內容準備狀態 |
| `GET /v2/bot/message/{messageId}/content/preview` | `GetContentPreviewAsync()` | 取得預覽圖 |

---

## ?? 使用者相關 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/profile/{userId}` | `GetUserProfileAsync()` | 取得使用者資料 |
| `GET /v2/bot/followers/ids` | `GetFollowersAsync()` | 取得關注者清單 |
| `GET /v2/bot/info` | `GetBotInfoAsync()` | 取得機器人資訊 |

---

## ?? 群組相關 API

### 群組聊天

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/group/{groupId}/summary` | `GetGroupSummaryAsync()` | 取得群組摘要 |
| `GET /v2/bot/group/{groupId}/members/count` | `GetGroupMemberCountAsync()` | 取得成員數量 |
| `GET /v2/bot/group/{groupId}/members/ids` | `GetGroupMemberIdsAsync()` | 取得成員 ID 清單 |
| `GET /v2/bot/group/{groupId}/member/{userId}` | `GetGroupMemberProfileAsync()` | 取得成員資料 |
| `POST /v2/bot/group/{groupId}/leave` | `LeaveFromGroupAsync()` | 離開群組 |

### 多人聊天室

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/room/{roomId}/members/count` | `GetRoomMemberCountAsync()` | 取得成員數量 |
| `GET /v2/bot/room/{roomId}/members/ids` | `GetRoomMemberIdsAsync()` | 取得成員 ID 清單 |
| `GET /v2/bot/room/{roomId}/member/{userId}` | `GetRoomMemberProfileAsync()` | 取得成員資料 |
| `POST /v2/bot/room/{roomId}/leave` | `LeaveFromRoomAsync()` | 離開聊天室 |

---

## ?? Rich Menu API

### 基本操作

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/richmenu` | `CreateRichMenuAsync()` | 建立 Rich Menu |
| `POST /v2/bot/richmenu/validate` | `ValidateRichMenuAsync()` | 驗證 Rich Menu |
| `GET /v2/bot/richmenu/list` | `GetRichMenuListAsync()` | 取得清單 |
| `GET /v2/bot/richmenu/{richMenuId}` | `GetRichMenuAsync()` | 取得單一 Rich Menu |
| `DELETE /v2/bot/richmenu/{richMenuId}` | `DeleteRichMenuAsync()` | 刪除 Rich Menu |

### 圖片操作

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/richmenu/{richMenuId}/content` | `UploadRichMenuJpegImageAsync()` | 上傳 JPEG |
| `POST /v2/bot/richmenu/{richMenuId}/content` | `UploadRichMenuPngImageAsync()` | 上傳 PNG |
| `GET /v2/bot/richmenu/{richMenuId}/content` | `DownloadRichMenuImageAsync()` | 下載圖片 |

### 預設 Rich Menu

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/user/all/richmenu/{richMenuId}` | `SetDefaultRichMenuAsync()` | 設定預設 |
| `GET /v2/bot/user/all/richmenu` | `GetDefaultRichMenuIdAsync()` | 取得預設 ID |
| `DELETE /v2/bot/user/all/richmenu` | `CancelDefaultRichMenuAsync()` | 取消預設 |

### 使用者 Rich Menu

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/user/{userId}/richmenu/{richMenuId}` | `LinkRichMenuToUserAsync()` | 連結到使用者 |
| `POST /v2/bot/richmenu/bulk/link` | `LinkRichMenuToUsersAsync()` | 批次連結 |
| `GET /v2/bot/user/{userId}/richmenu` | `GetRichMenuIdOfUserAsync()` | 取得使用者的 Rich Menu |
| `DELETE /v2/bot/user/{userId}/richmenu` | `UnLinkRichMenuFromUserAsync()` | 解除連結 |
| `POST /v2/bot/richmenu/bulk/unlink` | `UnLinkRichMenuFromUsersAsync()` | 批次解除連結 |

### Rich Menu 批次操作

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/richmenu/batch` | `RichMenuBatchOperationAsync()` | 批次控制 |
| `GET /v2/bot/richmenu/progress/batch` | `GetRichMenuBatchProgressAsync()` | 取得進度 |
| `POST /v2/bot/richmenu/validate/batch` | `ValidateRichMenuBatchRequestAsync()` | 驗證請求 |

### Rich Menu 別名

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/richmenu/alias` | `CreateRichMenuAliasAsync()` | 建立別名 |
| `DELETE /v2/bot/richmenu/alias/{richMenuAliasId}` | `DeleteRichMenuAliasAsync()` | 刪除別名 |
| `POST /v2/bot/richmenu/alias/{richMenuAliasId}` | `UpdateRichMenuAliasAsync()` | 更新別名 |
| `GET /v2/bot/richmenu/alias/{richMenuAliasId}` | `GetRichMenuAliasAsync()` | 取得別名資訊 |
| `GET /v2/bot/richmenu/alias/list` | `GetRichMenuAliasListAsync()` | 取得別名清單 |

---

## ?? 統計分析 API

### 訊息配額

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/message/quota` | `GetMessageQuotaAsync()` | 取得訊息配額 |
| `GET /v2/bot/message/quota/consumption` | `GetMessageQuotaConsumptionAsync()` | 取得使用量 |

### 訊息發送統計

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/message/delivery/reply` | `GetNumberOfSentReplyMessagesAsync()` | 回覆數量 |
| `GET /v2/bot/message/delivery/push` | `GetNumberOfSentPushMessagesAsync()` | 推播數量 |
| `GET /v2/bot/message/delivery/multicast` | `GetNumberOfSentMulticastMessagesAsync()` | 多播數量 |
| `GET /v2/bot/message/delivery/broadcast` | `GetNumberOfSentBroadcastMessagesAsync()` | 廣播數量 |

### Insights 統計

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/insight/message/delivery` | `GetMessageDeliveryAsync()` | 訊息傳送統計 |
| `GET /v2/bot/insight/followers` | `GetFollowerStatisticsAsync()` | 關注者統計 |
| `GET /v2/bot/insight/demographic` | `GetFriendDemographicsAsync()` | 好友人口統計 |
| `GET /v2/bot/insight/message/event` | `GetUserInteractionStatisticsAsync()` | 互動統計 |
| `GET /v2/bot/insight/message/event/aggregation` | `GetStatisticsPerUnitAsync()` | 單位統計 |
| `GET /v2/bot/message/aggregation/info` | `GetAggregationInfoAsync()` | 聚合資訊 |
| `GET /v2/bot/message/aggregation/list` | `GetAggregationUnitNameListAsync()` | 聚合單位清單 |

### 窄播進度

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/message/progress/narrowcast` | `GetNarrowcastProgressAsync()` | 取得窄播進度 |

---

## ?? 優惠券 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/coupon` | `CreateCouponAsync()` | 建立優惠券 |
| `PUT /v2/bot/coupon/{couponId}/close` | `CloseCouponAsync()` | 停止優惠券 |
| `GET /v2/bot/coupon` | `GetCouponListAsync()` | 取得清單 |
| `GET /v2/bot/coupon/{couponId}` | `GetCouponAsync()` | 取得詳情 |

---

## ?? 會員方案 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `GET /v2/bot/membership/subscription/{userId}` | `GetMembershipSubscriptionAsync()` | 取得訂閱狀態 |
| `GET /v2/bot/membership/{membershipId}/users/ids` | `GetMembershipUserIdsAsync()` | 取得會員清單 |
| `GET /v2/bot/membership/list` | `GetMembershipPlansAsync()` | 取得方案清單 |

---

## ?? Webhook API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `PUT /v2/bot/channel/webhook/endpoint` | `SetWebhookEndpointAsync()` | 設定端點 |
| `GET /v2/bot/channel/webhook/endpoint` | `GetWebhookEndpointAsync()` | 取得端點資訊 |
| `POST /v2/bot/channel/webhook/test` | `TestWebhookEndpointAsync()` | 測試端點 |

---

## ?? 帳號連結 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /v2/bot/user/{userId}/linkToken` | `IssueLinkTokenAsync()` | 發行連結令牌 |

---

## ?? Token 管理 API

| LINE API 端點 | SDK 方法 | 說明 |
|--------------|---------|------|
| `POST /oauth2/v2.1/token` | `IssueChannelAccessTokenAsync()` | 發行 Token |
| `POST /oauth2/v2.1/revoke` | `RevokeChannelAccessTokenAsync()` | 撤銷 Token |
| `POST /v2/oauth/accessToken` | `IssueChannelAccessTokenAsync()` | 發行 Token (v2) |
| `POST /v2/oauth/revoke` | `RevokeChannelAccessTokenAsync()` | 撤銷 Token (v2) |

---

## ?? 受眾管理 API（Placeholder）

| LINE API 端點 | SDK 方法 | 狀態 |
|--------------|---------|------|
| `POST /v2/bot/audienceGroup/upload` | `CreateUploadAudienceGroupAsync()` | ?? NotImplemented |
| `POST /v2/bot/audienceGroup/upload/byFile` | `CreateUploadAudienceGroupByFileAsync()` | ?? NotImplemented |
| `PUT /v2/bot/audienceGroup/upload` | `AddAudienceToGroupAsync()` | ?? NotImplemented |
| `PUT /v2/bot/audienceGroup/upload/byFile` | `AddAudienceToGroupByFileAsync()` | ?? NotImplemented |
| `POST /v2/bot/audienceGroup/click` | `CreateClickAudienceGroupAsync()` | ?? NotImplemented |
| `POST /v2/bot/audienceGroup/imp` | `CreateImpAudienceGroupAsync()` | ?? NotImplemented |
| `PUT /v2/bot/audienceGroup/{audienceGroupId}/updateDescription` | `UpdateAudienceGroupDescriptionAsync()` | ?? NotImplemented |
| `DELETE /v2/bot/audienceGroup/{audienceGroupId}` | `DeleteAudienceGroupAsync()` | ?? NotImplemented |
| `GET /v2/bot/audienceGroup/{audienceGroupId}` | `GetAudienceGroupAsync()` | ?? NotImplemented |
| `GET /v2/bot/audienceGroup/list` | `GetAudienceGroupsAsync()` | ?? NotImplemented |

---

## ? 缺少的 API

| LINE API 端點 | 說明 | 影響 |
|--------------|-----|------|
| `GET /v2/bot/audienceGroup/shared/{audienceGroupId}` | 取得共享受眾 | Business Manager 企業用戶 |
| `GET /v2/bot/audienceGroup/shared/list` | 列出共享受眾 | Business Manager 企業用戶 |

---

## ?? 使用範例

### 傳送訊息
```csharp
// 回覆訊息
await client.ReplyMessageAsync(replyToken, new TextMessage("Hello!"));

// 推播訊息
await client.PushMessageAsync(userId, new TextMessage("Hi!"));
```

### Rich Menu
```csharp
// 建立 Rich Menu
var richMenuId = await client.CreateRichMenuAsync(richMenu);

// 連結到使用者
await client.LinkRichMenuToUserAsync(userId, richMenuId);
```

### 取得統計
```csharp
// 取得訊息配額
var quota = await client.GetMessageQuotaAsync();

// 取得關注者統計
var stats = await client.GetFollowerStatisticsAsync(DateTime.Today);
```

---

**版本：** Line.Messaging v1.0  
**更新日期：** 2024年12月  
**相容性：** .NET Standard 1.6+
