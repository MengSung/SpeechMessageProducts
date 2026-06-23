# 🐑 行道會聖谷教會 LINE Mini App — 完整佈署安裝教學（超詳細圖解版）
### LINE Mini App 大白癡專用 — 每一步都有截圖位置說明、常見坑洞提醒

> **對象**：完全不懂 LINE Mini App 的開發者（自稱大白癡也沒問題 🙌）  
> **專案**：ChurchReport 行道會聖谷教會教會管理系統  
> **技術棧**：ASP.NET Core (.NET 10) + DevExtreme 21.2.7 + jQuery + LIFF SDK 2.x  
> **伺服器**：`jesus.speechmessage.com.tw:807`  
> **更新日期**：2025 年 7 月

---

## 🔧 依你目前 Console 截圖自動整理的實際填寫值

> 已同步填入 `ChurchReport/appsettings.json` 的 `MiniApp` 區段。

| 欄位 | 值 |
|---|---|
| Developing Channel ID | `2009427707` |
| Review Channel ID | `2009427708` |
| Published Channel ID | `2009427709` |
| Developing Channel Secret | `e94032391784ad4f690f79b8efdc193f` |
| Review Channel Secret | `6c3372bec7d1be20421f451191bd70c0` |
| Published Channel Secret | `e492954a5072bce0c4089893388fc6af` |
| Developing LIFF ID | `2009427707-Fi5L5blD` |
| Review LIFF ID | `2009427708-GToVLqgV` |
| Published LIFF ID | `2009427709-PTH3dfeP` |
| Channel name | `行道會聖谷教會` |
| Channel description | `行道會聖谷教會MINI` |
| Email address | `mengsunghu@gmail.com` |

### LINE MINI App Console 欄位請這樣填

1. `Basic settings`：
   - `Channel name`：`行道會聖谷教會`
   - `Channel description`：`行道會聖谷教會MINI`
   - `Email address`：`mengsunghu@gmail.com`
   - `Privacy policy URL`：`https://jesus.speechmessage.com.tw:807/Privacy`
   - `Terms of use URL`：可先留空

2. `Web app settings` → `Endpoint URL`（把預設 `liff-default-xxx.html` 全部改掉）：
   - Developing：`https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427707-Fi5L5blD`
   - Review：`https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV`
   - Published：`https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP`

3. `Scopes`：維持 `profile`、`openid`

### 下一步（你現在應該立刻做）

1. 在 LINE Console 先完成三個 `Endpoint URL` 的修改與儲存。
2. 把你自己的 LINE 帳號加到 `Roles` → `Tester`。
3. 部署後先測 `Developing`：
   - `https://liff.line.me/2009427707-Fi5L5blD`
4. 測試通過後改測 `Review`，再送審。
5. 審核通過後，將 `appsettings.json` 的 `MiniApp:ActiveEnvironment` 改成 `Published` 並重新部署。

---

## 📋 完整大綱

