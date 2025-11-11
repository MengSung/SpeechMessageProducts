# LINE Messaging API 更新計畫

## 概述
根據 https://developers.line.biz/en/reference/messaging-api/ 的最新規範，更新 Line.Messaging 專案

## 當前版本資訊
- 版本: 1.4.5
- 最後更新: 2019/01/17
- Target Framework: netstandard1.6
- 主要依賴: Newtonsoft.Json 13.0.3

## 主要更新類別

### 1. API 端點更新

#### 1.1 OAuth / Channel Access Token (新版)
- ? 現有: `/v2/oauth/accessToken` (舊版)
- ?? 新增: `/oauth2/v2.1/token` - Issue channel access token v2.1
- ?? 新增: `/oauth2/v2.1/verify` - Verify token v2.1
- ?? 新增: `/oauth2/v2.1/tokens/kid` - Get all valid token key IDs
- ?? 新增: `/oauth2/v2.1/revoke` - Revoke token v2.1
- ?? 新增: `/oauth2/v3/token` - Issue stateless channel access token

#### 1.2 Message API 擴充
- ? 現有: Reply, Push, Multicast
- ?? 新增: `/v2/bot/message/narrowcast` - Send narrowcast message
- ?? 新增: `/v2/bot/message/progress/narrowcast` - Get narrowcast status
- ?? 新增: `/v2/bot/message/broadcast` - Send broadcast message
- ?? 新增: `/v2/bot/chat/markAsRead` - Mark messages as read
- ?? 新增: `/v2/bot/chat/loading/start` - Display loading animation
- ?? 新增: `/v2/bot/message/quota` - Get message quota
- ?? 新增: `/v2/bot/message/quota/consumption` - Get quota consumption
- ?? 新增: `/v2/bot/message/delivery/broadcast` - Get broadcast delivery stats
- ?? 新增: 驗證 API (validate/reply, validate/push, validate/multicast, validate/narrowcast, validate/broadcast)

#### 1.3 Content API 擴充
- ? 現有: `/v2/bot/message/{messageId}/content`
- ?? 新增: `/v2/bot/message/{messageId}/content/transcoding` - Verify video/audio preparation
- ?? 新增: `/v2/bot/message/{messageId}/content/preview` - Get preview image

#### 1.4 Webhook 設定
- ?? 新增: `/v2/bot/channel/webhook/endpoint` (PUT) - Set webhook URL
- ?? 新增: `/v2/bot/channel/webhook/endpoint` (GET) - Get webhook info
- ?? 新增: `/v2/bot/channel/webhook/test` (POST) - Test webhook

#### 1.5 Audience Management (全新功能)
- ?? 新增: `/v2/bot/audienceGroup/upload` - Create audience (JSON)
- ?? 新增: `/v2/bot/audienceGroup/upload/byFile` - Create audience (File)
- ?? 新增: `/v2/bot/audienceGroup/click` - Create click audience
- ?? 新增: `/v2/bot/audienceGroup/imp` - Create impression audience
- ?? 新增: PUT/DELETE/GET audience operations

#### 1.6 Insights (統計分析)
- ?? 新增: `/v2/bot/insight/message/delivery` - Message delivery stats
- ?? 新增: `/v2/bot/insight/followers` - Follower stats
- ?? 新增: `/v2/bot/insight/demographic` - Demographic data
- ?? 新增: `/v2/bot/insight/message/event` - User interaction stats
- ?? 新增: `/v2/bot/insight/message/event/aggregation` - Aggregated stats

#### 1.7 Coupon (優惠券 - 全新功能)
- ?? 新增: `/v2/bot/coupon` (POST) - Create coupon
- ?? 新增: `/v2/bot/coupon/{couponId}/close` (PUT) - Discontinue coupon
- ?? 新增: `/v2/bot/coupon` (GET) - Get coupon list
- ?? 新增: `/v2/bot/coupon/{couponId}` (GET) - Get coupon details

#### 1.8 Users API 擴充
- ? 現有: `/v2/bot/profile/{userId}`
- ? 現有: `/v2/bot/followers/ids` (已實作但未在介面)

#### 1.9 Membership (會員功能 - 全新)
- ?? 新增: `/v2/bot/membership/subscription/{userId}` - Get subscription status
- ?? 新增: `/v2/bot/membership/{membershipId}/users/ids` - Get member list
- ?? 新增: `/v2/bot/membership/list` - Get membership plans

#### 1.10 Bot Info
- ?? 新增: `/v2/bot/info` - Get bot information

#### 1.11 Group Chat 擴充
- ? 現有: Member profile, IDs, Leave
- ?? 新增: `/v2/bot/group/{groupId}/summary` - Get group summary
- ?? 新增: `/v2/bot/group/{groupId}/members/count` - Get member count

#### 1.12 Multi-person Chat (Room) 擴充
- ? 現有: Member profile, IDs, Leave
- ?? 新增: `/v2/bot/room/{roomId}/members/count` - Get member count

#### 1.13 Rich Menu 擴充
- ? 現有: Create, Get, Delete, Link, Unlink, Upload/Download image
- ?? 新增: `/v2/bot/richmenu/validate` - Validate rich menu object
- ?? 新增: `/v2/bot/richmenu/bulk/link` - Link to multiple users
- ?? 新增: `/v2/bot/richmenu/bulk/unlink` - Unlink from multiple users
- ?? 新增: `/v2/bot/richmenu/batch` - Replace/unlink in batches
- ?? 新增: `/v2/bot/richmenu/progress/batch` - Get batch status
- ?? 新增: `/v2/bot/richmenu/validate/batch` - Validate batch request
- ?? 新增: `/v2/bot/user/all/richmenu` (GET) - Get default rich menu ID
- ?? 新增: `/v2/bot/user/all/richmenu` (DELETE) - Clear default rich menu

