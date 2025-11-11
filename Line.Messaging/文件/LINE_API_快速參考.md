# LINE Messaging API 更新 - 快速參考指南

## ?? 新功能快速索引

### ?? 訊息發送
| 功能 | 方法 | 說明 |
|------|------|------|
| 廣播訊息 | `BroadcastMessageAsync` | 發送給所有好友 |
| Narrowcast | `NarrowcastMessageAsync` | 依條件篩選發送 |
| 進度查詢 | `GetNarrowcastProgressAsync` | 查詢 narrowcast 進度 |

### ?? 聊天互動
| 功能 | 方法 | 說明 |
|------|------|------|
| 標記已讀 | `MarkAsReadAsync` | 將訊息標記為已讀 |
| 載入動畫 | `ShowLoadingAnimationAsync` | 顯示「正在輸入」動畫 |

### ?? 訊息配額
| 功能 | 方法 | 說明 |
|------|------|------|
| 查詢配額 | `GetMessageQuotaAsync` | 本月訊息配額上限 |
| 查詢用量 | `GetMessageQuotaConsumptionAsync` | 本月已使用訊息數 |
| 廣播統計 | `GetNumberOfSentBroadcastMessagesAsync` | 查詢特定日期發送數 |

### ?? Bot 資訊
| 功能 | 方法 | 說明 |
|------|------|------|
| 取得 Bot 資訊 | `GetBotInfoAsync` | ID、名稱、圖片、聊天模式 |

### ?? 群組/聊天室
| 功能 | 方法 | 說明 |
|------|------|------|
| 群組摘要 | `GetGroupSummaryAsync` | 群組名稱和圖片 |
| 群組人數 | `GetGroupMemberCountAsync` | 成員數量 |
| 聊天室人數 | `GetRoomMemberCountAsync` | 成員數量 |

### ?? 內容處理
| 功能 | 方法 | 說明 |
|------|------|------|
| 驗證轉檔 | `VerifyContentPreparationAsync` | 檢查影音是否處理完成 |
| 預覽圖 | `GetContentPreviewAsync` | 取得縮圖 |

### ?? Webhook 設定
| 功能 | 方法 | 說明 |
|------|------|------|
| 設定 URL | `SetWebhookEndpointAsync` | 設定 webhook URL |
| 查詢設定 | `GetWebhookEndpointAsync` | 取得當前設定 |
| 測試 | `TestWebhookEndpointAsync` | 測試 webhook 連線 |

### ?? Rich Menu 基本操作
| 功能 | 方法 | 說明 |
|------|------|------|
| 驗證 | `ValidateRichMenuAsync` | 驗證 rich menu 物件 |
| 預設選單 | `GetDefaultRichMenuIdAsync` | 取得預設選單 ID |
| 取消預設 | `CancelDefaultRichMenuAsync` | 取消預設選單 |

### ?? Rich Menu 批量操作
| 功能 | 方法 | 說明 |
|------|------|------|
| 批量連結 | `LinkRichMenuToUsersAsync` | 一次連結多個使用者 |
| 批量取消 | `UnLinkRichMenuFromUsersAsync` | 一次取消多個使用者 |

### ?? Rich Menu 批次控制
| 功能 | 方法 | 說明 |
|------|------|------|
| 批次操作 | `RichMenuBatchOperationAsync` | 執行批次 link/unlink |
| 查詢進度 | `GetRichMenuBatchProgressAsync` | 查詢批次操作狀態 |
| 驗證請求 | `ValidateRichMenuBatchRequestAsync` | 驗證批次請求 |

### ??? Rich Menu Alias
| 功能 | 方法 | 說明 |
|------|------|------|
| 建立別名 | `CreateRichMenuAliasAsync` | 為 rich menu 建立別名 |
| 刪除別名 | `DeleteRichMenuAliasAsync` | 刪除別名 |
| 更新別名 | `UpdateRichMenuAliasAsync` | 更新別名對應的選單 |
| 查詢別名 | `GetRichMenuAliasAsync` | 取得別名資訊 |
| 別名列表 | `GetRichMenuAliasListAsync` | 取得所有別名 |

### ?? 新 Action 類型
| Action | 類別 | 說明 |
|--------|------|------|
| Rich Menu 切換 | `RichMenuSwitchTemplateAction` | 切換到指定選單 |
| 剪貼簿 | `ClipboardTemplateAction` | 複製文字到剪貼簿 |

## ?? 常用場景範例

### 場景 1: 發送重要公告
```csharp
// 廣播給所有好友
await client.BroadcastMessageAsync(new List<ISendMessage> 
{
    new TextMessage("?? 重要公告...")
});
```

### 場景 2: 篩選特定條件發送
```csharp
// Narrowcast 給特定族群
string requestId = await client.NarrowcastMessageAsync(
    messages: new List<ISendMessage> { new TextMessage("限時優惠") },
    recipient: new { type = "audience", audienceGroupId = 123 }
);

// 查詢發送進度
var progress = await client.GetNarrowcastProgressAsync(requestId);
```

### 場景 3: 檢查訊息配額
```csharp
var quota = await client.GetMessageQuotaAsync();
var consumption = await client.GetMessageQuotaConsumptionAsync();
Console.WriteLine($"剩餘: {quota.TotalUsage - consumption.TotalUsage}");
```

