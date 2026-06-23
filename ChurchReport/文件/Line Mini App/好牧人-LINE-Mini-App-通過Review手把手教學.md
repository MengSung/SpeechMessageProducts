# 🔍 行道會聖谷教會 LINE Mini App — 通過 Review 手把手教學
### 你已在 Developing 成功開啟，現在目標是讓 Review 通過 LINE 審核

> **前提**：你已能在 Developing 環境正常開啟 Mini App ✅  
> **本文件目的**：一步一步帶你從「Review 有問題」走到「審核通過」  
> **你的系統資訊**：
> - 站台網址：`https://jesus.speechmessage.com.tw:807`
> - Developing LIFF ID：`2009427707-Fi5L5blD`
> - Review LIFF ID：`2009427708-GToVLqgV`
> - Published LIFF ID：`2009427709-PTH3dfeP`

---

## ⚠️ 台灣地區重要前置條件：Provider 必須先通過認證

```
LINE 官方規定：
"If the region to provide the service is Thailand or Taiwan,
 only certified providers can apply for the verification review."

翻譯：台灣地區只有「已認證的 Provider」才能送審 Mini App！

如果你的 Provider 尚未認證：
  → Review request 頁面只會顯示說明文字
  → 不會出現「Submit for review」按鈕
  → 必須先完成 Provider 認證，按鈕才會出現
```

### 第零步：先完成 Provider 認證

```
1. 登入 LINE Developers Console
   https://developers.line.biz/console/

2. 左側選單點 Provider 名稱「行道會聖谷教會」
   （注意：是 Provider 層級，不是進入 Channel）

3. 在 Provider 頁面找「Certification」或「Apply for certification」

4. 填寫認證資料：
   • 組織/公司名稱：行道會聖谷教會教會（或音訊科技股份有限公司）
   • 統一編號（Business ID）：13054485
   • 服務地區：Taiwan
   • 聯絡 Email：mengsunghu@gmail.com
   • 可能需要上傳教會或公司的登記證明文件

5. 提交申請 → 等待 LINE 審核 Provider 認證
   （審核時間：通常數個工作天）

6. 收到認證通過通知後，再回到 Mini App Channel
   → Review request 分頁的「Submit for review」按鈕才會出現
```

> **沒有 Provider 認證，所有後續步驟都無法進行。請先處理這一步！**

---

## 📋 本文件快速導覽

