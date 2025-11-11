# LINE Messaging API 更新變更日誌

## [2.0.0] - 規劃中

### 新增 (Added)

#### 訊息發送
- 新增 `BroadcastMessageAsync` - 廣播訊息給所有好友
- 新增 `NarrowcastMessageAsync` - 依條件篩選發送訊息
- 新增 `GetNarrowcastProgressAsync` - 查詢 narrowcast 發送進度
- 新增 `GetNumberOfSentBroadcastMessagesAsync` - 查詢廣播訊息統計

#### 聊天互動
- 新增 `MarkAsReadAsync` - 標記訊息為已讀
- 新增 `ShowLoadingAnimationAsync` - 顯示「正在輸入」載入動畫

#### 訊息配額管理
- 新增 `GetMessageQuotaAsync` - 查詢本月訊息配額上限
- 新增 `GetMessageQuotaConsumptionAsync` - 查詢本月訊息使用量

#### Bot 資訊
- 新增 `GetBotInfoAsync` - 取得 Bot 基本資訊
- 新增 `BotInfo` 模型類別

#### 群組和聊天室
- 新增 `GetGroupSummaryAsync` - 取得群組摘要資訊
- 新增 `GetGroupMemberCountAsync` - 取得群組成員數量
- 新增 `GetRoomMemberCountAsync` - 取得聊天室成員數量
- 新增 `GroupSummary` 模型類別
- 新增 `MemberCount` 模型類別

#### 內容處理
- 新增 `VerifyContentPreparationAsync` - 驗證影音內容處理狀態
- 新增 `GetContentPreviewAsync` - 取得內容預覽圖

#### Webhook 設定
- 新增 `SetWebhookEndpointAsync` - 設定 Webhook URL
- 新增 `GetWebhookEndpointAsync` - 查詢 Webhook 設定
- 新增 `TestWebhookEndpointAsync` - 測試 Webhook 連線
- 新增 `WebhookEndpoint` 模型類別
- 新增 `WebhookTestResult` 模型類別

#### Rich Menu 增強
- 新增 `ValidateRichMenuAsync` - 驗證 Rich Menu 物件
- 新增 `GetDefaultRichMenuIdAsync` - 取得預設 Rich Menu ID
- 新增 `CancelDefaultRichMenuAsync` - 取消預設 Rich Menu

#### Rich Menu 批量操作
- 新增 `LinkRichMenuToUsersAsync` - 批量連結 Rich Menu (最多 500 人)
- 新增 `UnLinkRichMenuFromUsersAsync` - 批量取消連結 Rich Menu
- 新增 `RichMenuBulkLinkRequest` 模型類別
- 新增 `RichMenuBulkUnlinkRequest` 模型類別

#### Rich Menu 批次控制
- 新增 `RichMenuBatchOperationAsync` - 執行批次操作 (link/unlink/unlinkAll)
- 新增 `GetRichMenuBatchProgressAsync` - 查詢批次操作進度
- 新增 `ValidateRichMenuBatchRequestAsync` - 驗證批次請求
- 新增 `RichMenuBatchRequest` 模型類別
- 新增 `RichMenuBatchOperation` 模型類別
- 新增 `RichMenuBatchProgress` 模型類別

#### Rich Menu Alias (全新功能)
- 新增 `CreateRichMenuAliasAsync` - 建立 Rich Menu 別名
- 新增 `DeleteRichMenuAliasAsync` - 刪除 Rich Menu 別名
- 新增 `UpdateRichMenuAliasAsync` - 更新 Rich Menu 別名
- 新增 `GetRichMenuAliasAsync` - 查詢 Rich Menu 別名
- 新增 `GetRichMenuAliasListAsync` - 取得所有別名列表
- 新增 `RichMenuAlias` 模型類別
- 新增 `RichMenuAliasList` 模型類別

#### Action 類型
- 新增 `RichMenuSwitchTemplateAction` - Rich Menu 切換動作
- 新增 `ClipboardTemplateAction` - 複製到剪貼簿動作
- 更新 `TemplateActionType` 枚舉,新增 `RichMenuSwitch` 和 `Clipboard`

