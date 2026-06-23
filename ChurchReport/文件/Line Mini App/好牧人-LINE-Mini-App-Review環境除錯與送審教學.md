# 🔍 好牧人 LINE Mini App — Review 環境除錯與送審完整教學
### Developing ✅ 已正常 → Review ❌ 有問題 → 這份文件幫你一步一步修好並送審

> **前提**：你已在 Developing 環境正常開啟好牧人 Mini App ✅  
> **目標**：讓 Review 環境正常運作 → 送審 → 通過審核 → 正式上線  
> **更新日期**：2025 年 7 月

---

## 📌 你的系統快速資訊（方便複製）

| 項目 | 值 |
|------|-----|
| 站台網址 | `https://jesus.speechmessage.com.tw:807` |
| Developing LIFF ID | `2009427707-Fi5L5blD` |
| **Review LIFF ID** | **`2009427708-GToVLqgV`** ← 本文主角 |
| Published LIFF ID | `2009427709-PTH3dfeP` |
| Developing Endpoint URL | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427707-Fi5L5blD` |
| **Review Endpoint URL** | **`https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV`** |
| Published Endpoint URL | `https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP` |
| Privacy Policy URL | `https://jesus.speechmessage.com.tw:807/Privacy` |
| Review 測試用 LIFF 連結 | `https://liff.line.me/2009427708-GToVLqgV` |

---

## 📋 全文快速導覽