| 階段 | 步驟 | 說明 |
|:----:|------|------|
| **第一階段** | [步驟 1](#步驟-1確認-review-endpoint-url-設定正確) | 確認 Review Endpoint URL 設定正確 |
| **第一階段** | [步驟 2](#步驟-2確認-review-環境可以正常開啟) | 確認 Review 環境可以正常開啟 |
| **第一階段** | [步驟 3](#步驟-3確認-basic-settings-資料完整) | 確認 Basic settings 資料完整 |
| **第一階段** | [步驟 4](#步驟-4確認-privacy-policy-頁面可公開存取) | 確認 Privacy Policy 頁面可公開存取 |
| **第一階段** | [步驟 5](#步驟-5確認-loading-gif-存在) | 確認 loading.gif 存在 |
| **第一階段** | [步驟 6](#步驟-6確認-channel-icon-已上傳) | 確認 Channel Icon 已上傳 |
| **第二階段** | [步驟 7](#步驟-7用-review-liff-url-做完整測試) | 用 Review LIFF URL 做完整功能測試 |
| **第三階段** | [步驟 8](#步驟-8填寫送審說明並提交) | 填寫送審說明並提交 |
| **第四階段** | [步驟 9](#步驟-9等待審核與退件處理) | 等待審核與退件處理 |
| **第五階段** | [步驟 10](#步驟-10審核通過後切換-published-正式上線) | 審核通過後切換 Published 正式上線 |
| **附錄** | [附錄 A](#附錄-a-review-常見問題清單與對策) | Review 常見問題清單與對策 |
| **附錄** | [附錄 B](#附錄-b-快速確認清單-checklist) | 快速確認清單 Checklist |

---

## 第一階段：讓 Review 環境可以正常運作

---

## 步驟 1：確認 Review Endpoint URL 設定正確

### 為什麼這是第一個要確認的？

```
Developing 正常 ≠ Review 正常

原因：Developing 和 Review 是兩個獨立的 LIFF ID。
  • Developing LIFF ID：2009427707-Fi5L5blD  ← 你已測試通過
  • Review LIFF ID：    2009427708-GToVLqgV  ← 這個可能 Endpoint URL 沒設對
  
如果 Review 的 Endpoint URL 還是 LINE 預設的 liff-default-review.html，
打開就會是 LINE 的空白頁，不是你的行道會聖谷教會系統。
```

### 操作步驟

```
1. 打開瀏覽器，登入 LINE Developers Console
   https://developers.line.biz/console/

2. 選擇你的 Provider → 進入行道會聖谷教會 Mini App Channel

3. 點擊上方的「Web app settings」分頁

4. 找到「Review」環境那一欄

5. 確認 Endpoint URL 是否還是：
   liff-default-review.html  ← ❌ 這是預設值，必須改掉！

6. 把 Review 的 Endpoint URL 改成：
   https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
                                                                              ↑
                                                              這是 Review 的 LIFF ID，不是 Developing 的！

7. 點「Save」儲存
```

### ⚠️ 常見錯誤：貼錯 LIFF ID

```
❌ 錯誤示範（把 Developing 的 LIFF ID 貼到 Review）：
   Review Endpoint URL:
   https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427707-Fi5L5blD
                                                                                    ↑
                                                                         這是 Developing 的！貼錯了！

✅ 正確示範：
   Developing Endpoint URL:
   https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427707-Fi5L5blD
                                                                                    ↑ Dev 的

   Review Endpoint URL:
   https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
                                                                                    ↑ Review 的

   Published Endpoint URL:
   https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP
                                                                                    ↑ Published 的
```

### ✅ 確認完成標準

```
Review 的 Endpoint URL 欄位顯示：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
→ 儲存成功後進下一步
```

---

## 步驟 2：確認 Review 環境可以正常開啟

### 你需要先確認自己在 Tester 名單中

```
⚠️ Developing 和 Review 環境只有「Tester」才能開啟！
   如果沒有加入 Tester，LINE 會直接拒絕存取。

確認方式：
  Console → 行道會聖谷教會 Mini App Channel → 「Roles」分頁
  → 看你自己的 LINE 帳號是否在 Tester 清單裡

  如果不在 → 點「Add Tester」→ 加入自己
  加入後等 1-2 分鐘再測試
```

### 用手機 LINE 開啟 Review 環境

```
在手機 LINE 的任何聊天室（或記事本），貼上以下連結並點擊：

https://liff.line.me/2009427708-GToVLqgV
↑ 這是 Review 環境的 LIFF ID
```

### 預期看到的結果

```
✅ 正常：看到行道會聖谷教會 LINE 登入頁面（和 Developing 一樣的頁面）

❌ 異常情況與對策：

  情況 A：看到 LINE 的空白頁或 "liff-default" 頁面
   → 步驟 1 沒做好，回去重新確認 Review 的 Endpoint URL

  情況 B：看到「你沒有存取權限」或「Access Denied」
   → 你不在 Tester 名單中，先去 Console → Roles → Add Tester

  情況 C：看到錯誤 "INVALID_ARGUMENT"
   → LIFF 初始化失敗，檢查：
      □ Endpoint URL 末尾的 LIFF ID 是 2009427708-GToVLqgV 嗎？
      □ 這個 LIFF ID 和 console.log 顯示的一樣嗎？

  情況 D：看到白屏（空白頁，沒有任何內容）
   → 伺服器問題或 JavaScript 錯誤
   → 用手機 LINE 打開後，到 LINE 設定 → LINE Labs → 打開 Developer Mode
   → 可以看到 console 錯誤

  情況 E：頁面載入但功能異常（例如無法登入、CRM 連不上）
   → 這不是 Mini App 的問題，是後端問題
   → 確認伺服器正常運作，確認 CRM 連線正常
```

### ✅ 確認完成標準

```
用手機 LINE 打開 https://liff.line.me/2009427708-GToVLqgV
→ 看到行道會聖谷教會 LINE 登入頁面
→ LIFF 初始化成功（不報錯）
→ 已綁定教友可以自動登入並導向功能頁面
→ 未綁定教友可以導向綁定頁面
```

---

## 步驟 3：確認 Basic settings 資料完整

### 為什麼重要？

```
LINE 審核員在審核時，首先會檢查 Channel 的基本資料是否完整。
如果有任何欄位是空的或不符合規範，審核就會直接退件。
```

### 操作步驟

```
1. Console → 行道會聖谷教會 Mini App Channel → 「Basic settings」分頁

2. 逐一確認以下欄位：
```

| 欄位 | 必填 | 應填內容 | 確認 |
|------|:----:|---------|:----:|
| **Channel icon** | ✅ | 行道會聖谷教會 LOGO（500×500 PNG，不透明底色）| □ |
| **Channel name** | ✅ | `行道會聖谷教會`（⚠️ 絕對不能含 "LINE"）| □ |
| **Channel description** | ✅ | `行道會聖谷教會教會管理系統，提供小組牧養回報、線上奉獻、會友管理等服務` | □ |
| **Email address** | ✅ | `mengsunghu@gmail.com` | □ |
| **Privacy policy URL** | ✅ | `https://jesus.speechmessage.com.tw:807/Privacy` | □ |
| **Terms of use URL** | 建議填 | 有的話填，沒有先留空 | □ |
| **Service company's country/region** | ✅ | `Taiwan` | □ |

### ⚠️ Channel name 規則（很重要！）

```
❌ 以下名稱一律會被退件：
  「行道會聖谷教會 LINE」
  「LINE 行道會聖谷教會」
  「行道會聖谷教會Line系統」（大小寫都不行）
  「LINE MINI 行道會聖谷教會」

✅ 以下名稱都可以：
  「行道會聖谷教會」
  「行道會聖谷教會教會系統」
  「行道會聖谷教會ChurchReport」
```

### ✅ 確認完成標準

```
以上表格中的所有 ✅ 必填欄位都有填寫
Channel name 不含 "LINE" 字樣
```

---

## 步驟 4：確認 Privacy Policy 頁面可公開存取

### 為什麼重要？

```
LINE 審核員會在瀏覽器直接輸入你填的 Privacy Policy URL，
確認頁面是否可以存取且內容符合要求。
這是退件最常見的原因之一！
```

### 測試方式

```
方法 1：用手機的瀏覽器（不是 LINE，是 Safari 或 Chrome）打開：
  https://jesus.speechmessage.com.tw:807/Privacy

方法 2：用另一台電腦（或無痕視窗）打開同一個網址

✅ 正常：看到「行道會聖谷教會 隱私政策」頁面，內容完整
❌ 異常：空白頁、錯誤、需要登入才能看 → 必須修復！
```

### 隱私政策頁面的內容要求

```
LINE 審核員會確認隱私政策頁面包含以下內容（不需要完全照抄，有提到就好）：

  ✅ 服務名稱（行道會聖谷教會教會管理系統）
  ✅ 服務提供者/營運組織（教會名稱或統一編號 13054485）
  ✅ 蒐集哪些個人資料（LINE 顯示名稱、LINE User ID、電話等）
  ✅ 為何蒐集資料（會友管理、小組回報、奉獻服務）
  ✅ 資料保護方式（加密傳輸、限制存取）
  ✅ 聯絡方式（Email 或電話）
```

### 如果頁面有問題，如何修復

程式碼中已建好這個頁面，請確認以下三點：

```
確認 1：Views/Authentication/Privacy.cshtml 存在且有內容

確認 2：Controller 有對應的 Action，且加了 [AllowAnonymous]
        → 因為頁面必須在未登入狀態也能存取！
        → 找到 AuthenticationController.Core.cs 中的 Privacy Action
        → 確認有 [AllowAnonymous] 標籤

確認 3：Startup.cs 有對應的路由
        → 搜尋 "privacy"（不分大小寫）確認路由有註冊

確認 4：伺服器已部署最新版本
        → 如果上面三點都 OK，但頁面還是打不開
        → 重新部署到伺服器
```

### ✅ 確認完成標準

```
在手機的 Safari 或 Chrome（不是 LINE App）打開：
https://jesus.speechmessage.com.tw:807/Privacy
→ 可以看到完整的隱私政策內容（不需要登入）
```

---

## 步驟 5：確認 loading.gif 存在

### 為什麼重要？

```
LINE 審核員開啟 Mini App 時，第一個畫面是 loading 動畫。
如果找不到 loading.gif，會顯示 LINE 的預設載入畫面（看起來不專業）。
雖然不會因此退件，但強烈建議要有。
```

### 確認方式

```
在瀏覽器打開：
https://jesus.speechmessage.com.tw:807/loading.gif

✅ 正常：看到一個圖片或動畫
❌ 異常：404 Not Found → 需要放置 loading.gif
```

### 如何放置 loading.gif

```
位置：ChurchReport\wwwroot\loading.gif

製作方式（三選一）：
  A. 前往 https://loading.io/
     → 選擇旋轉動畫 → 顏色改成 #4864b8 → 尺寸 240×240 → 下載 GIF
     → 改名為 loading.gif → 複製到 wwwroot 資料夾

  B. 用教會 LOGO 製作靜態版本：
     → 把 ChurchLogo.png 縮小到 240×240 px
     → 改名為 loading.gif（PNG 內容也可以，副檔名改 gif 即可）

  C. 暫時用任何 240×240 的圖片頂替：
     → 只要不是 404 就行，之後再換精緻版本
```

### ✅ 確認完成標準

```
https://jesus.speechmessage.com.tw:807/loading.gif
→ 顯示圖片（不是 404）
```

---

## 步驟 6：確認 Channel Icon 已上傳

### 確認方式

```
Console → Basic settings → 找到「Channel icon」欄位
→ 確認有圖片（不是空白或預設圖示）
```

### 如果還沒上傳

```
規格要求：
  • 格式：PNG 或 JPG
  • 尺寸：建議 500×500 px，最小 100×100 px
  • 必須有底色（不能透明背景）
  • 不能包含 "LINE" 字樣

快速製作方式：
  1. 找到專案中的教會 LOGO：
     ChurchReport\wwwroot\assets\images\ChurchLogo.png

  2. 用 Windows 小畫家快速製作：
     → 「調整大小」→ 500×500 像素（取消維持比例）
     → 存成 PNG

  3. 上傳到 Console：
     Console → Basic settings → Channel icon → Upload → 選擇圖片 → Save
```

### ✅ 確認完成標準

```
Console → Basic settings → Channel icon 欄位有顯示圖片
```

---

## 第二階段：完整功能測試

---

## 步驟 7：用 Review LIFF URL 做完整功能測試

### 為什麼需要用 Review URL 測試？

```
LINE 審核員測試的是 Review 環境！
你必須確認 Review 環境的所有功能都能正常使用。
Developing 沒問題 ≠ Review 沒問題（因為用的是不同的 LIFF ID）。
```

### Review 環境的 LIFF URL

```
https://liff.line.me/2009427708-GToVLqgV
```

### 完整測試清單（逐項打勾）

#### 📋 A. 載入與初始化

```
  □ A1. 打開 https://liff.line.me/2009427708-GToVLqgV
        → 看到 loading 動畫（不是白屏）
        → 載入時間在 3 秒內

  □ A2. 頁面完整載入
        → 看到行道會聖谷教會 LINE 登入頁
        → Hero 圖片輪播正常
        → 聯絡資訊卡片正常顯示

  □ A3. LINE Mini App Header 出現
        → 頂部有 LINE 的返回按鈕和 App 名稱「行道會聖谷教會」
        → 這是 LINE 自動加上的，確認沒有被遮住

  □ A4. 內容沒有被遮住
        → 頂部文字/按鈕沒有被 LINE Header 擋住
        → 底部文字沒有被 iPhone Home Indicator 切掉
```

#### 📋 B. 登入流程

```
  □ B1. LIFF 初始化成功（不報錯）
        → F12 開發者工具 Console 沒有紅色錯誤
        → 不顯示 "INVALID_ARGUMENT" 或 "FORBIDDEN" 錯誤

  □ B2. 自動取得 LINE Profile
        → 顯示「正在準備 LINE 登入...」
        → 自動取得你的 LINE 名字

  □ B3. 已綁定教友登入
        → 自動導向 IntegrateView 或 MultiGroupView
        → 看到小組管理頁面，有資料

  □ B4. 未綁定教友登入
        → 導向 LineLiffView 綁定頁面
        → 可以輸入姓名和電話
        → 點送出後，綁定成功
```

#### 📋 C. 核心功能

```
  □ C1. 小組管理 DataGrid
        → 有資料顯示
        → 可以滑動、點擊
        → 手機上操作不卡頓

  □ C2. 奉獻流程（如果要審核這個功能的話）
        → 進入奉獻頁面正常
        → 奉獻表單可以填寫

  □ C3. 個人資料（如有）
        → 可以查看和編輯
```

#### 📋 D. 多平台確認

```
  □ D1. iOS 手機的 LINE App → 功能正常
  □ D2. Android 手機的 LINE App → 功能正常（如有兩支手機）
```

### 遇到問題的排查流程

```
問題 1：Review 環境打開是空白頁或 liff-default 頁面
  → 回步驟 1，重新確認並儲存 Review 的 Endpoint URL

問題 2：LIFF 報錯 INVALID_ARGUMENT
  → Endpoint URL 末尾的 LIFF ID 貼錯了
  → 應該是 2009427708-GToVLqgV（Review 的），不是 Developing 的

問題 3：功能在 Developing 正常，但在 Review 異常
  → 大部分情況是「LIFF ID 不同」造成的
  → liff.init() 使用的 liffId 是從 URL 參數動態帶入的
  → 確認 Endpoint URL 的末尾帶了正確的 Review LIFF ID

問題 4：頁面載入但資料異常（CRM 抓不到資料等）
  → 這和 Mini App 環境無關，是後端/CRM 的問題
  → 確認伺服器和 CRM 連線正常
```

### ✅ 確認完成標準

```
□ A1 到 B4 的基本流程全部 ✅
□ C1 核心功能（小組管理）可以正常操作
□ 沒有 JavaScript 錯誤（F12 Console 無紅色錯誤）
→ 達成以上條件才能進入下一步送審
```

---

## 第三階段：提交審核

---

## 步驟 8：填寫送審說明並提交

### 送審前的最後確認

```
提交前，再快速確認以下 7 件事（每個都打勾才能送）：

  □ 1. Review Endpoint URL：
        https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
        
  □ 2. Channel icon 已上傳（有圖片）

  □ 3. Channel name = 「行道會聖谷教會」（不含 LINE）

  □ 4. Channel description 已填寫

  □ 5. Privacy Policy URL 已填寫且可存取：
        https://jesus.speechmessage.com.tw:807/Privacy

  □ 6. Review 環境功能測試通過（步驟 7 完成）

  □ 7. loading.gif 可存取：
        https://jesus.speechmessage.com.tw:807/loading.gif
```

### 找到「Submit for review」按鈕

```
⚠️ 不在 Basic settings！不在 Overview！

正確位置：上方分頁列 → 「Review request」分頁

步驟：
1. 登入 LINE Developers Console
   https://developers.line.biz/console/

2. 選擇你的 Provider → 進入行道會聖谷教會 Mini App Channel

3. 點擊上方分頁列的「Review request」
   （分頁順序：Basic settings → Web app settings → Review request ← 這個！）

4. 在 Review request 頁面內填寫說明，找到「Submit for review」按鈕

5. 點擊「Submit for review」
```

### 填寫審核說明（直接複製以下內容貼上）

```
【服務名稱】
行道會聖谷教會教會管理系統

【服務說明】
行道會聖谷教會是一個教會會友管理系統，服務對象為台灣教會會友。

主要功能：
1. LINE 帳號綁定與自動登入（會友使用 LINE 帳號直接登入，無需額外帳號密碼）
2. 小組牧養回報（小組長可回報每週聚會出席人數、探訪紀錄、代禱事項）
3. 線上奉獻（整合永豐銀行 QPay、高鉅科技 MyPay、台新銀行 TSPG）
4. 奉獻紀錄查詢（會友可查詢自己的奉獻明細與歷史紀錄）
5. 個人資料維護（會友可更新聯絡資訊）

技術架構：
- 後端：ASP.NET Core (.NET 10)
- 前端：DevExtreme + jQuery + LIFF SDK 2.x
- 伺服器：Windows Server，HTTPS（SSL 憑證正常）

【測試步驟】
1. 在 LINE App 中開啟 Mini App
2. 系統自動進行 LINE 登入，取得用戶的 LINE Profile
3. 若為已綁定會友 → 自動導向小組管理頁面，可查看小組資料
4. 若為新用戶 → 導向綁定頁面，輸入姓名與電話後完成綁定
5. 已綁定會友可操作小組 DataGrid（查看、編輯成員資料）
6. 點擊奉獻功能可進入線上奉獻流程

【注意事項】
- 測試第 3 步的「已綁定會友」功能，需要使用已與系統綁定的 LINE 帳號
- 系統連接 Microsoft Dynamics 365 CRM 作為資料庫
- 如需測試帳號，請告知，我們可以提供測試用帳號

【隱私政策】
https://jesus.speechmessage.com.tw:807/Privacy
```

### 提交

```
5. 確認所有欄位填好後，點擊「Submit」或「Confirm」

6. 出現確認對話框 → 點「OK」或「Confirm」

7. ✅ 審核申請已送出！
   → 你會收到一封確認 Email
   → 審核通常需要數個工作天（無固定時間）
```

---

## 第四階段：等待審核與退件處理

---

## 步驟 9：等待審核與退件處理

### 等待期間

```
審核通常需要幾個工作天（官方未公告確切時間）。

等待期間不需要做任何事，繼續日常使用現有的 Developing 環境即可。

LINE 會透過以下方式通知結果：
  • 發送通知 Email 到你在 Console 填的 Email（mengsunghu@gmail.com）
  • Console 上的 Mini App Channel 狀態也會更新
```

### 如果審核通過 ✅

```
恭喜！跳到步驟 10 進行上線作業。
```

### 如果審核退件 ❌（不要緊張，很正常）

```
LINE 會在通知 Email 中告知退件原因。

以下是針對行道會聖谷教會系統最常見的退件原因與對策：
```

#### 退件原因 1：隱私政策頁面無法存取或內容不符

```
原因：https://jesus.speechmessage.com.tw:807/Privacy 打不開，
      或頁面需要登入才能看，
      或頁面沒有說明蒐集哪些個人資料

對策：
  1. 用手機 Safari（不是 LINE App）打開上面的網址
  2. 確認可以在未登入狀態看到完整頁面
  3. 如果打不開：
     a. 確認 Controller 的 Privacy Action 有 [AllowAnonymous]
     b. 確認 Startup.cs 有對應路由
     c. 重新部署伺服器

  修復後：重新提交審核
```

#### 退件原因 2：頁面載入太慢

```
原因：首頁載入時間超過 3-5 秒

對策：
  1. 壓縮 Hero 圖片的大小：
     • church-001.jpg, church-002.jpg 如果超過 500KB，壓縮到 200KB 以下
     • 工具：https://squoosh.app/ （免費線上壓縮）
  
  2. 確認 loading.gif 存在（讓用戶在等待時看到動畫，不會覺得卡住）

  3. 確認伺服器 SSL 憑證有效，沒有 SSL 警告

  修復後：重新部署 → 重新提交
```

#### 退件原因 3：頁面有白屏或 JavaScript 錯誤

```
原因：用 Review LIFF URL 開啟時，頁面空白或報錯

對策：
  1. 在手機 LINE App 開啟後，啟用 LINE 的開發者模式看 Console：
     iOS：LINE App → 設定 → LINE Labs → 啟用 Developer Mode
     → 然後可以在頁面底部看到 Console 輸出

  2. 確認 Review 的 Endpoint URL 正確（步驟 1）

  3. 確認 Review LIFF ID 是 2009427708-GToVLqgV

  修復後：重新測試 → 確認沒問題 → 重新提交
```

#### 退件原因 4：Channel name 包含「LINE」

```
對策：
  Console → Basic settings → Channel name → 移除所有 "LINE" 相關字樣
  改成：「行道會聖谷教會」或「行道會聖谷教會教會系統」
  → 儲存後重新提交
```

#### 退件原因 5：核心功能無法正常使用

```
原因：審核員操作時功能不正常（例如登入後沒有資料、DataGrid 空白）

對策：
  1. 確認伺服器和 CRM 連線正常
  2. 確認 Review 環境有測試資料（至少有幾筆小組資料）
  3. 在送審說明中補充：
     「如需已綁定帳號的測試資料，請告知，我們可以提供測試用帳號」

  修復後：確認正常 → 重新提交，並在說明中補充測試帳號
```

#### 退件原因 6：服務不符合 LINE Mini App Policy

```
原因：服務內容有違規，例如：
  • 包含成人內容
  • 仿冒其他品牌
  • 提供違禁品購買

對策：
  行道會聖谷教會是教會管理系統，內容完全合規，不應有這個問題。
  如果真的收到這個退件，仔細閱讀退件說明，了解具體違規點。
  
  Policy 全文：https://terms2.line.me/LINE_MINI_App?lang=en
```

### 重新提交流程

```
修復問題後：

  1. 確認問題已修復（在 Review 環境測試確認）
  2. 重新部署到伺服器（如果有改程式碼）
  3. 回到 Console → Mini App Channel
  4. 再次點擊「Submit for review」
  5. 在送審說明中說明這次修復了什麼問題

⚠️ 每次重新提交都要等審核員重新審，請耐心等待。
```

---

## 第五階段：審核通過，正式上線

---

## 步驟 10：審核通過後切換 Published 正式上線

### 收到審核通過通知後

```
🎉 恭喜！這代表行道會聖谷教會正式成為「Verified LINE Mini App」！
```

### 操作步驟 1：修改 appsettings.json

```
1. 打開 Visual Studio
2. 開啟 ChurchReport\appsettings.json
3. 找到 MiniApp 區段（搜尋 "ActiveEnvironment"）

修改前：
"ActiveEnvironment": "Developing"

修改後：
"ActiveEnvironment": "Published"

4. Ctrl + S 存檔
```

### 操作步驟 2：重新部署到伺服器

```
1. Visual Studio → Build → Build Solution（Ctrl+Shift+B）
   → 確認 Build succeeded

2. 用你平常的方式部署到伺服器
   (IIS Publish 或你的 CI/CD 流程)

3. 重新啟動 IIS 站台或 Application Pool
```

### 操作步驟 3：確認 Published Endpoint URL

```
Published 環境的 Endpoint URL 應該已在步驟 1 一起設定好了，
再確認一次：

Console → Web app settings → Published 環境：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP
                                                                                ↑ Published 的

如果沒設定 → 點 Edit → 貼上上面的 URL → Save
```

### 操作步驟 4：最終確認測試

```
用手機 LINE 打開 Published 環境：
https://liff.line.me/2009427709-PTH3dfeP

確認：
  □ 頁面正常載入
  □ 已綁定教友可以自動登入
  □ 功能正常

⚠️ Published 環境所有 LINE 用戶都可以存取（不只 Tester）
   確認沒問題後，才進行對外公告
```

### 操作步驟 5（選用）：設定 Custom Path

```
如果想讓教友用更好記的網址開啟：

Console → Web app settings → Published 環境 → Custom Path 欄位
→ 輸入：good-shepherd（或其他你喜歡的英文名稱）
→ 儲存

設定後，教友可以用：
https://miniapp.line.me/good-shepherd
開啟行道會聖谷教會 Mini App！
```

### 操作步驟 6：對外公告 🎉

```
在教會 LINE 群組或公告欄分享以下訊息（範本）：

═══════════════════════════════════════════
📢 好消息！行道會聖谷教會已升級為 LINE Mini App！

現在可以直接在 LINE 中使用行道會聖谷教會系統：

🔗 點擊開啟：
https://liff.line.me/2009427709-PTH3dfeP

（或在 LINE 中搜尋「行道會聖谷教會」）

升級亮點：
✅ 在 LINE 中直接搜尋「行道會聖谷教會」就能找到
✅ 可以加到手機主畫面，像 App 一樣使用
✅ 自動 LINE 登入，不用輸入帳號密碼

功能和之前完全一樣，使用更方便！
═══════════════════════════════════════════
```

---

## 附錄 A：Review 常見問題清單與對策

| # | 問題 | 可能原因 | 對策 |
|---|------|---------|------|
| 1 | Review 環境打開是空白頁 | Endpoint URL 還是預設值 | 設定正確的 Review Endpoint URL |
| 2 | INVALID_ARGUMENT 錯誤 | LIFF ID 貼錯（Developing 的貼到 Review）| 確認 URL 末尾是 `2009427708-GToVLqgV` |
| 3 | 「無存取權限」錯誤 | 未加入 Tester 名單 | Console → Roles → Add Tester |
| 4 | 隱私政策打不開 | 路由未設定或需要登入 | 確認 [AllowAnonymous] 和路由 |
| 5 | 頁面白屏 | JS 錯誤或伺服器問題 | F12 查看 Console 錯誤 |
| 6 | 功能正常但審核退件 | 說明不清楚或缺少測試說明 | 補充詳細的測試步驟和測試帳號 |
| 7 | 載入太慢 | 圖片太大 | 壓縮 Hero 圖片到 200KB 以下 |

---

## 附錄 B：快速確認清單 Checklist

```
═══════════════════════════════════════════════════════
 送審前必做確認清單（全部打勾才能送審！）
═══════════════════════════════════════════════════════

【Console 設定】
  □ Review Endpoint URL 已設定：
    https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
  
  □ Channel icon 已上傳（行道會聖谷教會 LOGO 圖片）
  
  □ Channel name = 「行道會聖谷教會」（無 LINE 字樣）
  
  □ Channel description 已填寫
  
  □ Email = mengsunghu@gmail.com
  
  □ Privacy policy URL = https://jesus.speechmessage.com.tw:807/Privacy

【伺服器確認】
  □ https://jesus.speechmessage.com.tw:807/Privacy
    → 未登入可以看到完整頁面 ✅

  □ https://jesus.speechmessage.com.tw:807/loading.gif
    → 顯示圖片，不是 404 ✅

  □ https://liff.line.me/2009427708-GToVLqgV
    → 手機 LINE App 可以正常開啟，功能正常 ✅

【功能測試】
  □ LIFF 初始化成功（不報 INVALID_ARGUMENT）
  □ 已綁定教友可以自動登入並看到小組資料
  □ 未綁定教友可以完成綁定流程
  □ DataGrid 在手機上可以正常操作
  □ F12 Console 無紅色 JavaScript 錯誤

═══════════════════════════════════════════════════════
 以上全部打勾 → 送審！
═══════════════════════════════════════════════════════
```

---

## 整體時程估算

```
今天：
  ✅ 步驟 1：確認 Review Endpoint URL（10 分鐘）
  ✅ 步驟 2：確認 Review 環境可開啟（5 分鐘）
  ✅ 步驟 3：確認 Basic settings 完整（10 分鐘）
  ✅ 步驟 4：確認 Privacy Policy 可存取（5 分鐘）
  ✅ 步驟 5：確認 loading.gif 存在（30 分鐘，含製作）
  ✅ 步驟 6：確認 Channel Icon（30 分鐘，含上傳）
  ✅ 步驟 7：完整功能測試（1-2 小時）
  ✅ 步驟 8：送審（30 分鐘）

等待期（數個工作天）：
  ⏳ 審核中

審核通過後：
  ✅ 步驟 10：切換 Published 上線（30 分鐘）
  🎉 行道會聖谷教會正式成為 Verified LINE Mini App！
```

---

*文件版本：v1.0*  
*建立日期：2025 年 7 月*  
*專案：ChurchReport 行道會聖谷教會教會管理系統*  
*分支：Jesus_5.0.4.LineMiniApp*  
*對應 LIFF IDs：Dev=2009427707-Fi5L5blD / Review=2009427708-GToVLqgV / Published=2009427709-PTH3dfeP*