#### OAuth 支援更新
- 更新 `ChannelAccessToken` 類別,支援 v2.1
- 新增 `KeyId` 屬性 (v2.1)
- 新增 `ChannelAccessTokenKeyIds` 類別
- 新增 `StatelessChannelAccessTokenRequest` 類別 (v3)

#### 其他模型類別
- 新增 `MessageQuota` 類別 - 訊息配額
- 新增 `MessageQuotaConsumption` 類別 - 訊息用量
- 新增 `NarrowcastProgress` 類別 - Narrowcast 進度追蹤

### 更新 (Changed)

#### Interface 擴充
- 擴充 `ILineMessagingClient` 介面,新增 40+ 個方法

#### 文件更新
- 新增完整的 XML 文件註解
- 新增 5 份詳細的技術文件
- 新增快速參考指南
- 新增架構設計報告

### 計畫中 (Planned)

#### Phase 2 功能
- Insights APIs (統計分析)
  - Message delivery stats
  - Follower stats
  - Demographic data
  - User interaction stats
- 新的 Webhook Events
  - Unsend Event (收回訊息事件)
  - Video Viewing Complete Event (影片觀看完成事件)
- Coupon Message 類型

#### Phase 3 功能
- Audience Management (受眾管理)
- Coupon APIs (優惠券 API)
- Membership APIs (會員功能)

### 技術改進
- 考慮升級至 .NET Standard 2.0
- 考慮支援 System.Text.Json 作為 Newtonsoft.Json 的替代方案
- 完整的單元測試覆蓋率
- 整合測試套件

### 文件
- 新增 `LINE_API_更新計畫.md` - 完整更新計畫
- 新增 `LINE_API_更新進度報告.md` - 進度追蹤
- 新增 `LINE_API_Phase1_完成總結.md` - 詳細總結
- 新增 `LINE_API_Phase1_架構完成報告.md` - 架構報告
- 新增 `LINE_API_快速參考.md` - 快速參考指南
- 新增 `README.md` - 文件索引

---

## [1.4.5] - 2019-01-17

### 新增
- 支援 2019/01/17 的 API 更新
- 新增 ILineMessagingClient 介面
- LineMessagingClient 方法改為 virtual

### 更新
- 更新至最新的 API 端點

---

## 版本號規則

本專案遵循 [Semantic Versioning](https://semver.org/) 規範：

- **Major (主版本號)**: 不相容的 API 變更
- **Minor (次版本號)**: 向後相容的功能新增
- **Patch (修訂號)**: 向後相容的問題修正

## 發布策略

### v2.0.0 發布計畫
1. **Alpha**: 完成所有實作,內部測試
2. **Beta**: 開放測試,收集社群回饋
3. **RC (Release Candidate)**: 候選版本,最終測試
4. **Stable**: 正式發布

### 預計時程
- Phase 1 實作: 2-3 週
- Alpha 測試: 1 週
- Beta 測試: 2 週
- RC 測試: 1 週
- 正式發布: Beta 測試完成後

---

## Breaking Changes (可能的)

### v2.0.0
目前**沒有規劃** Breaking Changes,所有新功能都是向後相容的擴充。

現有的 API 將保持不變,只是新增了更多功能。

---

## 貢獻

如果你想要貢獻新功能或修復問題:
1. Fork 專案
2. 建立 feature branch
3. 提交 Pull Request
4. 確保所有測試通過

---

## 參考資源

- [LINE Messaging API 官方文件](https://developers.line.biz/en/reference/messaging-api/)
- [專案 GitHub](https://github.com/pierre3/LineMessagingApi/)
- [NuGet Package](https://www.nuget.org/packages/Line.Messaging/)

---

## 致謝

感謝 LINE 官方團隊持續更新和維護 Messaging API。

感謝原作者 [pierre3](https://github.com/pierre3) 和所有貢獻者。

---

**最後更新**: 2024  
**當前版本**: 1.4.5  
**下一版本**: 2.0.0 (開發中)