### 場景 4: Rich Menu 別名管理
```csharp
// 建立別名
await client.CreateRichMenuAliasAsync("richmenu-abc123", "main-menu");

// 在按鈕中使用
var switchAction = new RichMenuSwitchTemplateAction(
    label: "切換到主選單",
    richMenuAliasId: "main-menu"
);
```

### 場景 5: 批次連結 Rich Menu
```csharp
// 一次為 500 個使用者連結選單
await client.LinkRichMenuToUsersAsync(
    richMenuId: "richmenu-abc123",
    userIds: userList // max 500
);
```

### 場景 6: 複雜的批次操作
```csharp
var operations = new List<RichMenuBatchOperation>
{
    // 為特定使用者連結選單 A
    new RichMenuBatchOperation
    {
        Type = "link",
        RichMenuId = "richmenu-aaa",
        UserIds = new List<string> { "U111", "U222" }
    },
    // 為其他使用者連結選單 B
    new RichMenuBatchOperation
    {
        Type = "link",
        RichMenuId = "richmenu-bbb",
        UserIds = new List<string> { "U333", "U444" }
    }
};

await client.RichMenuBatchOperationAsync(operations);
```

### 場景 7: Webhook 測試
```csharp
var result = await client.TestWebhookEndpointAsync();
if (result.Success)
{
    Console.WriteLine($"Webhook 正常: {result.StatusCode}");
}
else
{
    Console.WriteLine($"Webhook 錯誤: {result.Detail}");
}
```

### 場景 8: 顯示「正在輸入」
```csharp
// 顯示載入動畫 5 秒
await client.ShowLoadingAnimationAsync(userId, 5);

// 處理複雜的業務邏輯...
await Task.Delay(4000);

// 發送回覆
await client.PushMessageAsync(userId, "處理完成！");
```

### 場景 9: 標記訊息已讀
```csharp
// 在處理 webhook 後標記已讀
await client.MarkAsReadAsync(userId);
```

### 場景 10: 取得群組資訊
```csharp
var summary = await client.GetGroupSummaryAsync(groupId);
int memberCount = await client.GetGroupMemberCountAsync(groupId);

await client.PushMessageAsync(groupId, 
    $"群組「{summary.GroupName}」目前有 {memberCount} 位成員");
```

## ?? 重要限制

### 訊息限制
- Broadcast/Narrowcast: 5 個訊息/次
- Multicast: 500 個收件人/次
- Rich Menu 批量: 500 個使用者/次
- Rich Menu 批次: 30 個操作/次

### 時間限制
- LoadingAnimation: 最多 60 秒
- Link Token: 有效期 10 分鐘
- Narrowcast 處理: 可能需要數分鐘

### 數量限制
- Rich Menu: 最多 1000 個/bot
- Rich Menu Alias: 最多 100 字元

## ?? 錯誤處理

```csharp
try
{
    await client.BroadcastMessageAsync(messages);
}
catch (LineResponseException ex)
{
    Console.WriteLine($"LINE API 錯誤: {ex.StatusCode}");
    Console.WriteLine($"訊息: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"一般錯誤: {ex.Message}");
}
```

## ?? 相關文件
- [完整實作總結](LINE_API_Phase1_完成總結.md)
- [更新計畫](LINE_API_更新計畫.md)
- [進度報告](LINE_API_更新進度報告.md)
- [LINE 官方文件](https://developers.line.biz/en/reference/messaging-api/)

## ?? 最佳實踐

### 1. 訊息配額管理
```csharp
// 發送前檢查配額
var quota = await client.GetMessageQuotaAsync();
var consumption = await client.GetMessageQuotaConsumptionAsync();
if (consumption.TotalUsage >= quota.TotalUsage)
{
    // 配額已用完，考慮使用 Narrowcast
}
```

### 2. 非同步處理
```csharp
// 使用 Narrowcast 時追蹤 requestId
string requestId = await client.NarrowcastMessageAsync(messages);
// 儲存 requestId 供後續查詢
await SaveRequestIdAsync(requestId);
```

### 3. Rich Menu 別名規劃
```csharp
// 使用有意義的別名
await client.CreateRichMenuAliasAsync(richMenuId, "promotion-2024");
// 而不是 "menu1", "menu2"
```

### 4. 批次操作分組
```csharp
// 將大量使用者分成每 500 人一組
var batches = SplitIntoBatches(allUsers, 500);
foreach (var batch in batches)
{
    await client.LinkRichMenuToUsersAsync(richMenuId, batch);
    await Task.Delay(1000); // 避免過於頻繁的請求
}
```

## ? 效能建議

1. **使用 Broadcast 而非個別 Push**
   - ? `BroadcastMessageAsync()` - 一次發送
   - ? 迴圈呼叫 `PushMessageAsync()` - 慢且消耗配額

2. **批量操作取代個別操作**
   - ? `LinkRichMenuToUsersAsync(richMenuId, 500 users)`
   - ? 迴圈 500 次 `LinkRichMenuToUserAsync()`

3. **使用 Narrowcast 精準發送**
   - 減少不必要的訊息發送
   - 節省配額

4. **善用別名**
   - 更新選單時只需更新別名對應
   - 不需重新連結所有使用者
