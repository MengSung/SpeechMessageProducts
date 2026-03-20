# 🐑 好牧人 LINE Mini App — 手把手實作教學
### 給完全不懂 LINE Mini App 的大白癡看的超詳細操作手冊

> **前提**：程式碼層面的修改已全部完成（13 項 ✅）  
> **本文件目的**：帶你完成剩下的 6 個「人工操作」步驟  
> **預估總時間**：申請許可 1-4 週 ＋ 設定操作 1-2 天 ＋ 測試送審 1-2 週  
> **更新日期**：2025 年 7 月

---

## 📋 你現在在哪裡？（進度總覽）

```
已完成的程式碼修改（不用再動了）：
  ✅ 1.  appsettings.json      → 新增 MiniApp 設定區段
  ✅ 2.  Privacy.cshtml         → 隱私政策頁面
  ✅ 3.  AuthenticationController.Core.cs → Privacy 路由
  ✅ 4.  Startup.cs             → Privacy 路由 + 中間件註冊
  ✅ 5.  mini-app-safe-area.css → Safe Area CSS
  ✅ 6.  MiniAppDetectionMiddleware.cs → Mini App 偵測中間件
  ✅ 7.  LineIdLoginView.cshtml → viewport-fit + Safe Area CSS
  ✅ 8.  LineLiffView.cshtml    → viewport-fit + Safe Area CSS
  ✅ 9.  Login.cshtml           → viewport-fit + Safe Area CSS
  ✅ 10. DediationLineLoginView.cshtml → viewport-fit + Safe Area CSS
  ✅ 11. DedicationFeeView.cshtml → viewport-fit + Safe Area CSS
  ✅ 12. _Layout.cshtml         → viewport-fit + Safe Area CSS

接下來要做的（本文件教你）：
  ⬜ 步驟一：向 LINE Taiwan 申請 Mini App 開發許可
  ⬜ 步驟二：在 LINE Developers Console 建立 Mini App Channel
  ⬜ 步驟三：把三組 LIFF ID 填入 appsettings.json
  ⬜ 步驟四：設定三個環境的 Endpoint URL
  ⬜ 步驟五：準備 Channel Icon + loading.gif
  ⬜ 步驟六：測試 → 送審 → 上線
```

---

## 步驟一：向 LINE Taiwan 申請 Mini App 開發許可

### 💡 為什麼要先申請？

> 台灣地區**無法自行**在 LINE Developers Console 建立「LINE MINI App」類型的 Channel。  
> 你必須先向 LINE Taiwan 提出申請，經過審核通過後，LINE 才會幫你開啟建立 Mini App Channel 的權限。  
> **這是整個流程最慢的一步，建議最先做。**

### 📖 操作步驟

```
🔵 Step 1：打開瀏覽器
   ↓
   前往 https://tw.linebiz.com/service/other-solutions/line-mini-app/
   
🔵 Step 2：點擊頁面上的「立即申請」或「聯絡我們」按鈕
   （如果看不到按鈕，往下捲動頁面找）

🔵 Step 3：填寫申請表單
```

### 📝 表單填寫範例

請照以下內容填寫（根據你的實際情況修改）：

| 表單欄位 | 填寫內容 | 說明 |
|---------|---------|------|
| **公司/組織名稱** | `音訊科技有限公司` | 你的公司名 |
| **統一編號** | `13054485` | 從你的 appsettings.json 看到的 |
| **服務名稱** | `好牧人教會管理系統` | 你的 Mini App 名稱 |
| **服務說明** | 見下方完整範文 ↓ | 越詳細越好 |
| **預期上線時間** | `2025 年 9 月`（或你的時程） | 給自己留些時間 |
| **聯絡人姓名** | `（你的名字）` | |
| **聯絡人 Email** | `mengsunghu@gmail.com` | |
| **聯絡人電話** | `03-4316679` | |
| **公司網址** | `https://jesus.speechmessage.com.tw:807` | |
| **LINE Official Account ID** | （如果有就填，沒有留空）| |

### 📄 服務說明範文（複製貼上即可）

