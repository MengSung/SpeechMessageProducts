# LINE 共用化抽離 — 設計文件

日期：2026-07-03
分支：Jesus_5.1.6.WorktreeRefactorLine
狀態：設計已與使用者逐段確認

## 一、目標與背景

把 ChurchReport 現有的 LINE 相關程式抽離成可共用模組，讓將來的產品（同
solution、project reference 取用）不必重寫 LINE 發訊、webhook 回覆與 rich menu
操作。

現況三層中，`Line.Messaging`（SDK，協定層）與 `LineMessagingProcessor`
（共用 adapter 層）已中立且有測試；抽離主戰場在 ChurchReport 產品層：

| 檔案 | 行數 | 現況 |
| --- | --- | --- |
| `ChurchReport/Tools/LineUtilityClass.cs` | 806 | **混雜**：約 20 個通用發送方法（圖片/影音/貼圖/位置/Template/Flex/Imagemap/RichMenu）與 CRM 邏輯（`SetupChannelAccessToken(ref IOrganizationService)`、`Entity` overload）綁在一起 |
| `ChurchReport/Tools/PushUtility.cs` | 498 | 產品推播 helper，吞錯設計 |
| `ChurchReport/Tools/ReplyUtility.cs` | 225 | webhook 回覆行為（profile 查詢已於 8e7509eb 改走 processor） |
| 付款處理器 ×3、`PaymentNotificationService`、`MemberInfoController` | — | 直接 new `LineMessagingClient` 的產品呼叫點 |
| LIFF（.cshtml/JS） | — | 瀏覽器端流程，**不在本設計範圍** |

## 二、已確認的設計決策

1. **取用方式**：同 solution project reference（與 SpeechMessage.Payments 金流
   抽離同模式）。不做 NuGet、不搬獨立 repo；日後要打包隨時可升級。
2. **共用範圍**：全型別訊息發送 + webhook 事件回覆入口 + rich menu 操作入口。
   LIFF / LINE Login 前端輔助留在產品層。
3. **Token 設計**：呼叫端注入（token 字串或 IConfiguration），**每 channel 一個
   processor 實例**。「選哪個組織的 token」（`LineMessaging:{organization}:
   ChannelAccessToken` 的組織判斷、含 CRM 判斷）永遠留在產品層。不做 token
   provider 介面（目前沒有動態換 token 需求，YAGNI）。
4. **方案**：漸進收斂到現有 `LineMessagingProcessor`（方案 A）。不新建
   SpeechMessage.Line 專案（LINE 已有中立 processor 層，重做是浪費）；不採
   「只內部委派不搬家」（未來產品拿不到通用方法，未達成共用目標）。

## 三、目標架構（相依方向）

```
┌─────────────────────────────────────────────┐
│ ChurchReport（產品）      未來產品（同 solution）│
│  CRM 組織選擇、會員綁定、付款流程、LIFF、Controller│
└──────────────┬──────────────────┬───────────┘
               ↓ project reference ↓
┌─────────────────────────────────────────────┐
│ LineMessagingProcessor（共用 adapter 層）      │
│  參數驗證、訊息組裝、便利入口                    │
│  token 由呼叫端注入，每 channel 一實例           │
└──────────────┬──────────────────────────────┘
               ↓
┌─────────────────────────────────────────────┐
│ Line.Messaging（SDK，協定層）                  │
│  endpoint / header / JSON / webhook 驗章解析   │
└─────────────────────────────────────────────┘
```

相依只能往下。共用兩層禁止出現 ChurchReport、CRM（`IOrganizationService` /
`Entity`）、DbContext。

## 四、processor 專案內部結構

未來產品只需認識一個入口類別 `LineMessagingProcessorClass`（token 一次注入，
所有功能同一實例），檔案按功能族拆 partial class，不讓單檔無限長大：

```
LineMessagingProcessor/
  LineMessagingProcessorClass.cs           ← 建構子、token/client 管理（現有）
  LineMessagingProcessorClass.Push.cs      ← 全型別發送（文字/圖/影音/貼圖/位置/多播）
  LineMessagingProcessorClass.Template.cs  ← Template/Flex/Confirm/Imagemap 發送
  LineMessagingProcessorClass.Reply.cs     ← ReplyMessage/ReplyText/ReplyImage…
  LineMessagingProcessorClass.RichMenu.cs  ← rich menu 連結/解除入口
  LineMessagingProcessorClass.Profile.cs   ← 既有 profile 查詢（搬檔不改碼）
```