#### 1.14 Rich Menu Alias (全新功能)
- ?? 新增: `/v2/bot/richmenu/alias` (POST) - Create alias
- ?? 新增: `/v2/bot/richmenu/alias/{richMenuAliasId}` (DELETE) - Delete alias
- ?? 新增: `/v2/bot/richmenu/alias/{richMenuAliasId}` (POST) - Update alias
- ?? 新增: `/v2/bot/richmenu/alias/{richMenuAliasId}` (GET) - Get alias info
- ?? 新增: `/v2/bot/richmenu/alias/list` (GET) - Get alias list

### 2. Message Objects 更新

#### 2.1 新增訊息類型
- ?? Coupon Message - 優惠券訊息
- ?? Text Message v2 - 增強版文字訊息

#### 2.2 新增 Action 類型
- ?? Rich Menu Switch Action - 切換 Rich Menu
- ?? Clipboard Action - 複製到剪貼簿

### 3. Webhook Events 更新

#### 3.1 新增事件類型
- ?? Unsend Event - 收回訊息事件
- ?? Video Viewing Complete Event - 影片觀看完成事件
- ?? Membership Event - 會員事件

### 4. 模型類別更新需求

#### 4.1 需要新增的類別
```
Models/
├── Audience/
│   ├── AudienceGroup.cs
│   ├── AudienceGroupStatus.cs
│   ├── CreateAudienceRequest.cs
│   └── AudienceGroupList.cs
├── Insights/
│   ├── MessageDeliveryStats.cs
│   ├── FollowerStats.cs
│   ├── DemographicData.cs
│   └── UserInteractionStats.cs
├── Coupon/
│   ├── Coupon.cs
│   ├── CouponCreate.cs
│   └── CouponList.cs
├── Membership/
│   ├── MembershipSubscription.cs
│   ├── MembershipPlan.cs
│   └── MembershipUserList.cs
├── Bot/
│   └── BotInfo.cs
├── Group/
│   └── GroupSummary.cs
├── RichMenu/
│   ├── RichMenuAlias.cs
│   ├── RichMenuBatchRequest.cs
│   ├── RichMenuBatchProgress.cs
│   └── RichMenuBulkLinkRequest.cs
└── Webhook/
    ├── UnsendEvent.cs
    ├── VideoViewingCompleteEvent.cs
    └── MembershipEvent.cs
```

#### 4.2 需要更新的類別
- `ChannelAccessToken.cs` - 新增 v2.1/v3 欄位
- `NumberOfMessages.cs` - 擴充統計資訊
- `WebhookEvent.cs` - 新增事件類型
- `RichMenu.cs` - 新增驗證欄位

### 5. 實作優先順序

#### Phase 1 (高優先級 - 核心功能)
1. OAuth v2.1/v3 支援
2. Broadcast/Narrowcast 訊息
3. Message validation APIs
4. Bot Info API
5. Group/Room member count
6. Webhook configuration APIs

#### Phase 2 (中優先級 - 擴充功能)
1. Insights APIs
2. Rich Menu bulk operations
3. Rich Menu Alias
4. Mark as read / Loading animation
5. Content transcoding APIs
6. 新的 Webhook Events

#### Phase 3 (低優先級 - 進階功能)
1. Audience Management
2. Coupon APIs
3. Membership APIs
4. 新 Message types (Coupon)
5. 新 Action types (Rich Menu Switch, Clipboard)

### 6. 專案配置更新建議

#### 6.1 Target Framework
考慮升級到 .NET Standard 2.0 或 2.1 以獲得更好的 API 支援

#### 6.2 套件更新
- Newtonsoft.Json 13.0.3 (已是最新)
- 考慮加入 System.Text.Json 支援

#### 6.3 版本號
- 建議更新至 2.0.0 (因為有 breaking changes)

### 7. Breaking Changes 評估

可能的 Breaking Changes:
1. OAuth 方法簽章變更
2. 新增必要參數
3. 回傳型別變更
4. 過時 API 標記為 Obsolete

### 8. 測試策略

1. 單元測試 - 針對新增的方法
2. 整合測試 - 實際 API 呼叫測試
3. 向後相容性測試
4. 文件範例驗證

## 實作步驟

### Step 1: 準備工作
- [ ] 建立新分支
- [ ] 備份現有程式碼
- [ ] 審閱所有 API 文件

### Step 2: Phase 1 實作
- [ ] 實作 OAuth v2.1/v3
- [ ] 實作 Broadcast/Narrowcast
- [ ] 實作 Validation APIs
- [ ] 實作 Bot Info
- [ ] 更新 Group/Room APIs

### Step 3: Phase 2 實作
- [ ] 實作 Insights
- [ ] 實作 Rich Menu 擴充
- [ ] 實作輔助功能 APIs

### Step 4: Phase 3 實作
- [ ] 實作 Audience Management
- [ ] 實作 Coupon APIs
- [ ] 實作 Membership APIs

### Step 5: 測試與驗證
- [ ] 執行所有測試
- [ ] 更新文件
- [ ] 更新範例程式碼
- [ ] 發布版本

## 參考資源
- LINE Messaging API Reference: https://developers.line.biz/en/reference/messaging-api/
- LINE Developers: https://developers.line.biz/