```
好牧人是一個教會會友管理系統，主要服務對象為台灣各教會會友。

【主要功能】
1. LINE 帳號快速登入：會友可以用 LINE 帳號直接登入系統
2. 小組牧養回報：小組長可回報每週聚會出席、探訪、代禱事項
3. 線上奉獻：支援永豐 QPay、高鉅 MyPay、台新 TSPG 金流
4. 奉獻紀錄查詢：會友可查詢自己的奉獻明細
5. 個人資料維護：會友可更新個人聯絡資訊

【技術架構】
- 後端：ASP.NET Core (.NET 10)
- 前端：DevExtreme + jQuery + LIFF SDK 2.x
- 資料庫：Microsoft Dynamics 365 CRM
- 伺服器：HTTPS（SSL 憑證正常）

【現有 LINE 整合】
- 已整合 LIFF SDK 2.x（LINE Login Channel ID: 2007621061）
- 已有 LINE 登入 + 綁定功能
- 希望升級為 LINE Mini App 以獲得更好的用戶體驗

【預期使用人數】
- 初期：約 200-500 名教會會友
- 目標：服務多間教會，預計 1000+ 用戶
```

### ⏳ 等待期間

```
申請送出後：
  ├── LINE Taiwan 會在 1-4 週內回覆
  ├── 可能會有 LINE 業務人員聯絡你了解更多
  ├── 通過後，你的 LINE Developers Console 會出現
  │   「LINE MINI App」的 Channel 類型選項
  └── 在等待期間，可以先做步驟五（準備圖示）

⚠️ 如果超過 4 週沒回覆，建議主動寫信追蹤：
   miniapp_tw@linecorp.com（LINE Taiwan 官方信箱，可能會變）
```

### ✅ 完成標誌

```
當你收到 LINE Taiwan 的通知信，告知你已開通 LINE Mini App 開發權限，
就可以進入步驟二了！

確認方式：
1. 登入 https://developers.line.biz/console/
2. 選擇你的 Provider
3. 點「Create a new channel」
4. 如果在列表中看到「LINE MINI App」選項 → ✅ 權限已開通！
```

---

## 步驟二：在 LINE Developers Console 建立 Mini App Channel

### 💡 這一步在做什麼？

> 在 LINE 的開發者後台，建立一個專屬於好牧人的 LINE Mini App Channel。  
> 建立後會自動產生三組 LIFF ID（Developing / Review / Published），  
> 這些 LIFF ID 就是讓你的網頁變成 LINE Mini App 的鑰匙。

### ⚠️ 超級重要的注意事項

```
╔══════════════════════════════════════════════════════════════╗
║  ⭐ 必須和現有的 LINE Login Channel 在同一個 Provider 下！    ║
║                                                              ║
║  為什麼？因為 LINE 的 User ID 是「Provider 層級」的。         ║
║  同一個 Provider 下，所有 Channel 拿到的 userId 都一樣。     ║
║  如果建在不同 Provider，教友之前綁定的 userId 就對不上了！    ║
║                                                              ║
║  你現有的 LINE Login Channel:                                ║
║    Channel ID: 2007621061                                    ║
║    → 找到這個 Channel 所屬的 Provider                        ║
║    → 在「同一個 Provider」下建立新的 Mini App Channel        ║
╚══════════════════════════════════════════════════════════════╝
```

### 📖 操作步驟（附每一步的說明）

```
🔵 Step 1：打開瀏覽器
   ↓
   前往 https://developers.line.biz/console/
   ↓
   用你的 LINE 帳號登入
   （如果已經登入就會直接看到 Console 首頁）
```

```
🔵 Step 2：找到正確的 Provider
   ↓
   登入後，你會看到畫面上列出你的 Provider（類似資料夾）
   ↓
   點擊那個包含 Channel ID: 2007621061（LINE Login）的 Provider
   ↓
   💡 如何確認？
      點進 Provider 後，你應該能看到已經有一個
      「LINE Login」類型的 Channel，
      Channel ID 是 2007621061
```

```
🔵 Step 3：建立新 Channel
   ↓
   在這個 Provider 的頁面中，點擊「Create a new channel」按鈕
   ↓
   你會看到一個列表，列出可以建立的 Channel 類型
```

```
🔵 Step 4：選擇 Channel 類型
   ↓
   在列表中找到並點擊「LINE MINI App」
   ↓
   ⚠️ 如果看不到這個選項：
      → 你的 LINE Mini App 開發權限還沒開通
      → 回到步驟一，確認 LINE Taiwan 已核准你的申請
```

```
🔵 Step 5：填寫 Channel 資訊
   ↓
   會出現一個表單，照以下方式填寫：
```