**搬移規則**：`LineUtilityClass` 裡「參數版」方法搬進共用層（如
`PostSerializedTemplate(string UserId, …)`）；「CRM Entity 版」overload
（`PostSerializedTemplate(Entity aLetterEntity, …)`、`SetupActionList(Entity…)`、
`GetLineIdAndContactFullNameOfSender(Entity…)`）留在產品層，轉換完參數後呼叫
共用版。

## 五、遷移路線圖（切片順序）

每一刀都是獨立切片：TDD → 測試綠 → 雙模型審查 → 提交。

| # | 切片 | 內容 | 排序理由 |
| --- | --- | --- | --- |
| 0 | 付款通知路徑收斂 | `PaymentNotificationService` 非 retry 路徑改走 processor SDK-backed `SendMessage`；**併入** C1 審查債（生產建構子測試）與 W1（註解點名 ChurchReport 改中性） | 已在進行的收尾，先清完 |
| 1 | 基礎發送族 | `SendMessage(UserId, List<ISendMessage>)` 泛用入口 + SendImage/Video/Audio/Location/Sticker + MultiCast → `*.Push.cs` | 呼叫端最多，方法小而同構 |
| 2 | Template/Flex 族 | 參數版 PostSerializedTemplate/Flex/Confirm/ImageMap → `*.Template.cs` | 沿用切片 1 模式，組裝較複雜 |
| 3 | Reply 族 | ReplyMessage/ReplyTextMessage/ReplyImage → `*.Reply.cs` | webhook 回覆入口共用化 |
| 4 | Rich Menu 族 | 通用 link/unlink/查詢入口 → `*.RichMenu.cs`（`AddRichMenuMessage` 內的產品邏輯留產品層） | 範圍內但呼叫端最少 |
| 5 | LineUtilityClass 瘦身 | 內部全面改委派 processor，只剩 CRM 組織選擇 + Entity 版 overload | 前四刀完成後自然發生 |
| 6 | （選做）PushUtility / 付款處理器呼叫點遷移 | 逐呼叫點評估，有價值才動 | 依實際收益決定 |

切片 0 已由 Codex 選定並經雙方確認；切片 1–5 各自產生 plan 後執行；切片 6 不
承諾。

## 六、錯誤處理與測試

**失敗語意統一**：共用層一律「失敗拋例外」（SDK 已用
`EnsureSuccessStatusCode`）。「吞錯」是產品層決策 — optional 通知要吞，產品
自己 catch（`PaymentNotificationService.SendLineMessage` 的 try/catch+log 是
正確示範）。共用層絕不靜默失敗。

**測試三件套**（每個搬入的方法）：

1. Request 捕捉測試 — URL、JSON body、必要 header 正確。
2. 行為測試 — 非 2xx 拋例外、空參數本地拒絕（`ArgumentException`）。
3. 生產建構子測試 — 真 token 路徑與空 token 拋例外（補 C1 型缺口；目前
   13 個 processor 測試全用 DI 建構子，生產路徑零覆蓋）。

**相容性保證**：`LineUtilityClass` 對外簽章不變，ChurchReport 呼叫端零改動；
行為等價由委派 + 回歸測試保證。同步/非同步邊界維持呼叫端現狀（既有 `.Wait()`
處不順手改成 async 蔓延）。

## 七、Guardrails（不可動範圍）

- LIFF / 前端 `.cshtml` / JS 不動（已決議留產品層）。
- `LinePayCSharp/` 不動（Line Pay 是另一個模組）。
- 官方對照矩陣的 P2 官方 API 不實作 — 共用化**只搬已在用的功能**，不趁機加新
  API。
- 共用專案不得引用產品專案（以 grep 邊界掃描驗證，沿用
  `line-processor-sdk-backed-send-message` 任務的做法）。
- 檔案 UTF-8 無 BOM + CRLF；bin/obj/artifacts 不進 commit。

## 八、成功標準

1. 未來產品加入 solution 後，只需 reference `Line.Messaging` +
   `LineMessagingProcessor` 兩個專案，即可完成：全型別發訊、webhook 事件回覆、
   rich menu 操作。
2. 共用兩層對 ChurchReport/CRM 的相依為零（邊界掃描通過）。
3. ChurchReport 全部既有 LINE 流程行為不變（既有測試 + 各切片回歸測試全綠）。
4. `LineUtilityClass` 瘦身後只含 CRM/產品邏輯；通用發送方法在共用層各有測試
   三件套。