| 步驟 | 做什麼 | 預估時間 |
|:----:|--------|:-------:|
| [1](#步驟-1診斷-review-不能開的原因) | 診斷 Review 不能開的原因（最關鍵！） | 10 分鐘 |
| [2](#步驟-2在-console-設定正確的-review-endpoint-url) | 在 Console 設定正確的 Review Endpoint URL | 5 分鐘 |
| [3](#步驟-3確認你自己在-tester-名單中) | 確認你自己在 Tester 名單中 | 3 分鐘 |
| [4](#步驟-4用手機-line-測試-review-環境) | 用手機 LINE 測試 Review 環境 | 10 分鐘 |
| [5](#步驟-5確認隱私政策頁面可公開存取) | 確認隱私政策頁面可公開存取 | 5 分鐘 |
| [6](#步驟-6確認-loadinggif-已放到伺服器) | 確認 loading.gif 已放到伺服器 | 15 分鐘 |
| [7](#步驟-7確認-basic-settings-全部填好) | 確認 Basic settings 全部填好 | 5 分鐘 |
| [8](#步驟-8用-review-環境跑完整測試清單) | 用 Review 環境跑完整測試清單 | 30-60 分鐘 |
| [9](#步驟-9送審操作教學) | 送審操作教學（含可直接貼的審核說明） | 15 分鐘 |
| [10](#步驟-10退件處理與重新送審) | 退件處理與重新送審 | 視情況 |
| [11](#步驟-11審核通過後上線) | 審核通過後上線 | 15 分鐘 |
| [附錄](#附錄送審前-checklist-全部打勾才送審) | 送審前 Checklist | 參考用 |

---

## 步驟 1：診斷 Review 不能開的原因

### 1.1 先試著打開 Review 環境

```
拿起你的手機，在 LINE App 中的任何聊天室貼上這個連結並點擊：

https://liff.line.me/2009427708-GToVLqgV
```

### 1.2 根據你看到的現象，對照下表找到原因

| 你看到什麼 | 最可能原因 | 跳到哪一步修 |
|-----------|----------|:----------:|
| LINE 的預設空白頁（顯示 `liff-default-review.html` 字樣）| Review 的 Endpoint URL 還是預設值，沒改 | → [步驟 2](#步驟-2在-console-設定正確的-review-endpoint-url) |
| 「無法存取」或「Access Denied」之類的錯誤 | 你不在 Tester 名單裡 | → [步驟 3](#步驟-3確認你自己在-tester-名單中) |
| `INVALID_ARGUMENT` 紅字錯誤 | Endpoint URL 末尾貼到了 Developing 的 LIFF ID | → [步驟 2](#步驟-2在-console-設定正確的-review-endpoint-url) |
| 白屏，什麼都沒有 | JavaScript 錯誤或伺服器沒回應 | → [步驟 4](#步驟-4用手機-line-測試-review-環境) 看排查方法 |
| 看到好牧人登入頁面（和 Developing 一樣）| ✅ Review 其實已經正常了！ | → [步驟 5](#步驟-5確認隱私政策頁面可公開存取) 繼續往下做 |
| 頁面有載入但功能異常（登不進去、沒資料） | 後端/CRM 問題，不是 Mini App 的問題 | → [步驟 4](#步驟-4用手機-line-測試-review-環境) 看排查方法 |

### 1.3 為什麼 Developing 正常但 Review 不行？

```
核心觀念：

LINE Mini App 一個 Channel 有三個「獨立的內部環境」，
每個環境都有自己獨立的 LIFF ID 和 Endpoint URL 設定。

                     你的 Mini App Channel
                ┌───────────┬───────────┬──────────────┐
                │ Developing │  Review   │  Published   │
                ├───────────┼───────────┼──────────────┤
    LIFF ID     │ ...Fi5L5blD│ ...GToVLqgV│ ...PTH3dfeP │
    Endpoint URL│ (你已設好) │ (可能沒設) │ (可能沒設)  │
    存取權限    │ Tester only│ Tester only│ 所有人      │
                └───────────┴───────────┴──────────────┘

Developing 正常 ≠ Review 正常
因為 Review 有自己的 Endpoint URL 要設！
如果你只設了 Developing 的，Review 還是 LINE 的預設空白頁。
```

---

## 步驟 2：在 Console 設定正確的 Review Endpoint URL

### 2.1 打開 LINE Developers Console

```
1. 用瀏覽器前往：https://developers.line.biz/console/
2. 用你的 LINE 帳號登入
3. 選擇你的 Provider
4. 點進好牧人的「LINE MINI App」Channel
```

### 2.2 進入 Web app settings

```
5. 點擊頁面上方的「Web app settings」分頁
6. 你會看到三個環境：Developing、Review、Published
```

### 2.3 設定 Review 的 Endpoint URL

```
7. 找到 Review 那一欄的「Endpoint URL」

   現在顯示的可能是：
   ❌ liff-default-review.html              ← 預設值，這就是問題所在！
   ❌ (空白)                                 ← 沒填
   ❌ ...LineIdLoginView/2009427707-Fi5L5blD  ← 貼成 Developing 的了！

8. 點「Edit」，把 Review 的 Endpoint URL 改成：
```

```
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
```

⚠️ **注意末尾的 LIFF ID 是 Review 的：`2009427708-GToVLqgV`**

### 2.4 順便確認其他兩個也正確

```
Developing 環境的 Endpoint URL：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427707-Fi5L5blD

Review 環境的 Endpoint URL：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV

Published 環境的 Endpoint URL：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP
```

### 2.5 儲存

```
9. 點「Save」（或 Update）儲存
10. 確認畫面上顯示的 URL 是你剛才貼的（沒有被截斷）
```

### ⚠️ 最容易犯的錯：三個環境的 LIFF ID 搞混

```
三個 URL 唯一的差異在末尾的 LIFF ID：

  Developing → ...LineIdLoginView/2009427707-Fi5L5blD   最後是 707...blD
  Review     → ...LineIdLoginView/2009427708-GToVLqgV   最後是 708...qgV
  Published  → ...LineIdLoginView/2009427709-PTH3dfeP   最後是 709...feP
                                          ↑↑↑
                                    注意這三個數字不同！

  快速辨認法：看最後幾個字元
    ...blD → Developing
    ...qgV → Review
    ...feP → Published
```

### ✅ 完成標準

```
Console → Web app settings → Review 環境的 Endpoint URL 顯示：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
```

---

## 步驟 3：確認你自己在 Tester 名單中

### 3.1 為什麼需要？

```
Developing 和 Review 環境有存取限制：
只有 Tester 名單上的人才能開啟！

你在 Developing 能正常打開，可能因為你之前已經加了自己為 Tester。
但 Tester 名單是 Channel 層級的（不是環境層級），
所以如果你在 Developing 是 Tester，Review 也會是 Tester。

但如果你沒有加過 → Developing 和 Review 都會被擋。
你可能在 Developing 是用其他方式進去的。
先確認一下比較安全。
```

### 3.2 操作步驟

```
1. 在 Console 中，同一個 Mini App Channel 頁面
2. 點擊上方的「Roles」分頁
3. 看你的 LINE 帳號是否在 Tester 清單裡

   有 → ✅ 不用做任何事，往下一步
   沒有 → 點「Add Tester」→ 加入自己的 LINE 帳號

4. 如果有其他同工也要幫忙測試 → 一起加進去
5. 加完等 1-2 分鐘再測試
```

### ✅ 完成標準

```
Roles 分頁中可以看到你自己的 LINE 帳號在 Tester 清單裡
```

---

## 步驟 4：用手機 LINE 測試 Review 環境

### 4.1 測試方法

```
在手機 LINE 的任何聊天室（或記事本），貼上並點擊：

https://liff.line.me/2009427708-GToVLqgV
```

### 4.2 正常情況

```
✅ 你應該看到和 Developing 一模一樣的好牧人 LINE 登入頁面：
   • Hero 圖片輪播
   • 「好牧人」標題
   • 經文「我們愛，因為神先愛我們」
   • 聯絡資訊卡片
   • LINE 自動在頂部加的 Mini App Header（返回按鈕 + App 名稱）
```

### 4.3 還是不正常？逐一排查

#### 情況 A：還是看到 `liff-default-review.html`

```
→ 步驟 2 沒存檔成功
→ 回 Console → Web app settings → 確認 Review 的 URL 有改成功
→ 注意：有些瀏覽器會快取，清除 LINE App 快取後再試
```

#### 情況 B：`INVALID_ARGUMENT` 錯誤

```
→ Endpoint URL 末尾的 LIFF ID 和 Review 的 LIFF ID 不匹配
→ 原理：LINE 打開 Review 環境時，會檢查 Endpoint URL 是否屬於這個環境
→ 如果你把 Developing 的 LIFF ID (707...blD) 貼到了 Review 的 Endpoint URL → 不匹配 → 報錯

修法：
  Console → Web app settings → Review → Endpoint URL
  確認末尾是 2009427708-GToVLqgV（不是 2009427707-Fi5L5blD）
```

#### 情況 C：白屏

```
→ 可能是 JavaScript 錯誤
→ 在電腦瀏覽器（無痕模式）直接打開 Review 的 Endpoint URL：
  https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV

→ 如果電腦也打不開 → 是伺服器問題（和 Mini App 無關）
→ 如果電腦有頁面但手機白屏 → 可能是 LIFF SDK 或 CSS 問題

排查工具：
  iOS：LINE App → 設定 → LINE Labs → 啟用 Developer Mode → 可看 Console 錯誤
  Android：和 iOS 類似，在 LINE 設定中找 Developer 相關選項
```

#### 情況 D：頁面有出來但功能異常

```
→ 這和 Review 環境無關，是後端問題
→ 確認 CRM 連線正常（CrmConnection 設定）
→ 確認伺服器可以正常處理請求
→ 用 Developing 的 URL 也測一下，確認不是全面性的問題
```

### ✅ 完成標準

```
用手機 LINE 打開 https://liff.line.me/2009427708-GToVLqgV
→ 看到好牧人 LINE 登入頁面
→ 可以正常登入、看到資料
→ 和 Developing 環境的體驗一樣
```

---

## 步驟 5：確認隱私政策頁面可公開存取

### 5.1 為什麼是送審必要條件？

```
LINE 審核員會直接用瀏覽器打開你填的 Privacy Policy URL。
如果打不開 → 100% 退件。
如果內容太簡略（例如只有一行字） → 很可能退件。
```

### 5.2 測試方法

```
用以下任一方式測試（重點是「不用登入就能看到」）：

方法 A：用手機的 Safari 或 Chrome（不是 LINE App）打開：
        https://jesus.speechmessage.com.tw:807/Privacy

方法 B：用電腦的無痕視窗（Ctrl+Shift+N）打開同一個網址

方法 C：用另一台沒有登入過你系統的裝置打開
```

### 5.3 預期結果

```
✅ 正常：看到「🐑 好牧人 隱私政策」頁面，包含：
   • 服務說明
   • 蒐集的個人資料
   • 資料用途
   • 資料保護措施
   • 聯絡資訊（地址、電話、Email）

❌ 異常情況：
   → 404 Not Found → Startup.cs 路由有問題或沒部署到伺服器
   → 需要登入 → Controller Action 需要加 [AllowAnonymous]
   → 空白頁 → View 檔案不存在或有渲染錯誤
```

### 5.4 你的系統現況（已確認）

```
✅ Privacy.cshtml 已建立（完整的隱私政策頁面）
✅ AuthenticationController.Core.cs 有 Privacy Action（第 80-86 行）
✅ Startup.cs 有 /Privacy 路由（第 740-744 行）
✅ 沒有全域 [Authorize] 限制，所以 Privacy 頁面不需要登入即可存取

⚠️ 唯一的疑慮：
   確認你已經把最新程式碼部署到伺服器！
   如果伺服器上跑的是舊版程式碼（沒有 Privacy 路由），就會 404。
```

### ✅ 完成標準

```
用手機 Safari/Chrome（不是 LINE App）打開：
https://jesus.speechmessage.com.tw:807/Privacy
→ 看到完整的「好牧人 隱私政策」頁面（不需要登入）
```

---

## 步驟 6：確認 loading.gif 已放到伺服器

### 6.1 為什麼需要？

```
LINE Mini App 在載入你的網頁時，會在畫面上顯示一個 loading 動畫。
LINE 會自動去你的網站根目錄找 loading.gif 這個檔案。

如果找不到 → 顯示 LINE 預設的載入畫面（不影響審核，但不專業）
如果有 → 顯示你自訂的載入畫面（更好的品牌形象）
```

### 6.2 確認方式

```
用瀏覽器打開：
https://jesus.speechmessage.com.tw:807/loading.gif

✅ 看到圖片 → OK
❌ 404 Not Found → 需要建立
```

### 6.3 ⚠️ 目前狀態：loading.gif 尚未建立

```
我已確認：ChurchReport\wwwroot\ 資料夾中目前沒有 loading.gif 檔案。
你需要手動建立一個。
```

### 6.4 三種快速製作方式

#### 方案 A：線上產生器（推薦，5 分鐘搞定）

```
1. 打開 https://loading.io/
2. 在首頁選擇一個你喜歡的旋轉動畫樣式
3. 點擊該樣式，進入自訂頁面
4. 把顏色改成 #4864b8（好牧人品牌藍色）
5. 把尺寸改成 240×240 px
6. 點「Download」下載 GIF
7. 把下載的檔案改名為「loading.gif」
8. 複製到 ChurchReport\wwwroot\ 資料夾
```

#### 方案 B：用現有教會 LOGO

```
1. 找到 ChurchReport\wwwroot\assets\images\ChurchLogo.png
2. 用小畫家開啟
3. 「調整大小」→ 像素 → 240×240
4. 「另存新檔」→ 儲存到 ChurchReport\wwwroot\loading.gif
   （小畫家可以存 PNG，但副檔名改成 .gif 也能用）
```

#### 方案 C：用 PowerShell 快速產生一個簡單的佔位圖

```powershell
# 在 Visual Studio 的終端機中執行：
# 這會建立一個簡單的 1x1 像素 GIF 作為暫時的佔位圖
# 之後再換成正式的 loading 動畫

$gifBytes = [Convert]::FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")
$path = Join-Path (Get-Location) "ChurchReport\wwwroot\loading.gif"
[System.IO.File]::WriteAllBytes($path, $gifBytes)
Write-Host "loading.gif 已建立在：$path"
```

### 6.5 部署到伺服器

```
建立 loading.gif 後，需要重新部署到伺服器。

部署後確認：
https://jesus.speechmessage.com.tw:807/loading.gif
→ 能看到圖片 ✅
```

### ✅ 完成標準

```
https://jesus.speechmessage.com.tw:807/loading.gif → 顯示圖片（不是 404）
```

---

## 步驟 7：確認 Basic settings 全部填好

### 7.1 操作步驟

```
1. Console → 好牧人 Mini App Channel → 「Basic settings」分頁
2. 逐一確認以下欄位都有填：
```

| # | 欄位 | 必填 | 應填內容 | ✓ |
|---|------|:----:|---------|:---:|
| 1 | Channel icon | ✅ | 好牧人 LOGO 圖片（500×500 PNG，不透明底色） | □ |
| 2 | Channel name | ✅ | `好牧人`（⚠️ 絕對不能含 "LINE"） | □ |
| 3 | Channel description | ✅ | `好牧人教會管理系統，提供小組牧養回報、線上奉獻、會友管理等服務` | □ |
| 4 | Email address | ✅ | `mengsunghu@gmail.com` | □ |
| 5 | Privacy policy URL | ✅ | `https://jesus.speechmessage.com.tw:807/Privacy` | □ |
| 6 | Terms of use URL | 選填 | 先留空也行 | □ |
| 7 | Service company's country/region | ✅ | `Taiwan` | □ |

### 7.2 Channel Icon 快速製作（如果還沒上傳）

```
規格：
  • 格式：PNG 或 JPG
  • 尺寸：建議 500×500 px，最小 100×100 px
  • 必須有底色（不能透明背景）
  • 不能包含 "LINE" 字樣

最快的方式：
  1. 用你電腦上的任何圖片工具（小畫家即可）
  2. 建立 500×500 畫布
  3. 放上教會 LOGO
  4. 存成 PNG
  5. Console → Basic settings → Channel icon → Upload → 選擇圖片 → Save
```

### ✅ 完成標準

```
以上 7 個欄位中，所有 ✅ 必填的都有填好
Channel name 不含 "LINE"
Channel icon 有圖片
```

---

## 步驟 8：用 Review 環境跑完整測試清單

### 8.1 為什麼要用 Review 環境測試？

```
LINE 審核員看到的就是 Review 環境！
不是 Developing，不是 Published。
你必須確認審核員看到的東西是正常的。
```

### 8.2 Review 環境 LIFF URL（貼到手機 LINE 開啟）

```
https://liff.line.me/2009427708-GToVLqgV
```

### 8.3 測試清單

#### 📋 基本載入（必過）

| # | 測試項目 | 預期結果 | ✓ |
|---|---------|---------|:---:|
| 1 | 點擊 LIFF URL 後 | 看到 loading 動畫（不是白屏） | □ |
| 2 | 載入完成後 | 看到好牧人 LINE 登入頁面 | □ |
| 3 | 載入時間 | 3 秒內完成（不含 loading 動畫時間） | □ |
| 4 | LINE Header | 頂部出現 LINE 的返回按鈕和「好牧人」名稱 | □ |
| 5 | 內容不被遮住 | 頂部內容沒被 LINE Header 擋住；底部沒被 Home Indicator 切掉 | □ |

#### 📋 登入流程（必過）

| # | 測試項目 | 預期結果 | ✓ |
|---|---------|---------|:---:|
| 6 | LIFF 初始化 | 不報錯（無 INVALID_ARGUMENT） | □ |
| 7 | 自動取得 Profile | 顯示你的 LINE 名字 | □ |
| 8 | 已綁定教友 | 自動導向小組管理頁面，有資料 | □ |
| 9 | 未綁定教友 | 導向綁定頁面，可輸入姓名和電話 | □ |

#### 📋 核心功能（至少 C1 要過）

| # | 測試項目 | 預期結果 | ✓ |
|---|---------|---------|:---:|
| C1 | DataGrid 操作 | 手機上可以滑動、點擊、看到資料 | □ |
| C2 | 奉獻流程（選測）| 進入奉獻頁面正常 | □ |

#### 📋 額外確認

| # | 測試項目 | 預期結果 | ✓ |
|---|---------|---------|:---:|
| E1 | Privacy 頁面 | 用 Safari/Chrome 打開 .../Privacy 可看到 | □ |
| E2 | F12 Console | 沒有紅色 JavaScript 錯誤 | □ |

---

## 步驟 9：送審操作教學

### 9.1 送審前最後確認（全部打勾才送！）

```
□ Review Endpoint URL 已設定且正確
□ Channel icon 已上傳
□ Channel name = 「好牧人」（無 LINE 字樣）
□ Channel description 已填寫
□ Email = mengsunghu@gmail.com
□ Privacy policy URL = https://jesus.speechmessage.com.tw:807/Privacy
□ Privacy 頁面未登入可存取
□ Review 環境功能測試通過（步驟 8 全部 ✅）
```

### 9.2 操作步驟

```
1. 登入 LINE Developers Console
   https://developers.line.biz/console/

2. 選擇你的 Provider → 進入好牧人 Mini App Channel

3. 找到「Submit for review」按鈕
   （通常在 Overview 分頁的頁面上方或下方）

4. 點擊「Submit for review」

5. 填寫審核說明（見下方範本，直接複製貼上）

6. 點「Submit」或「Confirm」

7. 出現確認對話框 → 點「OK」
```

### 9.3 審核說明範本（直接複製貼上）

```
【服務名稱】
好牧人教會管理系統

【服務說明】
好牧人是一個教會會友管理系統，服務對象為台灣教會會友。

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
- 部分功能需要已綁定的教友帳號才能完整測試
- 系統連接 Microsoft Dynamics 365 CRM 作為資料庫
- 如需測試帳號，請告知，我們可以提供測試用帳號

【隱私政策】
https://jesus.speechmessage.com.tw:807/Privacy
```

### 9.4 送審後

```
✅ 你會收到 LINE 的確認 Email
⏳ 等待審核（通常數個工作天，無固定時間）
📧 審核結果會透過 Email 通知你（mengsunghu@gmail.com）
```

---

## 步驟 10：退件處理與重新送審

### 如果審核不通過（很正常，不要緊張！）

LINE 會在 Email 中告訴你退件原因。以下是最常見的 6 種原因與對策：

### 退件原因 ①：隱私政策頁面無法存取

```
原因：審核員打開 Privacy URL 看到 404 或錯誤頁面
對策：
  1. 確認伺服器正在運作
  2. 確認已部署最新版本（包含 Privacy 路由）
  3. 用手機 Safari 確認可以打開 https://jesus.speechmessage.com.tw:807/Privacy
  4. 修復後 → 重新提交
```

### 退件原因 ②：頁面載入太慢

```
原因：Mini App 頁面超過 3-5 秒才完成載入
對策：
  1. 壓縮 Hero 圖片（church-001.jpg, church-002.jpg）到 200KB 以下
     工具：https://squoosh.app/
  2. 確認伺服器效能正常
  3. 確認 SSL 憑證有效（無 SSL 警告）
  4. 修復後 → 重新提交
```

### 退件原因 ③：頁面白屏或 JavaScript 錯誤

```
原因：審核員看到空白頁或錯誤訊息
對策：
  1. 用手機 LINE 打開 Review URL 自己測一遍
  2. 啟用 LINE Labs Developer Mode 看 Console 錯誤
  3. 確認 Review Endpoint URL 正確
  4. 修復後 → 重新提交
```

### 退件原因 ④：Channel name 包含「LINE」

```
對策：
  Console → Basic settings → Channel name → 改成不含 LINE 的名稱
  → 儲存 → 重新提交
```

### 退件原因 ⑤：核心功能無法使用

```
原因：審核員操作時登入失敗、沒資料、功能異常
對策：
  1. 確認伺服器和 CRM 連線正常
  2. 在 Review 環境自己做完整測試
  3. 在送審說明中提供更詳細的測試帳號和步驟
  4. 修復後 → 重新提交
```

### 退件原因 ⑥：不符合 LINE Mini App Policy

```
Policy 全文：https://terms2.line.me/LINE_MINI_App?lang=en
好牧人是教會管理系統，內容合規，通常不會因此退件。
如果收到 → 仔細閱讀退件說明了解具體問題。
```

### 重新提交流程

```
1. 修復所有退件指出的問題
2. 在 Review 環境重新測試確認
3. 如果有改程式碼 → 重新部署到伺服器
4. Console → Mini App Channel → 再次點「Submit for review」
5. 在送審說明中說明：「已修復 [退件原因]，修復內容如下：...」
```

---

## 步驟 11：審核通過後上線

### 11.1 收到審核通過通知 🎉

```
LINE 會發 Email 通知你審核通過。
好牧人正式成為 Verified LINE Mini App！
```

### 11.2 修改 appsettings.json

```
1. 打開 Visual Studio
2. 開啟 ChurchReport\appsettings.json
3. 找到 MiniApp 區段

修改前：
"ActiveEnvironment": "Developing"

修改後：
"ActiveEnvironment": "Published"

4. Ctrl+S 存檔
```

### 11.3 確認 Published Endpoint URL

```
Console → Web app settings → Published 環境：
確認 Endpoint URL 是：
https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP
```

### 11.4 重新部署

```
Build → 部署到伺服器 → 重啟 IIS 站台/Application Pool
```

### 11.5 最終確認

```
用手機 LINE 打開 Published 連結：
https://liff.line.me/2009427709-PTH3dfeP

確認所有功能正常 → ✅ 正式上線！
```

### 11.6 設定 Custom Path（選用）

```
Console → Web app settings → Published → Custom Path
輸入：good-shepherd
儲存後教友可用：https://miniapp.line.me/good-shepherd
```

### 11.7 對外公告 🎉

```
在教會 LINE 群組分享：

📢 好消息！好牧人已升級為 LINE Mini App！

🔗 點擊開啟：https://liff.line.me/2009427709-PTH3dfeP

✅ 在 LINE 中直接搜尋「好牧人」就能找到
✅ 可以加到手機主畫面，像 App 一樣使用
✅ 自動 LINE 登入，不用輸入帳號密碼
```

---

## 附錄：送審前 Checklist（全部打勾才送審！）

```
═══════════════════════════════════════════════════════════════
 好牧人 LINE Mini App — 送審前必做確認清單
═══════════════════════════════════════════════════════════════

【LINE Developers Console 設定】

  □ Review Endpoint URL 已設定：
    https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427708-GToVLqgV
  
  □ Published Endpoint URL 已設定：
    https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/2009427709-PTH3dfeP
  
  □ Channel icon 已上傳（好牧人 LOGO 圖片，500×500 PNG）
  
  □ Channel name =「好牧人」（不含 LINE 字樣）
  
  □ Channel description 已填寫
  
  □ Email address = mengsunghu@gmail.com
  
  □ Privacy policy URL = https://jesus.speechmessage.com.tw:807/Privacy
  
  □ Service company's country/region = Taiwan
  
  □ 自己已加入 Tester（Console → Roles）

【伺服器確認】

  □ https://jesus.speechmessage.com.tw:807/Privacy
    → 用手機 Safari/Chrome 打開 → 未登入可看到完整頁面 ✅

  □ https://jesus.speechmessage.com.tw:807/loading.gif
    → 顯示圖片（不是 404）✅

  □ 已部署最新版程式碼到伺服器 ✅

【Review 環境功能測試】

  □ https://liff.line.me/2009427708-GToVLqgV
    → 手機 LINE App 開啟 → 看到好牧人登入頁面 ✅

  □ LIFF 初始化成功（不報 INVALID_ARGUMENT）

  □ 已綁定教友 → 自動登入 → 導向小組管理頁面 → 有資料

  □ 未綁定教友 → 導向綁定頁面 → 可完成綁定

  □ DataGrid 在手機上可正常操作（滑動、點擊）

  □ 載入時間在 3 秒內

  □ F12 Console 無紅色 JavaScript 錯誤

  □ 頂部內容沒有被 LINE Header 遮住

═══════════════════════════════════════════════════════════════
 ✅ 以上全部打勾 → 可以送審了！
═══════════════════════════════════════════════════════════════
```

---

*文件版本：v1.0*  
*建立日期：2025 年 7 月*  
*專案：ChurchReport 好牧人教會管理系統*  
*分支：Jesus_5.0.4.LineMiniApp*  
*對應 LIFF IDs：*  
*　Dev = 2009427707-Fi5L5blD*  
*　Review = 2009427708-GToVLqgV*  
*　Published = 2009427709-PTH3dfeP*