| 欄位 | 怎麼填 | 小白解說 |
|------|--------|---------|
| **Region** | 選 `Taiwan` | 你的服務地區 |
| **Channel icon** | 上傳你的好牧人 LOGO 圖片 | 500×500px PNG，參見步驟五 |
| **Channel name** | 輸入 `好牧人` | ⚠️ 絕對不能包含 "LINE" 這個字！ |
| **Channel description** | 輸入 `好牧人教會管理系統，提供小組牧養回報、線上奉獻、會友管理等服務` | 清楚說明你的服務 |
| **Email address** | 輸入 `mengsunghu@gmail.com` | 收重要通知用 |
| **Privacy policy URL** | 輸入 `https://jesus.speechmessage.com.tw:807/Privacy` | 就是我們已經建好的隱私政策頁面 |
| **Terms of use URL** | 可以先留空 | 非必填，之後再補 |
| **Service company's country/region** | 選 `Taiwan` | 必須跟 Region 一致 |

```
🔵 Step 6：同意條款
   ↓
   畫面下方有三個勾選方塊，全部打勾：
   ☑ LINE Developers Agreement
   ☑ LINE MINI App Platform Agreement
   ☑ LINE MINI App Policy
```

```
🔵 Step 7：點擊「Create」按鈕
```

```
🔵 Step 8：確認資料使用同意
   ↓
   會跳出一個對話框「Regarding Consent to Usage of the Information」
   ↓
   點擊「Accept」
```

```
🎉 建立成功！

建立完成後，LINE 會自動幫你產生三個內部環境：
  • Developing（開發用）
  • Review（送審用）
  • Published（正式上線用）

每個環境都有自己的 LIFF ID。
```

### 📋 立刻記錄以下資訊！

建立完成後，**馬上把以下資訊抄下來或截圖**：

```
🔵 Step 9：記錄 Channel 基本資訊
   ↓
   在新建的 Mini App Channel 頁面，點擊「Basic settings」分頁
   ↓
   找到並記下：
   
   Channel ID:     ____________________________
                    （一串數字，例如 2012345678）
   
   Channel secret: ____________________________
                    （一串英數字，例如 a1b2c3d4e5f6...）
```

```
🔵 Step 10：記錄三組 LIFF ID
   ↓
   點擊「Web app settings」分頁（或「LIFF」分頁，看版本）
   ↓
   你會看到三個環境，每個都有一個 LIFF ID，記下它們：

   Developing LIFF ID:  ____________________________
                         （格式像 1234567890-AbCdEfGh）
   
   Review LIFF ID:      ____________________________
                         （格式像 1234567890-XxYyZzWw）
   
   Published LIFF ID:   ____________________________
                         （格式像 1234567890-MmNnOoPp）
```

### ✅ 完成標誌

```
你應該已經記錄了 5 組資訊：
  □ Channel ID（一組）
  □ Channel Secret（一組）
  □ Developing LIFF ID（一組）
  □ Review LIFF ID（一組）
  □ Published LIFF ID（一組）

如果都記下來了 → 進入步驟三！
```

---

## 步驟三：把三組 LIFF ID 填入 appsettings.json

### 💡 這一步在做什麼？

> 把剛才從 LINE Console 記錄下來的資訊，填入我們程式碼中的設定檔。  
> 這樣程式就知道要用哪個 LIFF ID 來初始化 LINE Mini App。

### 📖 操作步驟

```
🔵 Step 1：在 Visual Studio 中打開檔案
   ↓
   檔案路徑：ChurchReport\appsettings.json
   （你現在應該已經開著了）
```

```
🔵 Step 2：找到 MiniApp 區段
   ↓
   在 appsettings.json 中搜尋 "MiniApp"
   你會看到大約在第 103 行附近：

   "MiniApp": {
     "ChannelId": "",
     "ChannelSecret": "",
     "DevelopingLiffId": "",
     "ReviewLiffId": "",
     "PublishedLiffId": "",
     ...
   }
```

```
🔵 Step 3：把你記錄的資訊填進去
   ↓
   把空字串 "" 替換成你在步驟二記錄的值
```

### 📝 填寫範例

假設你從 Console 記到的資訊是：

| 資訊 | 你記到的值（範例） |
|------|-----------------|
| Channel ID | `2012345678` |
| Channel Secret | `a1b2c3d4e5f6g7h8i9j0` |
| Developing LIFF ID | `2012345678-AbCdEfGh` |
| Review LIFF ID | `2012345678-XxYyZzWw` |
| Published LIFF ID | `2012345678-MmNnOoPp` |

那你要修改成這樣：

```jsonc
"MiniApp": {
    "ChannelId": "2012345678",                    // ← 填入你的 Channel ID
    "ChannelSecret": "a1b2c3d4e5f6g7h8i9j0",     // ← 填入你的 Channel Secret
    
    "DevelopingLiffId": "2012345678-AbCdEfGh",    // ← 填入 Developing 的 LIFF ID
    "ReviewLiffId": "2012345678-XxYyZzWw",        // ← 填入 Review 的 LIFF ID
    "PublishedLiffId": "2012345678-MmNnOoPp",     // ← 填入 Published 的 LIFF ID
    
    "ActiveEnvironment": "Developing",             // ← 先維持 Developing 不用改
    // ... 其他設定不用動 ...
}
```