| 章節 | 內容 | 預估時間 |
|:----:|------|:-------:|
| [零](#第零章行道會聖谷教會現在的狀態你不用從零開始) | 行道會聖谷教會現在的狀態（你不用從零開始！） | 5 分鐘閱讀 |
| [一](#第一章line-mini-app-到底是什麼白話文版) | LINE Mini App 到底是什麼？（白話文版） | 10 分鐘閱讀 |
| [二](#第二章向-line-taiwan-申請開發許可) | 向 LINE Taiwan 申請開發許可 | 30 分鐘操作 + 1-4 週等待 |
| [三](#第三章在-line-developers-console-建立-mini-app-channel) | 在 LINE Developers Console 建立 Mini App Channel | 30 分鐘操作 |
| [四](#第四章把-console-資訊填入-appsettingsjson) | 把 Console 資訊填入 appsettings.json | 10 分鐘操作 |
| [五](#第五章設定-endpoint-url讓-line-知道要載入哪個網頁) | 設定 Endpoint URL | 15 分鐘操作 |
| [六](#第六章準備兩張圖片channel-icon--loadinggif) | 準備兩張圖片（Channel Icon + loading.gif） | 30-60 分鐘 |
| [七](#第七章部署到伺服器) | 部署到伺服器 | 15 分鐘操作 |
| [八](#第八章用手機測試超詳細測試手冊) | 用手機測試（超詳細測試手冊） | 1-3 小時 |
| [九](#第九章送-line-審核) | 送 LINE 審核 | 30 分鐘操作 + 數天等待 |
| [十](#第十章審核通過正式上線) | 審核通過 → 正式上線 🎉 | 15 分鐘操作 |
| [附錄A](#附錄a完整問題排查手冊遇到問題看這裡) | 完整問題排查手冊（遇到問題看這裡） | 參考用 |
| [附錄B](#附錄b名詞對照表) | 名詞對照表 | 參考用 |
| [附錄C](#附錄c行道會聖谷教會專案程式碼與-mini-app-的對應關係) | 行道會聖谷教會程式碼與 Mini App 的對應關係 | 參考用 |

---

## 第零章：行道會聖谷教會現在的狀態（你不用從零開始！）

### 0.1 程式碼部分已經全部完成 ✅

以下 13 項程式碼修改**已經做完了**，你完全不需要再碰：

```
✅ 已完成（不用再動）                                     對應檔案
───────────────────────────────────────────────────────────────────
 1. MiniApp 設定區段                                → appsettings.json (第 97-131 行)
 2. 隱私政策頁面                                    → Views/Authentication/Privacy.cshtml
 3. Privacy 路由 Action                             → Controllers/AuthenticationController.Core.cs
 4. Privacy 路由 + 中間件註冊                       → Startup.cs (第 683 行 + 第 740-744 行)
 5. Safe Area CSS                                   → wwwroot/css/mini-app-safe-area.css
 6. Mini App 環境偵測中間件                         → Middleware/MiniAppDetectionMiddleware.cs
 7. LineIdLoginView viewport-fit + Safe Area CSS     → Views/Authentication/LineIdLoginView.cshtml (第 14, 28 行)
 8. LineLiffView viewport-fit + Safe Area CSS        → Views/Authentication/LineLiffView.cshtml
 9. Login viewport-fit + Safe Area CSS               → Views/Authentication/Login.cshtml
10. DediationLineLoginView viewport-fit + Safe Area  → Views/Dedication/DediationLineLoginView.cshtml
11. DedicationFeeView viewport-fit + Safe Area       → Views/Dedication/DedicationFeeView.cshtml
12. _Layout viewport-fit + Safe Area                 → Views/Shared/_Layout.cshtml
13. Startup 中間件註冊                               → Startup.cs
```

### 0.2 你現在只需要做的事（6 個人工操作）

```
⬜ 步驟 1：向 LINE Taiwan 申請開發許可          ← 整個流程最慢，先做！
⬜ 步驟 2：在 LINE Console 建立 Mini App Channel ← 申請通過後才能做
⬜ 步驟 3：填入 LIFF ID 到 appsettings.json     ← 只改 5 個空字串
⬜ 步驟 4：設定 Endpoint URL                     ← 在 Console 網頁上貼 3 個 URL
⬜ 步驟 5：準備 Channel Icon + loading.gif       ← 做兩張圖
⬜ 步驟 6：測試 → 送審 → 上線                   ← 用手機測試後送審
```

### 0.3 為什麼行道會聖谷教會轉換特別容易？

```
原因：我們已經整合了 LIFF SDK！

現有系統的 LIFF 基礎：
  ✅ LIFF SDK 2.x 已引入（LineIdLoginView.cshtml 第 136 行）
     <script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>

  ✅ liff.init() 已經寫好（第 179 行）
     liff.init({ liffId: '@TempData["Proponent"]' })
     → TempData["Proponent"] 是從 URL 參數帶入的 LIFF ID
     → 也就是說，只要 URL 帶的 LIFF ID 換成 Mini App 的，就自動生效！

  ✅ 環境偵測已寫好（第 178 行 detectEnvironmentAndChooseMethod）
     → 手機版 LINE → 用 LIFF SDK
     → 電腦版 LINE → 用 Server-side OAuth

  ✅ liff.getProfile() 已寫好（第 260 行）
     → 取得用戶 displayName、userId

  ✅ LINE Login Channel 已建立
     → Channel ID: 2007621061
     → 現有 LIFF ID: "2007621061-Exd9BGv8"（登入）、"1653819697-YkPyPkr6"（綁定）

結論：
  LINE Mini App = 換一組新的 LIFF ID + UI 微調 + 送審
  程式碼邏輯完全不用改！
```

---

## 第一章：LINE Mini App 到底是什麼？（白話文版）

### 1.1 最簡單的理解方式

```
想像 LINE Mini App 就像「LINE 裡面的 App」：

  ┌──────────────────────────────────┐
  │  你的手機                         │
  │                                  │
  │  ┌────────────────────────────┐  │
  │  │  LINE App                  │  │
  │  │                            │  │
  │  │  ┌──────────────────────┐  │  │
  │  │  │  行道會聖谷教會 Mini App     │  │  │  ← 這就是 LINE Mini App
  │  │  │                      │  │  │     它跑在 LINE 裡面
  │  │  │  ┌────────────────┐  │  │  │
  │  │  │  │ LINE Header    │  │  │  │  ← LINE 自動加的頂部列
  │  │  │  ├────────────────┤  │  │  │
  │  │  │  │                │  │  │  │
  │  │  │  │  你的網頁      │  │  │  │  ← 這才是你寫的東西
  │  │  │  │  (行道會聖谷教會)      │  │  │  │     就是 LineIdLoginView.cshtml
  │  │  │  │                │  │  │  │
  │  │  │  ├────────────────┤  │  │  │
  │  │  │  │ LINE Footer    │  │  │  │  ← LINE 自動加的底部列
  │  │  │  └────────────────┘  │  │  │
  │  │  └──────────────────────┘  │  │
  │  └────────────────────────────┘  │
  └──────────────────────────────────┘

用白話說：
  LINE Mini App = LINE 裡面開一個小視窗，載入你的網頁
  技術上：LINE Mini App = LIFF App 的升級版
```

### 1.2 教友的使用體驗（轉換前 vs 轉換後）

```
【轉換前】現在的方式：
  教友收到 LINE 連結 → 點開 → 跑 LIFF → 登入 → 使用
  缺點：只能透過連結進入，沒有搜尋、沒有主畫面捷徑

【轉換後】Mini App 的方式：
  教友在 LINE 搜尋「行道會聖谷教會」→ 直接開啟 → 自動登入 → 使用 ✅
  教友在 LINE 首頁 → 看到行道會聖谷教會圖示 → 點一下就開啟 ✅
  教友加捷徑到手機桌面 → 像原生 App 一樣使用 ✅
  教友收到 Service Message 推播通知 → 點擊直接開啟 ✅
```

### 1.3 LIFF App vs LINE Mini App（差在哪）

```
┌──────────────────┬──────────────────────┬──────────────────────────┐
│    項目          │   LIFF App（現在）    │  LINE Mini App（目標）    │
│                  │   我們目前用的        │  要升級成的               │
├──────────────────┼──────────────────────┼──────────────────────────┤
│ Channel 類型     │ LINE Login           │ LINE MINI App            │
│ 用戶入口         │ 只能透過連結點開      │ LINE 搜尋/首頁/分享      │
│ Service Message  │ ❌ 不支援            │ ✅ 免費推播通知           │
│ 主畫面捷徑       │ ❌ 不支援            │ ✅ 加到手機桌面           │
│ Custom Path      │ ❌ 不支援            │ ✅ 自訂短網址             │
│ 載入畫面         │ 沒有（白屏）          │ 有 loading.gif 動畫      │
│ Header/Footer    │ 沒有                 │ LINE 自動加上             │
│ 審核要求         │ 不需要               │ 需要 LINE 審核            │
│ LIFF SDK         │ ✅ 相同              │ ✅ 完全相同 + 額外 API    │
└──────────────────┴──────────────────────┴──────────────────────────┘

⭐ 重點：LIFF SDK 完全相同！你現有的 JavaScript 程式碼不用改！
```

### 1.4 三個環境是什麼意思？

```
LINE Mini App Channel 建好後，LINE 會自動幫你建 3 個環境：

  ┌─────────────────────────────────────────────────────────────┐
  │                 你的 Mini App Channel                        │
  │                                                             │
  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
  │  │ Developing   │  │ Review       │  │ Published        │  │
  │  │ （開發用）    │  │（送審用）    │  │（正式上線用）     │  │
  │  │              │  │              │  │                  │  │
  │  │ LIFF ID: A   │  │ LIFF ID: B   │  │ LIFF ID: C       │  │
  │  │              │  │              │  │                  │  │
  │  │ 只有 Tester  │  │ 只有 Tester  │  │ 所有 LINE 用戶   │  │
  │  │ 才能用       │  │ + LINE 審核員│  │ 都能用           │  │
  │  └──────────────┘  └──────────────┘  └──────────────────┘  │
  └─────────────────────────────────────────────────────────────┘

  用時間軸來看：

  現在 ──────► 開發測試 ──────► 送審 ──────► 上線
              Developing        Review       Published

  行道會聖谷教會的情況：
    因為只有一台伺服器，三個環境都指向同一台
    差別只在 URL 末尾的 LIFF ID 不同
```

---

## 第二章：向 LINE Taiwan 申請開發許可

### 2.1 為什麼台灣要先申請？

```
╔══════════════════════════════════════════════════════════════════╗
║                                                                  ║
║  ⚠️ 台灣地區特殊規定：                                           ║
║                                                                  ║
║  你無法自己在 LINE Developers Console 建立 LINE MINI App         ║
║  類型的 Channel。必須先向 LINE Taiwan 申請，審核通過後，         ║
║  LINE 才會在你的帳號開啟這個功能。                               ║
║                                                                  ║
║  這是整個流程中最慢的一步（1-4 週），所以第一件事就是送出申請！  ║
║                                                                  ║
╚══════════════════════════════════════════════════════════════════╝
```

### 2.2 操作步驟（每一步都有說明）

```
🔵 Step 1：打開瀏覽器，前往以下網址
   
   https://tw.linebiz.com/service/other-solutions/line-mini-app/

   💡 這是 LINE 台灣的官方商業網站，
      專門用來申請各種 LINE 商業服務
```

```
🔵 Step 2：找到並點擊申請按鈕

   頁面上會有「立即申請」、「聯絡我們」或「了解更多」之類的按鈕
   ↓
   點擊進入申請表單
   
   ⚠️ 如果頁面改版找不到按鈕：
      → 往頁面最下方捲動，通常會有聯絡方式
      → 或直接寄信到 LINE Taiwan：miniapp_tw@linecorp.com
```

```
🔵 Step 3：填寫申請表單

   照以下內容填寫（請根據實際情況修改）：
```

#### 📝 表單填寫對照表

| 欄位 | 怎麼填 | 為什麼這樣填 |
|------|--------|-------------|
| **公司/組織名稱** | `音訊科技有限公司` | 你的公司登記名稱 |
| **統一編號** | `13054485` | appsettings.json 中 ChurchInfo:TaxId 的值 |
| **服務名稱** | `行道會聖谷教會教會管理系統` | 教友看到的 Mini App 名稱 |
| **服務說明** | （見下方完整範文） | 越詳細越好，LINE 會看這個 |
| **預期上線時間** | `2025 年 9 月`（或更晚） | 留多一點時間，不要太趕 |
| **聯絡人姓名** | （你的本名） | LINE 會用這個名字聯絡你 |
| **聯絡人 Email** | `mengsunghu@gmail.com` | 會收到審核通知 |
| **聯絡人電話** | `03-4316679` | LINE 業務可能會打電話了解 |
| **公司網址** | `https://jesus.speechmessage.com.tw:807` | 讓 LINE 看到你的服務已經在運作 |
| **LINE OA ID** | （有就填，沒有留空） | 如果教會有 LINE 官方帳號 |
| **是否已有 LIFF App** | `是，Channel ID: 2007621061` | 告訴 LINE 你已經有基礎 |

#### 📄 服務說明範文（可直接複製貼上）

```
行道會聖谷教會（ChurchReport）是一個教會會友管理系統，主要服務對象為台灣各教會會友。

【主要功能】
1. LINE 帳號快速登入：會友透過 LINE LIFF SDK 登入，無須額外帳號密碼
2. 小組牧養回報：小組長可回報每週聚會出席人數、探訪紀錄、代禱事項
3. 線上奉獻：整合永豐銀行 QPay、高鉅科技 MyPay、台新銀行 TSPG 三種金流
4. 奉獻紀錄查詢：會友可查詢自己的奉獻明細與歷史紀錄
5. 個人資料維護：會友可更新聯絡資訊（姓名、電話、地址等）

【技術架構】
- 後端框架：ASP.NET Core (.NET 10)
- 前端技術：DevExtreme 21.2.7 + jQuery + Bootstrap
- 前端 LINE 整合：LIFF SDK 2.x（已整合使用中）
- 資料庫：Microsoft Dynamics 365 CRM
- 伺服器：Windows Server，HTTPS (SSL/TLS)
- 網址：https://jesus.speechmessage.com.tw:807

【現有 LINE 整合狀況】
我們目前已使用 LIFF SDK 2.x 超過一年，功能穩定：
- LINE Login Channel ID: 2007621061
- 已實作 LIFF 登入、LINE Profile 取得、LINE 綁定
- 支援手機版 LINE（LIFF）和電腦版 LINE（OAuth 2.0）
- 希望升級為 LINE Mini App 以提供更完善的用戶體驗

【目標用戶】
- 初期：單一教會約 200-500 名會友
- 中期：擴展至多間教會，預計 1,000+ 用戶
- 長期：成為教會管理標準化工具

【申請原因】
希望透過 LINE Mini App 讓教友有更好的使用體驗，包括：
- LINE 搜尋直接找到「行道會聖谷教會」
- 主畫面捷徑，像原生 App 一樣方便
- Service Message 發送聚會提醒、奉獻感謝通知
```

### 2.3 申請後會發生什麼？

```
申請送出後的時間線：

  Day 1     ：你送出申請表單
              ├── 你會收到一封「已收到申請」的自動回覆 Email
              └── LINE Taiwan 的團隊開始審核

  Day 3-7   ：可能會有 LINE 業務人員聯絡你
              ├── 用 Email 或電話詢問更多細節
              ├── 可能問你服務的具體使用場景
              └── 如實回答即可，不用太緊張

  Day 7-28  ：審核結果通知
              ├── 通過 → 收到通知信 → 你的 Console 開通 Mini App 功能
              └── 不通過 → 收到退件原因 → 修改後重新申請

⏳ 等待期間可以先做的事：
  ✅ 製作 Channel Icon（步驟五 Part A）
  ✅ 製作 loading.gif（步驟五 Part B）
  ✅ 閱讀本文件其餘章節，了解後續步驟
  ✅ 用現有 LIFF 環境繼續日常運作
```

### 2.4 如何確認申請已通過？

```
方法一：收到 LINE Taiwan 的通知 Email
  → 信件會說「您的 LINE Mini App 開發權限已開通」或類似訊息

方法二：自己去 Console 確認
  1. 打開 https://developers.line.biz/console/
  2. 用你的 LINE 帳號登入
  3. 選擇你的 Provider
  4. 點擊「Create a new channel」
  5. 看列表中有沒有「LINE MINI App」選項

  ┌─ 看到以下列表 ────────────────────────┐
  │                                        │
  │  ○ Messaging API                       │
  │  ○ LINE Login                          │
  │  ○ LINE MINI App     ← 有這個就對了！  │
  │  ○ LINE MINI App - Web App             │
  │  ○ Blockchain Service                  │
  │                                        │
  └────────────────────────────────────────┘

  ✅ 看到「LINE MINI App」→ 權限已開通，進入第三章！
  ❌ 沒看到 → 還在審核中，繼續等
```

---

## 第三章：在 LINE Developers Console 建立 Mini App Channel

### 3.1 開始之前，最重要的一件事

```
╔═══════════════════════════════════════════════════════════════════════╗
║                                                                       ║
║  ⭐⭐⭐ 超級重要 ⭐⭐⭐                                              ║
║                                                                       ║
║  新的 Mini App Channel 必須建在                                       ║
║  和現有 LINE Login Channel（ID: 2007621061）「同一個 Provider」下！    ║
║                                                                       ║
║  原因：                                                               ║
║  LINE 的 User ID 是「Provider 層級」的。                              ║
║  同一個 Provider 下 → 同一個教友 → 同一個 userId                     ║
║  不同的 Provider 下 → 同一個教友 → 不同的 userId ❌                   ║
║                                                                       ║
║  如果建錯 Provider：                                                  ║
║  → 教友之前綁定的 userId 全部對不上                                   ║
║  → 所有人要重新綁定                                                   ║
║  → 這是災難！😱                                                      ║
║                                                                       ║
║  所以：先確認你的 Provider → 再建立 Channel                          ║
║                                                                       ║
╚═══════════════════════════════════════════════════════════════════════╝
```

### 3.2 Step by Step 操作

#### Step 1：登入 LINE Developers Console

```
🔵 打開瀏覽器
   ↓
   網址：https://developers.line.biz/console/
   ↓
   點擊右上角「Log in」
   ↓
   選擇「Log in with LINE account」
   ↓
   用你的 LINE 帳號（綁定的 Email 或掃 QR Code）登入
   ↓
   登入成功後，你會看到 Console 首頁
```

#### Step 2：找到正確的 Provider

```
🔵 登入後的畫面：

   ┌──────────────────────────────────────────────┐
   │  LINE Developers Console                      │
   │                                               │
   │  Providers                                    │
   │  ┌────────────────────────────────────────┐   │
   │  │ 📁 你的 Provider 名稱                  │   │  ← 點這個！
   │  │    Channels: 1                         │   │
   │  └────────────────────────────────────────┘   │
   │                                               │
   │  （可能還有其他 Provider）                     │
   └──────────────────────────────────────────────┘

   ↓ 點進去後

   ┌──────────────────────────────────────────────┐
   │  Provider: 你的 Provider 名稱                 │
   │                                               │
   │  Channels                                     │
   │  ┌────────────────────────────────────────┐   │
   │  │ 📱 LINE Login                          │   │  ← 確認這裡有 LINE Login
   │  │    Channel ID: 2007621061              │   │     Channel ID 是 2007621061
   │  │    Status: Published                   │   │
   │  └────────────────────────────────────────┘   │
   │                                               │
   │  [+ Create a new channel]                     │  ← 等下要點這個
   └──────────────────────────────────────────────┘

   ✅ 確認事項：
      □ 看到 LINE Login Channel
      □ Channel ID 是 2007621061
      □ 你是在「這個 Provider」下面
      → 都確認了？往下一步
```

#### Step 3：建立新 Channel

```
🔵 點擊「Create a new channel」按鈕
   ↓
   出現 Channel 類型選擇列表
   ↓
   找到並點擊「LINE MINI App」
   
   ⚠️ 看不到「LINE MINI App」選項？
      → 你的開發權限還沒開通，回第二章
```

#### Step 4：填寫 Channel 資訊

```
🔵 出現建立表單，照以下填寫：
```

| # | 欄位名稱 | 怎麼填 | 在哪裡會顯示 |
|---|---------|--------|-------------|
| 1 | **Region** | 下拉選 `Taiwan` | — |
| 2 | **Channel icon** | 點「Upload」上傳圖片<br>（還沒做好可以先跳過，之後補） | 搜尋結果、授權畫面、分享卡片 |
| 3 | **Channel name** | 輸入 `行道會聖谷教會` | 搜尋結果、授權畫面<br>⚠️ **不能包含 "LINE"** |
| 4 | **Channel description** | 輸入：<br>`行道會聖谷教會教會管理系統，提供小組牧養回報、線上奉獻、會友管理等服務` | 授權畫面 |
| 5 | **Email address** | 輸入 `mengsunghu@gmail.com` | — |
| 6 | **Privacy policy URL** | 輸入：<br>`https://jesus.speechmessage.com.tw:807/Privacy` | 授權畫面 |
| 7 | **Terms of use URL** | 先留空（非必填） | 授權畫面 |
| 8 | **Service company's country/region** | 下拉選 `Taiwan` | 授權畫面 |

```
⚠️ Channel name 規則：
  ✅ 「行道會聖谷教會」           → OK
  ✅ 「行道會聖谷教會教會系統」    → OK
  ❌ 「行道會聖谷教會 LINE」       → 不行！包含 "LINE" 字樣
  ❌ 「LINE 行道會聖谷教會」       → 不行！
  ❌ 「行道會聖谷教會Line系統」    → 不行！大小寫都不行
```

#### Step 5：同意條款

```
🔵 畫面最下方有 3 個勾選方塊：

   ☐ I have read and agree to the LINE Developers Agreement
   ☐ I have read and agree to the LINE MINI App Platform Agreement  
   ☐ I have read and agree to the LINE MINI App Policy

   → 全部打勾 ✅✅✅
```

#### Step 6：建立

```
🔵 點擊「Create」按鈕
   ↓
   跳出對話框：「Regarding Consent to Usage of the Information」
   ↓
   點擊「Accept」
   ↓
   🎉 Channel 建立成功！
```

### 3.3 馬上記錄 5 組重要資訊

建立完成後，**立刻**做以下事情：

#### 記錄 Channel ID 和 Channel Secret

```
🔵 在新建的 Channel 頁面

   點擊上方的「Basic settings」分頁
   ↓
   找到以下兩個欄位，把值抄下來：

   ┌──────────────────────────────────────────────┐
   │  Basic settings                               │
   │                                               │
   │  Channel ID                                   │
   │  ┌──────────────────────────────────┐         │
   │  │  2012345678                      │ [Copy]  │  ← 記下這個！
   │  └──────────────────────────────────┘         │
   │                                               │
   │  Channel secret                               │
   │  ┌──────────────────────────────────┐         │
   │  │  a1b2c3d4e5f6g7h8i9j0k1l2m3n4  │ [Copy]  │  ← 記下這個！
   │  └──────────────────────────────────┘         │
   └──────────────────────────────────────────────┘
```

#### 記錄三組 LIFF ID

```
🔵 點擊上方的「Web app settings」分頁
   （有些版本叫「LIFF」分頁）
   ↓
   你會看到三個環境，每個都有 LIFF ID：

   ┌──────────────────────────────────────────────────────┐
   │  Web app settings                                     │
   │                                                       │
   │  📦 Developing                                        │
   │     LIFF ID: 2012345678-AbCdEfGh        [Copy]       │  ← 記下！
   │     Endpoint URL: (empty)                             │
   │                                                       │
   │  🔍 Review                                            │
   │     LIFF ID: 2012345678-XxYyZzWw        [Copy]       │  ← 記下！
   │     Endpoint URL: (empty)                             │
   │                                                       │
   │  🌐 Published                                         │
   │     LIFF ID: 2012345678-MmNnOoPp        [Copy]       │  ← 記下！
   │     Endpoint URL: (empty)                             │
   └──────────────────────────────────────────────────────┘
```

### 3.4 你的記錄表（填在這裡）

```
把你剛才記下的值填在下面（或截圖保存）：

Channel ID:          ________________________________________

Channel Secret:      ________________________________________

Developing LIFF ID:  ________________________________________

Review LIFF ID:      ________________________________________

Published LIFF ID:   ________________________________________
```

### ✅ 完成標誌

```
  □ Channel 已建立（在正確的 Provider 下）
  □ Channel ID 已記錄
  □ Channel Secret 已記錄
  □ Developing LIFF ID 已記錄
  □ Review LIFF ID 已記錄
  □ Published LIFF ID 已記錄
```

---

## 第四章：把 Console 資訊填入 appsettings.json

### 4.1 這步在做什麼？

```
你在第三章從 LINE Console 記下了 5 組資訊。
現在要把它們填入程式碼的設定檔 appsettings.json。
這樣程式就知道要用哪個 LIFF ID。
```

### 4.2 操作步驟

```
🔵 Step 1：在 Visual Studio 中打開
   
   檔案路徑：ChurchReport\appsettings.json
```

```
🔵 Step 2：找到 MiniApp 區段（約第 103 行附近）

   搜尋關鍵字 "MiniApp" 或 "ChannelId": ""
   你會看到：

   "MiniApp": {
       "ChannelId": "",              ← 空的，要填
       "ChannelSecret": "",          ← 空的，要填
       "DevelopingLiffId": "",       ← 空的，要填
       "ReviewLiffId": "",           ← 空的，要填
       "PublishedLiffId": "",        ← 空的，要填
       "ActiveEnvironment": "Developing",
       ...
   }
```

```
🔵 Step 3：填入你記錄的 5 組值

   ⚠️ 重要：只改這 5 行！其他的不要動！
```

#### 填寫範例

假設你記到的值是：

```
Channel ID:          2012345678
Channel Secret:      a1b2c3d4e5f6g7h8i9j0k1l2m3n4
Developing LIFF ID:  2012345678-AbCdEfGh
Review LIFF ID:      2012345678-XxYyZzWw
Published LIFF ID:   2012345678-MmNnOoPp
```

那你要把 appsettings.json 改成：

```jsonc
"MiniApp": {
    "ChannelId": "2012345678",                        // ← 填你的 Channel ID
    "ChannelSecret": "a1b2c3d4e5f6g7h8i9j0k1l2m3n4", // ← 填你的 Channel Secret

    "DevelopingLiffId": "2012345678-AbCdEfGh",        // ← 填 Developing 的 LIFF ID
    "ReviewLiffId": "2012345678-XxYyZzWw",            // ← 填 Review 的 LIFF ID
    "PublishedLiffId": "2012345678-MmNnOoPp",         // ← 填 Published 的 LIFF ID

    "ActiveEnvironment": "Developing",                 // ← 先不改，保持 Developing
    // ... 以下不動 ...
}
```

### 4.3 常見犯錯清單

```
❌ 錯誤 1：忘了加引號
   "ChannelId": 2012345678              ← ❌ JSON 字串一定要有引號！
   "ChannelId": "2012345678"            ← ✅ 正確

❌ 錯誤 2：多了空格或換行
   "ChannelId": " 2012345678 "          ← ❌ 前後多了空格
   "ChannelId": "2012345678"            ← ✅ 正確

❌ 錯誤 3：把 Developing 和 Review 搞混
   "DevelopingLiffId": "2012345678-XxYyZzWw",  ← ❌ 這是 Review 的！
   "ReviewLiffId": "2012345678-AbCdEfGh",      ← ❌ 這是 Developing 的！
   → 每個 LIFF ID 最後幾個字元不同，注意對應

❌ 錯誤 4：改到舊的 Liff 區段
   "Liff": {
       "BindingLiffId": "1653819697-YkPyPkr6",   ← ❌ 這是舊的，不要改！
       "LoginLiffId": "2007621061-Exd9BGv8"       ← ❌ 這是舊的，不要改！
   },
   "MiniApp": {
       "DevelopingLiffId": "2012345678-AbCdEfGh",  ← ✅ 改這組才對！
       ...
   }

❌ 錯誤 5：不小心刪掉逗號
   "ChannelId": "2012345678"            ← ❌ 少了結尾逗號
   "ChannelSecret": "a1b2c3d4..."
   
   "ChannelId": "2012345678",           ← ✅ 別忘記逗號
   "ChannelSecret": "a1b2c3d4..."
```

### 4.4 確認方式

```
🔵 Step 4：Ctrl + S 存檔

🔵 Step 5：在 Visual Studio 按 Ctrl + Shift + B（Build）
   ↓
   看 Output 視窗
   ↓
   如果顯示「Build succeeded」→ ✅ 沒有語法錯誤
   如果顯示「Build failed」→ 檢查是不是 JSON 語法錯了（少了引號或逗號）
```

---

## 第五章：設定 Endpoint URL（讓 LINE 知道要載入哪個網頁）

### 5.1 這步在做什麼？

```
告訴 LINE：
  「教友用 LINE 打開行道會聖谷教會 Mini App 時，
    請載入這個網頁 → https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{LIFF_ID}」

這個就是 Endpoint URL。
三個環境各設一個，差別只在最後的 LIFF ID。
```

### 5.2 為什麼 URL 要帶 LIFF ID？

```
因為行道會聖谷教會的 LineIdLoginView Action 是這樣寫的：

  [Route("/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}")]
  public IActionResult LineIdLoginView(string LineIdLoginViewPatameter)
  {
      ...
      TempData["Proponent"] = LineIdLoginViewPatameter;  // URL 的最後一段
      ...
  }

然後前端 JavaScript 是這樣用的：

  liff.init({ liffId: '@TempData["Proponent"]' })

也就是說：
  URL 最後一段 → TempData["Proponent"] → liff.init() 的 liffId
  
  所以 URL 帶什麼 LIFF ID，前端就用什麼 LIFF ID 做初始化
  完全自動，不用改任何程式碼！
```

### 5.3 操作步驟

```
🔵 Step 1：回到 LINE Developers Console
   ↓
   https://developers.line.biz/console/
   ↓
   選擇你的 Provider → 選擇行道會聖谷教會 Mini App Channel
   ↓
   點擊「Web app settings」分頁
```

```
🔵 Step 2：設定 Developing 的 Endpoint URL

   找到 Developing 環境的「Endpoint URL」欄位
   ↓
   填入：
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Developing_LIFF_ID}
```

> 把 `{你的Developing_LIFF_ID}` 替換成你在第三章記錄的 Developing LIFF ID。

```
🔵 Step 3：設定 Review 的 Endpoint URL（同樣格式，換 Review LIFF ID）
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Review_LIFF_ID}
```

```
🔵 Step 4：設定 Published 的 Endpoint URL（同樣格式，換 Published LIFF ID）
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Published_LIFF_ID}
```

```
🔵 Step 5：點「Save」或「Update」儲存
```

### 5.4 三個 URL 對照表（方便複製）

| 環境 | Endpoint URL |
|------|-------------|
| **Developing** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/` + Developing LIFF ID |
| **Review** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/` + Review LIFF ID |
| **Published** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/` + Published LIFF ID |

### 5.5 加入 Tester（不加就打不開！）

```
⚠️ Developing 和 Review 環境只有 Tester 才能打開！
   你自己不加為 Tester 的話，連你自己都打不開！

🔵 Step 6：加入測試人員
   ↓
   在 Console 中，點擊「Roles」分頁
   ↓
   點擊「Add Tester」
   ↓
   方式 A：輸入 LINE User ID
   方式 B：讓對方掃 QR Code
   ↓
   至少加入：
   □ 你自己
   □ 1-2 位教會同工（幫忙測試）

💡 怎麼找自己的 LINE User ID？
   方法 1：你之前用行道會聖谷教會 LIFF 登入時，後端 Console 日誌有印出 userId
   方法 2：等 Mini App 跑起來後，看瀏覽器 F12 → Console 裡的
          [LINE Profile] { UserId: "Uxxxxxxxxx..." }
```

---

## 第六章：準備兩張圖片（Channel Icon + loading.gif）

### 6.1 Channel Icon（Mini App 圖示）

#### 規格

```
┌──────────────────────────────────────────────────────┐
│  Channel Icon 規格                                    │
│                                                      │
│  ✅ 格式：PNG 或 JPG                                 │
│  ✅ 建議尺寸：500 × 500 px（最小 100 × 100 px）     │
│  ✅ 必須有底色（不能透明背景）                        │
│  ❌ 不能包含 "LINE" 字樣                             │
│                                                      │
│  用在這些地方：                                       │
│  • LINE 搜尋結果                                     │
│  • 授權同意畫面                                       │
│  • 分享卡片                                           │
│  • Service Message 通知                              │
│  • 首頁推薦                                           │
└──────────────────────────────────────────────────────┘
```

#### 你已經有教會 LOGO

```
檔案位置：ChurchReport\wwwroot\assets\images\ChurchLogo.png

你可以用這張做底圖，加上品牌色背景：
  品牌色：#4864b8（從你的 CSS 中取得的藍色）
```

#### 3 種製作方式

```
方案 A：用小畫家（最快，Windows 內建）
  1. 打開小畫家
  2. 「調整大小」→ 取消「維持比例」→ 500 × 500 像素
  3. 用「色彩選擇」工具填充底色 #4864b8
  4. 「貼上來源」→ 選擇 ChurchLogo.png → 放在中央
  5. 另存為 PNG → good-shepherd-icon.png

方案 B：用 Canva（免費線上工具，更好看）
  1. 前往 https://www.canva.com/ （免費註冊）
  2. 點「建立設計」→「自訂大小」→ 500 × 500 px
  3. 左邊「背景」→ 輸入色碼 #4864b8
  4. 左邊「上傳」→ 上傳 ChurchLogo.png → 拖到中央
  5. 可選：加「行道會聖谷教會」文字
  6. 右上角「下載」→ PNG → good-shepherd-icon.png

方案 C：請設計師
  規格：500×500px PNG，品牌色 #4864b8，教會名「行道會聖谷教會」
```

#### 上傳到 Console

```
🔵 回到 LINE Developers Console
   → 你的 Mini App Channel
   → 「Basic settings」分頁
   → Channel icon 欄位 → 「Upload」→ 上傳 good-shepherd-icon.png
   → 儲存
```

### 6.2 loading.gif（載入動畫）

#### 規格

```
┌──────────────────────────────────────────────────────┐
│  loading.gif 規格                                     │
│                                                      │
│  ✅ 格式：GIF（動態最佳）或 PNG（靜態也行）           │
│  ✅ 建議尺寸：240 × 240 px                           │
│  ✅ 檔名：必須是 loading.gif（全小寫）               │
│  ✅ 放在：wwwroot 根目錄                              │
│                                                      │
│  LINE 會在載入你的網頁時顯示這個動畫                  │
│  讓教友不會看到白屏                                   │
└──────────────────────────────────────────────────────┘
```

#### 製作方式

```
最快的方式：用 https://loading.io/

  1. 打開 https://loading.io/
  2. 選一個喜歡的動畫樣式（例如旋轉圓圈）
  3. 把顏色改成 #4864b8
  4. 尺寸改成 240 × 240
  5. 下載 GIF
  6. 檔名改成 loading.gif

次快的方式：用教會 LOGO 做靜態圖
  1. 把 ChurchLogo.png 縮小到 240 × 240
  2. 改名為 loading.gif（就算是 PNG 內容也能用）
```

#### 放到正確位置

```
把 loading.gif 複製到：

  ChurchReport
  └── wwwroot
       ├── favicon.ico
       ├── loading.gif    ← 放在這裡！和 favicon.ico 同一層
       ├── css/
       ├── js/
       └── ...

驗證方式：
  部署後在瀏覽器打開：
  https://jesus.speechmessage.com.tw:807/loading.gif
  → 能看到圖片就 OK ✅
```

---

## 第七章：部署到伺服器

### 7.1 步驟

```
🔵 Step 1：確認所有修改都存檔了
   ↓
   Ctrl + Shift + S（全部存檔）

🔵 Step 2：Build 確認沒有錯誤
   ↓
   Ctrl + Shift + B
   ↓
   Output 顯示「Build succeeded」→ ✅

🔵 Step 3：用你平常的方式部署到伺服器
   ↓
   （通常是 Publish → 複製到 IIS → 重啟站台）
   ↓
   部署到 jesus.speechmessage.com.tw:807

🔵 Step 4：驗證部署成功
   ↓
   在瀏覽器打開以下網址，確認都能正常運作：
```

```
驗證清單：

  □ https://jesus.speechmessage.com.tw:807/Privacy
    → 要看到「行道會聖谷教會 隱私政策」頁面

  □ https://jesus.speechmessage.com.tw:807/loading.gif
    → 要看到載入動畫圖片

  □ https://jesus.speechmessage.com.tw:807/Authentication/Login
    → 要看到帳號密碼登入頁
    
  □ https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/test
    → 要看到 LINE 登入頁面（LIFF 初始化會失敗，但頁面本身要能載入）
```

---

## 第八章：用手機測試（超詳細測試手冊）

### 8.1 測試前準備

```
你需要：
  📱 一支手機（iOS 或 Android 都行）
  📲 手機已安裝最新版 LINE App
  ✅ 你的 LINE 帳號已加為 Tester（第五章 Step 6）
  🌐 手機連得上網路
```

### 8.2 如何打開你的 Mini App

```
方法 1（推薦）：在 LINE 聊天室貼連結
  ↓
  在手機 LINE 的「記事本」或任何聊天室
  ↓
  輸入以下連結：

  https://liff.line.me/{你的Developing_LIFF_ID}

  例如：
  https://liff.line.me/2012345678-AbCdEfGh
  ↓
  發送後點擊該連結
  ↓
  LINE 會在內建瀏覽器中打開你的 Mini App


方法 2：掃 QR Code
  ↓
  在電腦上把上面的 URL 產生 QR Code
  （Google 搜尋「QR Code generator」就有免費工具）
  ↓
  用手機 LINE 的 QR Code 掃描器掃描
```

### 8.3 測試清單（逐項打勾）

#### 📋 A. 基本載入測試

```
  □ A1. 點擊連結後，是否看到 loading 動畫？
        → 如果看到白屏或錯誤 → 看附錄A「問題 1」
        → ✅ 看到 loading 圖片

  □ A2. loading 結束後，是否看到行道會聖谷教會登入頁面？
        → 應該看到：
           • Hero 圖片（教會照片輪播）
           • 「行道會聖谷教會」標題
           • 經文「我們愛，因為神先愛我們」
           • 聯絡資訊卡片
        → ✅ 頁面正常顯示

  □ A3. 頂部是否有 LINE 的 Header（包含返回按鈕和 Mini App 名稱）？
        → 這是 LINE 自動加的，你不用做任何事
        → ✅ 有看到 LINE Header
```

#### 📋 B. LIFF 登入測試

```
  □ B1. 是否自動開始 LIFF 初始化？
        → 頁面應顯示「正在準備 LINE 登入...」
        → 然後自動偵測環境
        → ✅ 有看到初始化訊息

  □ B2. 是否成功取得你的 LINE 名稱？
        → 應該看到「歡迎 [你的名字] 登入」
        → ✅ 有顯示你的 LINE 名稱

  □ B3. 是否出現 LoadPanel（「登入中，請稍候…」）？
        → ✅ 有顯示
```

#### 📋 C. 登入後導向測試

```
  □ C1. 如果你是已綁定教友：
        → 應該自動導向 IntegrateView 或 MultiGroupView
        → 看到小組管理頁面
        → ✅ 正確導向

  □ C2. 如果你是未綁定教友：
        → 應該導向 LineLiffView（綁定頁面）
        → 可以輸入姓名和電話完成綁定
        → ✅ 正確導向

  □ C3. 導向後的頁面功能是否正常？
        → DataGrid 能正常顯示資料
        → 可以滑動、點擊
        → ✅ 功能正常
```

#### 📋 D. UI 適配測試

```
  □ D1. 內容是否被 LINE Header 遮住？
        → 頂部內容不應該被遮住
        → Safe Area CSS 會處理這個
        → ✅ 沒有被遮住

  □ D2. 底部內容是否被 iOS Home Indicator 遮住？
        → Copyright 文字不應該被切掉
        → ✅ 沒有被遮住

  □ D3. Hero 圖片是否正常顯示？
        → 教會照片輪播正常運作
        → ✅ 正常顯示

  □ D4. Toast 通知位置是否正確？
        → 不應該出現在 LINE Header 後面
        → ✅ 位置正確
```

#### 📋 E. 奉獻流程測試（選做）

```
  □ E1. 奉獻入口是否能正常打開？
  □ E2. 奉獻表單是否正常顯示？
  □ E3. 奉獻紀錄查詢是否正常？
```

#### 📋 F. 多平台測試（建議都測）

```
  □ F1. Android 手機的 LINE App → ✅
  □ F2. iPhone 的 LINE App → ✅
  □ F3. 電腦版 LINE → 應該自動走 Server-side OAuth → ✅
```

---

## 第九章：送 LINE 審核

### 9.1 送審前確認清單

```
以下全部確認後才能送審：

  □ Developing 環境測試全部通過（第八章）
  □ Channel Icon 已上傳（500×500 PNG）
  □ Channel name = "行道會聖谷教會"（不含 "LINE"）
  □ Channel description 已填寫
  □ Email address 已填寫
  □ Privacy policy URL 已填寫且可存取
  □ Review 環境的 Endpoint URL 已設定
  □ 頁面在 3 秒內完成載入
  □ 沒有 JavaScript 錯誤（F12 Console 沒有紅色錯誤）
```

### 9.2 操作步驟

```
🔵 Step 1：確認 Review 環境

   回到 Console → Web app settings → Review 環境
   → 確認 Endpoint URL 已正確設定（第五章做過了）

🔵 Step 2：確認 Channel 資訊完整

   Basic settings 分頁：
   □ Channel icon ✅
   □ Channel name ✅
   □ Channel description ✅
   □ Email address ✅
   □ Privacy policy URL ✅

🔵 Step 3：點擊「Submit for review」
   
   在 Channel 頁面找到這個按鈕
   （通常在 Overview 或頁面頂部/底部）

🔵 Step 4：填寫審核說明
```

#### 審核說明範本（直接複製貼上）

```
【服務說明】
行道會聖谷教會是一個教會會友管理系統，提供以下功能：
1. LINE 帳號綁定與自動登入
2. 小組牧養回報（出席、探訪、代禱事項）
3. 線上奉獻（支援永豐銀行 QPay / 高鉅科技 MyPay / 台新銀行 TSPG）
4. 奉獻紀錄查詢
5. 個人資料維護

教友可以透過 LINE 登入，直接在 LINE 中管理小組事務和進行線上奉獻。

【測試步驟】
1. 開啟 Mini App 後，系統自動進行 LINE 登入
2. 若為已綁定用戶，自動導向小組管理頁面
3. 若為新用戶，導向綁定註冊頁面（可輸入姓名和電話完成綁定）
4. 已綁定用戶可在 DataGrid 中查看和編輯小組成員資料
5. 點擊奉獻功能，可進入線上奉獻流程

【技術資訊】
- 後端：ASP.NET Core (.NET 10)
- 前端：DevExtreme + jQuery + LIFF SDK 2.x
- 所有連線皆使用 HTTPS

【注意事項】
- 部分功能需要已綁定的教友帳號才能完整測試
- 系統連接 Dynamics 365 CRM 作為資料庫
```

```
🔵 Step 5：提交

🔵 Step 6：等待審核（通常數個工作天）
   → LINE 會寄 Email 通知結果
```

### 9.3 萬一被退件怎麼辦？

```
不要緊張！被退件很正常。LINE 會告訴你退件原因。

常見退件原因：

  1.「隱私政策頁面無法存取」
     → 確認伺服器正常運作
     → 用手機試試能不能打開 https://jesus.speechmessage.com.tw:807/Privacy

  2.「頁面載入太慢」
     → 壓縮圖片（特別是 Hero 區域的 church-001.jpg、church-002.jpg）
     → 確認伺服器效能

  3.「頁面出現錯誤」
     → 用手機 LINE 打開 Mini App
     → 看有沒有白屏或 JS 錯誤

  4.「Channel name 包含 LINE」
     → 修改名稱，去掉 "LINE" 相關字

  5.「功能無法正常使用」
     → 確認 CRM 連線正常
     → 確認有測試資料可展示

  修復後 → 重新部署到伺服器 → 再次點「Submit for review」
```

---

## 第十章：審核通過 → 正式上線 🎉

### 10.1 收到審核通過通知

```
LINE 會寄 Email 告訴你審核通過了！🎉

接下來的步驟：
```

### 10.2 修改 ActiveEnvironment

```
🔵 Step 1：打開 ChurchReport\appsettings.json

🔵 Step 2：找到 MiniApp 區段，修改 ActiveEnvironment：

   修改前：
   "ActiveEnvironment": "Developing"
   
   修改後：
   "ActiveEnvironment": "Published"

🔵 Step 3：Ctrl + S 存檔
```

### 10.3 重新部署

```
🔵 Step 4：Build → 部署到伺服器
   （和第七章一樣的流程）
```

### 10.4 最終測試

```
🔵 Step 5：用手機打開 Mini App
   → 這次用 Published 的 LIFF URL 測試：
   
   https://liff.line.me/{你的Published_LIFF_ID}
   
   → 確認所有功能正常
```

### 10.5 設定 Custom Path（選用）

```
🔵 Step 6（選用）：設定好記的短網址

   Console → Web app settings → Published 環境
   → 找到「Custom Path」欄位
   → 輸入：good-shepherd
   → 儲存
   
   之後教友就可以用這個網址：
   https://miniapp.line.me/good-shepherd
```

### 10.6 通知教友 🎉

```
🔵 Step 7：把 Mini App 的連結分享給教友

   在 LINE 群組或教會公告中發送：

   ─────────────────────────────────────
   📢 好消息！行道會聖谷教會已升級為 LINE Mini App！

   現在可以在 LINE 中直接使用行道會聖谷教會系統：
   
   🔗 點擊開啟：https://miniapp.line.me/{Published_LIFF_ID}
   
   功能不變，但使用更方便：
   ✅ 在 LINE 中直接搜尋「行道會聖谷教會」
   ✅ 可以加到手機主畫面，像 App 一樣使用
   ✅ 自動 LINE 登入，不用輸入帳號密碼
   ─────────────────────────────────────
```

---

## 附錄A：完整問題排查手冊（遇到問題看這裡）

### 問題 1：打開 Mini App 後白屏

```
可能原因 A：LIFF ID 填錯
  → 檢查 appsettings.json 的 DevelopingLiffId 是否正確
  → 檢查 Console 的 Endpoint URL 末尾的 LIFF ID 是否一致
  
可能原因 B：Endpoint URL 錯誤
  → 在電腦瀏覽器直接打開 Endpoint URL
  → 如果電腦也打不開，是伺服器或路由問題

可能原因 C：HTTPS 憑證問題
  → 確認 https://jesus.speechmessage.com.tw:807 沒有 SSL 錯誤
  → LINE 要求必須是合法的 HTTPS
```

### 問題 2：LIFF 初始化失敗（INVALID_ARGUMENT）

```
  → LIFF ID 和 Console 設定不一致
  → 確認你用的是「Mini App Channel」的 LIFF ID
  → 不是舊的「LINE Login Channel」的 LIFF ID
  
  Mini App LIFF ID ≠ LINE Login LIFF ID
  "2007621061-Exd9BGv8" ← 這是舊的 LINE Login，不要用在 Mini App
  "2012345678-AbCdEfGh" ← 這才是 Mini App 的
```

### 問題 3：「你不是 Tester」或拒絕存取

```
  → 你沒有被加為 Tester
  → Console → Roles → Add Tester → 加入你自己的 LINE 帳號
  → 加完後等 1-2 分鐘再試
```

### 問題 4：登入後 userId 不一樣（教友資料對不上）

```
  → ⚠️ 最嚴重的問題！
  → Mini App Channel 建在了不同的 Provider！
  → 解決方式：
     1. 刪除建錯的 Channel
     2. 在正確的 Provider 下重新建立（和 LINE Login Channel 同一個）
     3. 更新 appsettings.json 的 LIFF ID
```

### 問題 5：Session 丟失（需要重新登入）

```
  → 確認 Startup.cs 的 Cookie 設定：
     options.Cookie.SameSite = SameSiteMode.Lax;
     options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
  → 確認 HTTPS 正常運作
```

### 問題 6：DevExtreme 元件樣式異常

```
  → LINE 會注入自己的 CSS，可能影響你的樣式
  → 確認 CSS 載入順序正確：
     dx.common.css → dx.light.css → 你的自訂 CSS
  → mini-app-safe-area.css 應該最後載入
```

### 問題 7：電腦版 LINE 打開白屏

```
  → 正常行為！電腦版 LINE 的 LIFF Browser 有限制
  → 你的程式碼已經有處理：
     detectEnvironmentAndChooseMethod() 會偵測到電腦版
     → 自動切換到 Server-side OAuth（第 190-192 行）
  → 如果沒有自動切換，檢查 LINE Login Channel 設定：
     appsettings.json 的 LineLogin:ChannelId 是否正確
```

### 問題 8：隱私政策頁面打不開

```
  → 確認路由有註冊：
     Startup.cs 第 740-744 行有 privacy 路由
  → 確認 Controller 有 Privacy Action：
     AuthenticationController.Core.cs 第 81-86 行
  → 確認 View 檔案存在：
     Views/Authentication/Privacy.cshtml
  → 在瀏覽器直接打開確認：
     https://jesus.speechmessage.com.tw:807/Privacy
```

---

## 附錄B：名詞對照表

| 名詞 | 白話文解釋 | 在行道會聖谷教會系統的對應 |
|------|----------|-------------------|
| **LIFF** | LINE 前端框架，讓網頁可以跟 LINE 互動 | 我們已經在用了（LineIdLoginView.cshtml 第 136 行） |
| **LIFF ID** | 一組辨識碼，格式像 `1234567890-AbCdEfGh` | appsettings.json 裡的各種 LiffId |
| **LIFF Browser** | LINE App 裡面的內建瀏覽器 | 教友用 LINE 打開行道會聖谷教會時，就是跑在這裡面 |
| **Channel** | LINE 的服務設定單位 | 我們有兩個：LINE Login Channel + Mini App Channel |
| **Provider** | Channel 的上層資料夾，同 Provider → 同 userId | ⚠️ 必須和 LINE Login 在同一個 |
| **Endpoint URL** | LINE 打開你的 Mini App 時，實際載入的網頁網址 | 指向 LineIdLoginView |
| **Safe Area** | iOS 安全區域，避開瀏海和 Home Indicator | mini-app-safe-area.css 處理 |
| **Verified Mini App** | 通過 LINE 審核，擁有完整功能 | 最終目標 |
| **Service Message** | Mini App 的免費推播通知 | 通過審核後可用（appsettings 已預留設定） |
| **Custom Path** | 自訂短網址 `miniapp.line.me/xxx` | 審核通過後可設 |
| **TempData["Proponent"]** | ASP.NET MVC 的暫存資料，存放 LIFF ID | Controller 設定，View 裡 JS 使用 |
| **viewport-fit=cover** | CSS 設定，讓網頁延伸到安全區域外 | 已加到所有 cshtml 的 meta viewport |

---

## 附錄C：行道會聖谷教會專案程式碼與 Mini App 的對應關係

### C.1 LIFF 初始化流程（程式碼在哪裡）

```
教友打開 Mini App
       │
       │ LINE 載入 Endpoint URL
       ▼
LineIdLoginView.cshtml
       │
       │ 第 136 行：載入 LIFF SDK
       │ <script src="https://static.line-scdn.net/liff/edge/2/sdk.js">
       │
       │ 第 179 行：初始化 LIFF
       │ liff.init({ liffId: '@TempData["Proponent"]' })
       │
       │ 第 178 行：偵測環境
       │ detectEnvironmentAndChooseMethod()
       │
       ├── 手機版 LINE → useLiffSdk()（第 209 行）
       │     │
       │     │ 第 260 行：取得 Profile
       │     │ const profile = await liff.getProfile()
       │     │
       │     │ 第 277 行：AJAX 送到後端
       │     │ UpdateLineUserId(UserId, GroupId, RoomId, ViewType)
       │     │
       │     └──→ 後端 SaveUserLineId Action
       │           → 查 CRM → 已綁定：導向功能頁 / 未綁定：導向綁定頁
       │
       └── 電腦版 LINE → useServerSideOAuth()（第 164 行）
             └──→ /Authentication/LineLoginStart
                   → LINE OAuth 2.0 流程
```

### C.2 Mini App 相關檔案一覽

```
ChurchReport/
├── appsettings.json                          ← MiniApp 設定（第 97-131 行）
├── Startup.cs                                ← 中間件註冊（第 683 行）+ 路由（第 740 行）
├── Middleware/
│   └── MiniAppDetectionMiddleware.cs         ← 偵測是否來自 LINE LIFF Browser
├── Controllers/AuthenticationController/
│   ├── AuthenticationController.Core.cs      ← Privacy Action（第 81 行）
│   └── AuthenticationController.LineLogin.cs ← LineIdLoginView Action（第 25 行）
├── Views/Authentication/
│   ├── LineIdLoginView.cshtml                ← Mini App 主入口（viewport-fit + Safe Area）
│   ├── LineLiffView.cshtml                   ← 綁定頁面（viewport-fit + Safe Area）
│   ├── Login.cshtml                          ← 帳密登入（viewport-fit + Safe Area）
│   └── Privacy.cshtml                        ← 隱私政策頁面
├── Views/Dedication/
│   ├── DediationLineLoginView.cshtml         ← 奉獻登入（viewport-fit + Safe Area）
│   └── DedicationFeeView.cshtml              ← 奉獻紀錄（viewport-fit + Safe Area）
├── Views/Shared/
│   └── _Layout.cshtml                        ← 共用 Layout（viewport-fit + Safe Area）
└── wwwroot/
    ├── css/mini-app-safe-area.css            ← Safe Area CSS
    └── loading.gif                           ← 載入動畫（⬜ 需要你放進來）
```

---

## 📅 總時程表

```
Week 1（現在）：
  ☑ 提交 LINE Taiwan 申請（第二章）← 第一件事就做這個！
  ☑ 準備 Channel Icon + loading.gif（第六章）← 等待期間可以做

Week 2-4：
  ⏳ 等待 LINE Taiwan 審核
  ☑ 程式碼已全部完成
  ☑ 先用現有 LIFF 環境繼續日常運作

收到許可後（Week 4-5）：
  ☑ 建立 Mini App Channel（第三章）
  ☑ 填入 LIFF ID（第四章）
  ☑ 設定 Endpoint URL + Tester（第五章）
  ☑ 部署到伺服器（第七章）
  ☑ 手機測試（第八章）

測試完成後（Week 5-6）：
  ☑ 送 LINE 審核（第九章）
  ⏳ 等待審核（數個工作天）

審核通過（Week 6-7）：
  ☑ 正式上線！🎉（第十章）

📅 總計：約 5-7 週（主要在等 LINE 審核，實際操作時間只有 1-2 天）
```

---

*文件版本：v1.0*  
*建立日期：2025 年 7 月*  
*專案：ChurchReport 行道會聖谷教會教會管理系統*  
*分支：Jesus_5.0.4.LineMiniApp*