```
🔵 Step 4：存檔
   ↓
   Ctrl + S 存檔
```

### ⚠️ 常見錯誤

```
❌ 錯誤 1：多了或少了引號
   "ChannelId": 2012345678          ← ❌ 少了引號
   "ChannelId": "2012345678"        ← ✅ 正確

❌ 錯誤 2：多了空格
   "ChannelId": " 2012345678 "     ← ❌ 前後有空格
   "ChannelId": "2012345678"        ← ✅ 正確

❌ 錯誤 3：填錯位置
   DevelopingLiffId 填到 ReviewLiffId 的位置  ← ❌
   每組 LIFF ID 不同，注意對應               ← ✅

❌ 錯誤 4：把舊的 Liff 設定改掉
   "Liff": { "BindingLiffId": "...", "LoginLiffId": "..." }
   ↑ 這組是舊的 LINE Login Channel 的，不要動它！
   
   "MiniApp": { "DevelopingLiffId": "...", ... }
   ↑ 這組是新的 Mini App Channel 的，填這裡！
```

### ✅ 完成標誌

```
確認以下 5 個欄位都填好了，不是空字串 ""：
  □ "ChannelId": "有值"
  □ "ChannelSecret": "有值"
  □ "DevelopingLiffId": "有值"
  □ "ReviewLiffId": "有值"
  □ "PublishedLiffId": "有值"

確認 ActiveEnvironment 是 "Developing"（先用開發環境測試）

存檔後，Build 一下專案確認沒有語法錯誤
```

---

## 步驟四：設定三個環境的 Endpoint URL

### 💡 這一步在做什麼？

> 告訴 LINE：「教友打開好牧人 Mini App 時，要載入哪個網頁」。  
> 這個網頁就是我們的 `LineIdLoginView` 登入頁面。

### 📖 操作步驟

```
🔵 Step 1：回到 LINE Developers Console
   ↓
   https://developers.line.biz/console/
   ↓
   選擇你的 Provider → 選擇好牧人 Mini App Channel
```

```
🔵 Step 2：進入 Web app settings
   ↓
   點擊「Web app settings」分頁
   ↓
   你會看到三個環境：Developing、Review、Published
   每個環境都有一個「Endpoint URL」欄位
```

```
🔵 Step 3：設定 Developing 環境的 Endpoint URL
   ↓
   在 Developing 的 Endpoint URL 欄位中填入：
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Developing_LIFF_ID}
```

**具體範例**（假設你的 Developing LIFF ID 是 `2012345678-AbCdEfGh`）：

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2012345678-AbCdEfGh
```

```
🔵 Step 4：設定 Review 環境的 Endpoint URL
   ↓
   同樣的格式，但換成 Review 的 LIFF ID：
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Review_LIFF_ID}
```

```
🔵 Step 5：設定 Published 環境的 Endpoint URL
   ↓
   同樣的格式，但換成 Published 的 LIFF ID：
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{你的Published_LIFF_ID}
```

```
🔵 Step 6：儲存
   ↓
   點擊「Save」或「Update」按鈕
```

### 💡 為什麼三個環境都指向同一台伺服器？

```
好牧人只有一台伺服器 (jesus.speechmessage.com.tw:807)，
所以三個環境都指向同一台，差別只在 URL 末尾的 LIFF ID 不同。

URL 結構解析：
  https://jesus.speechmessage.com.tw:807  ← 你的伺服器
  /Authentication/LineIdLoginView/         ← 登入頁面路由
  {LIFF_ID}                                ← 這個會變成 TempData["Proponent"]
                                              然後 JavaScript 中的 liff.init() 會用到它

也就是說：
  LINE 打開 Mini App
  → 載入 LineIdLoginView 頁面
  → 頁面中的 JavaScript 用 URL 裡的 LIFF ID 做 liff.init()
  → LIFF SDK 初始化成功
  → 自動走登入/綁定流程
  → 導向功能頁面

整個流程和現有的 LIFF App 一模一樣！只是 LIFF ID 換成 Mini App 的。
```

### 📝 三個 URL 的對照表（方便你複製）

| 環境 | Endpoint URL |
|------|-------------|
| **Developing** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Developing_LIFF_ID}` |
| **Review** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Review_LIFF_ID}` |
| **Published** | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Published_LIFF_ID}` |

> ⚠️ 記得把 `{xxx_LIFF_ID}` 替換成你在步驟二記錄的實際 LIFF ID！

### 🔵 Step 7：加入測試人員

```
還在 Console 裡，點擊「Roles」分頁
   ↓
點擊「Add Tester」
   ↓
輸入你自己的 LINE User ID，或讓你自己掃 QR Code 加入
   ↓
也把牧師、教會同工等需要測試的人加進來

💡 怎麼找自己的 LINE User ID？
   如果你之前用好牧人系統的 LIFF 登入過，
   你的 userId 已經存在 CRM 的會友資料裡。

   或者等 Mini App 可以打開後，
   在瀏覽器 F12 開發者工具中看 console.log 輸出的 userId。
```

### ✅ 完成標誌

```
確認以下全部完成：
  □ Developing Endpoint URL 已設定
  □ Review Endpoint URL 已設定
  □ Published Endpoint URL 已設定
  □ 你自己已加為 Tester
  □ 至少 1-2 位同工也加為 Tester
```

---

## 步驟五：準備 Channel Icon + loading.gif

### 💡 這一步在做什麼？

> 準備兩張圖片：  
> ① Channel Icon — 教友在 LINE 中看到的好牧人圖示  
> ② loading.gif — Mini App 載入時顯示的動畫

### 🖼️ Part A：Channel Icon（Mini App 圖示）

#### 規格要求

```
┌──────────────────────────────────────────────┐
│  Channel Icon 規格：                          │
│                                              │
│  ✅ 格式：PNG 或 JPG                         │
│  ✅ 尺寸：500 × 500 px（建議）               │
│           最小 100 × 100 px                  │
│  ✅ 必須有底色（不能透明背景）                │
│  ❌ 不能包含 "LINE" 字樣                     │
│  ❌ 不能是粗糙的低解析度圖片                 │
│                                              │
│  用途：                                       │
│  • LINE 搜尋結果中的圖示                     │
│  • LINE 首頁推薦的圖示                       │
│  • 授權同意畫面的圖示                        │
│  • Service Message 的圖示                    │
│  • 聊天分享卡片的圖示                        │
└──────────────────────────────────────────────┘
```

#### 製作方式

```
你已有教會 LOGO 圖片：
  ChurchReport\wwwroot\assets\images\ChurchLogo.png

方案 A：用現有 LOGO 加底色（最快）
  1. 打開任何圖片編輯工具（小畫家、Canva、Photoshop、GIMP 都行）
  2. 建立 500×500 px 新畫布
  3. 填充底色 #4864b8（好牧人品牌藍色）
  4. 把 ChurchLogo.png 放在正中央
  5. 存成 PNG
  6. 命名為 good-shepherd-icon.png

方案 B：用 Canva 線上設計（免費、更好看）
  1. 前往 https://www.canva.com/
  2. 點「建立設計」→「自訂大小」→ 500 × 500 px
  3. 背景色填 #4864b8
  4. 上傳 ChurchLogo.png 放在中央
  5. 可加上文字「好牧人」
  6. 下載 PNG

方案 C：請設計師幫忙（最好看）
  告訴設計師：
  - 500×500px PNG
  - 品牌色 #4864b8
  - 教會名「好牧人」
  - 風格簡約、圓角友好
```

#### 上傳到 Console

```
🔵 回到 LINE Developers Console
   → 好牧人 Mini App Channel
   → 「Basic settings」分頁
   → 找到「Channel icon」
   → 點擊「Upload」上傳你的圖片
   → 儲存
```

### 🖼️ Part B：loading.gif（載入動畫）

#### 規格要求

```
┌──────────────────────────────────────────────┐
│  loading.gif 規格：                           │
│                                              │
│  ✅ 格式：GIF（動態最佳）或 PNG（靜態）       │
│  ✅ 建議尺寸：240 × 240 px                   │
│  ✅ 放在 wwwroot 根目錄                       │
│  ✅ 檔名必須是 loading.gif                    │
│                                              │
│  用途：                                       │
│  • LINE 在載入你的 Mini App 網頁時顯示        │
│  • 讓教友不會看到白屏                        │
└──────────────────────────────────────────────┘
```

#### 製作方式

```
方案 A：用免費線上工具產生（最快）
  1. 前往 https://loading.io/
  2. 選擇一個喜歡的 Loading 動畫樣式
  3. 修改顏色為 #4864b8
  4. 設定尺寸 240×240
  5. 下載 GIF
  6. 命名為 loading.gif

方案 B：用 Canva 做簡單的靜態 loading（也可以）
  1. 建立 240×240 畫布
  2. 放上教會 LOGO（小一點）
  3. 下方加「載入中...」文字
  4. 下載 PNG
  5. 改名為 loading.gif（PNG 副檔名改成 gif 也能用）

方案 C：先用預設圖片（趕時間的話）
  如果暫時沒有，可以先放一張教會 LOGO 的靜態圖
  之後再換成動畫版本
```

#### 放到正確位置

```
把做好的 loading.gif 放到：

  ChurchReport
    └── wwwroot
         └── loading.gif    ← 放在這裡！

⚠️ 注意：
  • 檔名必須是 loading.gif（全小寫）
  • 必須在 wwwroot 根目錄（不是子資料夾）
  • LINE 會自動去 {你的Endpoint URL 的根路徑}/loading.gif 找這個檔案
```

### ✅ 完成標誌

```
確認以下全部完成：
  □ Channel Icon 已上傳到 LINE Console（500×500 PNG）
  □ loading.gif 已放到 ChurchReport\wwwroot\loading.gif
  □ 在瀏覽器輸入 https://jesus.speechmessage.com.tw:807/loading.gif
    → 能看到載入動畫圖片
```

---

## 步驟六：測試 → 送審 → 上線

### 💡 這一步在做什麼？

> 先部署到伺服器 → 用手機 LINE 測試 → 確認沒問題 → 送 LINE 審核 → 通過後上線

### 🚀 Part A：部署到伺服器

```
🔵 Step 1：確認所有程式碼修改已存檔
   ↓
   在 Visual Studio 中 Build 一下，確認沒有錯誤

🔵 Step 2：部署到正式伺服器
   ↓
   用你平常的部署方式，把程式碼更新到
   jesus.speechmessage.com.tw:807 伺服器上
   
   （這通常是 Publish 到 IIS 或用你的 CI/CD 流程）

🔵 Step 3：確認隱私政策頁面可以存取
   ↓
   在瀏覽器打開：
   https://jesus.speechmessage.com.tw:807/Privacy
   ↓
   如果能看到「好牧人 隱私政策」頁面 → ✅ OK
   如果看到錯誤或空白 → 需要檢查部署
```

### 🧪 Part B：手機測試（超重要！）

#### 測試前準備

```
準備一支手機：
  ✅ 已安裝最新版 LINE App
  ✅ 你的 LINE 帳號已被加為 Tester（步驟四做過了）
  ✅ 手機可以連上網路
```

#### 如何打開 Developing 環境的 Mini App

```
方法 1：用 LIFF URL 打開（最快）
  在手機 LINE 的任何聊天室中，貼上以下網址並點擊：
  
  https://liff.line.me/{你的Developing_LIFF_ID}
  
  例如：
  https://liff.line.me/2012345678-AbCdEfGh

方法 2：用 LINE 的 QR Code 掃描器
  在電腦上把上面的 URL 產生 QR Code
  用手機 LINE 掃描

方法 3：自己傳訊息給自己
  在 LINE 的「記事本」或任何聊天室，
  傳送上面的 URL 連結，然後自己點擊
```

#### 測試清單

```
📋 基本功能測試（必做）

  □ 1. 打開 Mini App
        → 預期：看到好牧人的 LINE 登入頁面（LineIdLoginView）
        → 不應該出現白屏或錯誤訊息
  
  □ 2. 自動登入
        → 預期：LIFF SDK 初始化成功
        → 預期：自動取得你的 LINE Profile（顯示你的名字）
  
  □ 3. 已綁定用戶流程
        → 預期：自動導向 IntegrateView 或 MultiGroupView
        → 預期：能看到小組管理頁面
  
  □ 4. 未綁定用戶流程
        → 預期：導向 LineLiffView 綁定頁面
        → 預期：可以輸入姓名/電話完成綁定
  
  □ 5. 隱私政策頁面
        → 在手機瀏覽器打開：
           https://jesus.speechmessage.com.tw:807/Privacy
        → 預期：頁面正常顯示


📋 奉獻流程測試（如果有時間）

  □ 6. 奉獻登入
        → 打開奉獻入口
        → 預期：LIFF 登入成功
  
  □ 7. 奉獻操作
        → 預期：可以選擇奉獻類別和金額
  
  □ 8. 奉獻紀錄查詢
        → 預期：可以查到奉獻紀錄


📋 UI 測試

  □ 9. 內容沒有被遮住
        → 頂部內容沒有被 LINE Header 遮住
        → 底部內容沒有被 iOS Home Indicator 遮住
  
  □ 10. DataGrid 操作
        → 在手機上可以左右滑動表格
        → 可以點擊編輯
  
  □ 11. Hero 圖片
        → 圖片輪播正常顯示


📋 多平台測試（建議）

  □ 12. Android 手機的 LINE App
  □ 13. iPhone 的 LINE App
  □ 14. 電腦版 LINE（應該自動走 OAuth 登入）
```

#### 遇到問題怎麼辦？

```
問題 1：打開後白屏
  → 檢查 LIFF ID 是否正確
  → 檢查 Endpoint URL 是否正確
  → 在電腦瀏覽器直接打開 Endpoint URL，看是否能載入頁面

問題 2：LIFF 初始化失敗，顯示 INVALID_ARGUMENT
  → LIFF ID 填錯了！確認是 Developing 環境的 LIFF ID
  → 確認 URL 中的 LIFF ID 和 Console 設定一致

問題 3：說「你不是 Tester」或拒絕存取
  → 你沒有被加為 Tester
  → 回 Console → Roles → Add Tester → 加入你自己

問題 4：登入後教友資料對不上（userId 不同）
  → Mini App Channel 建在了不同的 Provider！
  → 這是最嚴重的問題，需要刪掉重建在正確的 Provider 下

問題 5：頁面載入很慢
  → 圖片太大，壓縮一下
  → 伺服器效能問題
  → 確認 SSL 憑證沒過期

問題 6：能打開但功能不正常（CRM 連不上等）
  → 這跟 Mini App 無關，是伺服器端的問題
  → 和你平常除錯方式一樣
```

### 📤 Part C：送審

> ⚠️ 確認所有測試都通過後，才進行送審！

```
🔵 Step 1：設定 Review 環境
   ↓
   回到 LINE Developers Console
   → 好牧人 Mini App Channel
   → Web app settings
   → 確認 Review 環境的 Endpoint URL 已正確設定
   （步驟四已經做過了，再確認一下）

🔵 Step 2：確認 Channel 資訊完整
   ↓
   → Basic settings 分頁
   → 確認以下都有填：
     □ Channel icon ✅
     □ Channel name = "好牧人" ✅
     □ Channel description ✅
     □ Email address ✅
     □ Privacy policy URL ✅

🔵 Step 3：點擊「Submit for review」
   ↓
   在 Channel 頁面找到「Submit for review」按鈕
   （通常在 Overview 或頁面頂部）

🔵 Step 4：填寫審核說明
```

#### 審核說明填寫範本（複製貼上）

```
【服務說明】
好牧人是一個教會會友管理系統，提供以下功能：
1. LINE 帳號綁定與自動登入
2. 小組牧養回報（出席、探訪、代禱事項）
3. 線上奉獻（支援永豐QPay/高鉅MyPay/台新TSPG）
4. 奉獻紀錄查詢
5. 個人資料維護

教友可以透過 LINE 登入，直接在 LINE 中管理小組事務和進行線上奉獻。

【測試步驟】
1. 開啟 Mini App 後，系統自動進行 LINE 登入
2. 若為已綁定用戶，自動導向小組管理頁面
3. 若為新用戶，導向綁定註冊頁面
4. 可在 DataGrid 中查看和編輯小組成員資料
5. 點擊奉獻功能，可進入線上奉獻流程

【注意事項】
- 部分功能需要與 Dynamics 365 CRM 連線
- 測試時可能需要使用已綁定的教友帳號
```

```
🔵 Step 5：提交
   ↓
   確認所有資訊無誤後，點擊提交

🔵 Step 6：等待審核
   ↓
   通常需要數個工作天
   LINE 會透過 Email 通知審核結果
```

#### 審核不通過怎麼辦？

```
不要緊張！退件是很正常的。

常見退件原因與對策：
┌─────────────────────────────────┬────────────────────────────────┐
│ 退件原因                        │ 怎麼修                          │
├─────────────────────────────────┼────────────────────────────────┤
│ 隱私政策頁面打不開               │ 確認伺服器正常運作               │
│                                 │ 確認 HTTPS 沒有錯誤              │
│                                 │ 用手機測試可以打開               │
├─────────────────────────────────┼────────────────────────────────┤
│ 載入速度太慢                     │ 壓縮圖片檔案大小                │
│                                 │ 確認伺服器回應時間正常           │
├─────────────────────────────────┼────────────────────────────────┤
│ 頁面出現白屏或 JavaScript 錯誤   │ 用 F12 開發者工具檢查錯誤        │
│                                 │ 確認 LIFF ID 正確                │
├─────────────────────────────────┼────────────────────────────────┤
│ Channel name 包含 "LINE"        │ 把名稱改成不含 LINE 的           │
├─────────────────────────────────┼────────────────────────────────┤
│ 功能無法使用                     │ 確認 CRM 連線正常                │
│                                 │ 確認測試資料存在                 │
└─────────────────────────────────┴────────────────────────────────┘

修改後 → 重新部署到 Review 環境 → 再次提交審核
```

### 🎉 Part D：審核通過 → 正式上線！

```
🔵 Step 1：收到審核通過通知
   ↓
   LINE 會發 Email 通知你

🔵 Step 2：修改 appsettings.json 的 ActiveEnvironment
   ↓
   打開 appsettings.json
   找到 MiniApp 區段
   修改：
   
   "ActiveEnvironment": "Published"    ← 改成 Published

🔵 Step 3：重新部署到伺服器
   ↓
   把更新後的程式碼部署到
   jesus.speechmessage.com.tw:807

🔵 Step 4：最終確認測試
   ↓
   用手機 LINE 打開 Mini App
   確認所有功能正常

🔵 Step 5（選用）：設定 Custom Path
   ↓
   回到 LINE Developers Console
   → Web app settings
   → Published 環境
   → 找到「Custom Path」欄位
   → 輸入你想要的路徑，例如：good-shepherd
   → 儲存後，教友就可以用這個短網址：
     https://miniapp.line.me/good-shepherd

🔵 Step 6：通知教友 🎉
   ↓
   把 Mini App 的連結分享給教友：
   
   📢 好消息！好牧人已升級為 LINE Mini App！
   現在可以在 LINE 中直接搜尋「好牧人」使用，
   或點擊以下連結：
   https://miniapp.line.me/{Published_LIFF_ID}
```

---

## 📋 整個流程的時間線

```
Week 1：
  ☑ 提交 LINE Taiwan 申請（步驟一）
  ☑ 準備 Channel Icon 和 loading.gif（步驟五）

Week 2-4：
  ⏳ 等待 LINE Taiwan 審核通過
  ☑ 程式碼已全部完成（之前已做好）
  ☑ 先用現有 LIFF 環境繼續日常運作

Week 4-5（收到許可後）：
  ☑ 在 Console 建立 Mini App Channel（步驟二）
  ☑ 填入 LIFF ID（步驟三）
  ☑ 設定 Endpoint URL（步驟四）
  ☑ 部署到伺服器
  ☑ 手機測試

Week 5-6：
  ☑ 提交審核（步驟六 Part C）
  ⏳ 等待 LINE 審核

Week 6-7：
  ☑ 審核通過 → 正式上線！（步驟六 Part D）
  ☑ 通知教友使用

📅 總計：約 5-7 週（主要時間花在等 LINE 審核）
```

---

## 🔧 附錄：如果程式碼需要微調

### 你可能需要用到的 Helper 方法

如果未來需要在 Controller 中根據環境自動選擇 LIFF ID，  
可以在 `AuthenticationController` 中加入這個方法（目前不需要，因為 URL 參數已經帶了 LIFF ID）：

```csharp
/// <summary>
/// 取得當前 Mini App 環境的 LIFF ID
/// </summary>
private string GetActiveMiniAppLiffId()
{
    var activeEnv = Configuration["MiniApp:ActiveEnvironment"] ?? "Developing";
    return activeEnv switch
    {
        "Developing" => Configuration["MiniApp:DevelopingLiffId"] ?? "",
        "Review"     => Configuration["MiniApp:ReviewLiffId"] ?? "",
        "Published"  => Configuration["MiniApp:PublishedLiffId"] ?? "",
        _            => Configuration["MiniApp:DevelopingLiffId"] ?? ""
    };
}
```

### 在 Controller 或 View 中偵測是否來自 Mini App

```csharp
// Controller 中
var isMiniApp = (bool)(HttpContext.Items["IsLineMiniApp"] ?? false);
if (isMiniApp)
{
    // 來自 LINE LIFF Browser 的請求
}
```

```html
<!-- Razor View 中 -->
@{
    var isMiniApp = (bool)(Context.Items["IsLineMiniApp"] ?? false);
}
@if (isMiniApp)
{
    <div class="mini-app-notice">你正在 LINE Mini App 中瀏覽</div>
}
```

---

*文件版本：v1.0*  
*建立日期：2025 年 7 月*  
*專案：ChurchReport 好牧人教會管理系統*  
*分支：Jesus_5.0.4.LineMiniApp*
