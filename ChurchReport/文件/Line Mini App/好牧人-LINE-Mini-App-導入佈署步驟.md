# 🐑 好牧人 ChurchReport — LINE Mini App 導入佈署步驟
### 專案專屬完整教學（從現有 ASP.NET Core Web App 轉換到 LINE Mini App）

> **適用對象**：完全沒有 LINE Mini App 開發經驗的新手  
> **專案名稱**：ChurchReport（好牧人教會管理系統）  
> **技術棧**：ASP.NET Core (.NET 10) + DevExtreme 21.2.7 + jQuery + LIFF SDK  
> **伺服器**：`jesus.speechmessage.com.tw` (Port 807/700)  
> **更新日期**：2025 年 7 月

---

## 🏗️ 實施進度追蹤

> 以下是程式碼層面已完成的實施項目。  
> ⚠️ LINE Developers Console 操作（Phase 0、Phase 1）需要人工在網頁上完成。

| # | 項目 | 檔案 | 狀態 |
|---|------|------|:----:|
| 1 | `appsettings.json` 新增 MiniApp 設定區段 | `appsettings.json` | ✅ 已完成 |
| 2 | Privacy 隱私政策頁面 (View) | `Views/Authentication/Privacy.cshtml` | ✅ 已完成 |
| 3 | Privacy 路由 (Controller Action) | `Controllers/AuthenticationController.Core.cs` | ✅ 已完成 |
| 4 | Privacy 路由 (Startup 路由表) | `Startup.cs` | ✅ 已完成 |
| 5 | Safe Area CSS 檔案 | `wwwroot/css/mini-app-safe-area.css` | ✅ 已完成 |
| 6 | Mini App 偵測中間件 | `Middleware/MiniAppDetectionMiddleware.cs` | ✅ 已完成 |
| 7 | 中間件註冊 (Startup) | `Startup.cs` | ✅ 已完成 |
| 8 | LineIdLoginView viewport-fit + Safe Area CSS | `Views/Authentication/LineIdLoginView.cshtml` | ✅ 已完成 |
| 9 | LineLiffView viewport-fit + Safe Area CSS | `Views/Authentication/LineLiffView.cshtml` | ✅ 已完成 |
| 10 | Login viewport-fit + Safe Area CSS | `Views/Authentication/Login.cshtml` | ✅ 已完成 |
| 11 | DediationLineLoginView viewport-fit + Safe Area CSS | `Views/Dedication/DediationLineLoginView.cshtml` | ✅ 已完成 |
| 12 | DedicationFeeView viewport-fit + Safe Area CSS | `Views/Dedication/DedicationFeeView.cshtml` | ✅ 已完成 |
| 13 | _Layout viewport-fit + Safe Area CSS | `Views/Shared/_Layout.cshtml` | ✅ 已完成 |
| 14 | 向 LINE Taiwan 申請 Mini App 開發許可 | （人工操作）| ⬜ 待執行 |
| 15 | 在 Console 建立 Mini App Channel | （人工操作）| ⬜ 待執行 |
| 16 | 填入 LIFF ID 到 appsettings.json | `appsettings.json` | ⬜ 待執行 |
| 17 | 設定 Endpoint URL | （Console 操作）| ⬜ 待執行 |
| 18 | 準備 loading.gif | `wwwroot/loading.gif` | ⬜ 待執行 |
| 19 | 準備 Channel Icon (500x500) | （設計）| ⬜ 待執行 |
| 20 | 測試 + 送審 | （人工操作）| ⬜ 待執行 |

---

## 📋 目錄

| 章節 | 內容 |
|------|------|
| [第一章](#第一章我們的專案現況分析) | 我們的專案現況分析 |
| [第二章](#第二章line-mini-app-vs-現有-liff-差異) | LINE Mini App vs 現有 LIFF 差異 |
| [第三章](#第三章整體架構圖好牧人專屬) | 整體架構圖（好牧人專屬）|
| [第四章](#第四章需要轉換的頁面清單與對應關係) | 需要轉換的頁面清單與對應關係 |
| [第五章](#第五章前置準備工作) | 前置準備工作 |
| [第六章](#第六章在-line-developers-console-建立-mini-app-channel) | 在 LINE Developers Console 建立 Mini App Channel |
| [第七章](#第七章appsettingsjson-新增設定) | appsettings.json 新增設定 |
| [第八章](#第八章建立-mini-app-middleware-中間件) | 建立 Mini App Middleware（中間件）|
| [第九章](#第九章改造現有-cshtml-頁面) | 改造現有 .cshtml 頁面（逐檔教學）|
| [第十章](#第十章controller-後端改造) | Controller 後端改造 |
| [第十一章](#第十一章safe-area-與-ui-適配) | Safe Area 與 UI 適配 |
| [第十二章](#第十二章本地開發測試流程) | 本地開發測試流程 |
| [第十三章](#第十三章三個環境部署) | 三個環境部署 |
| [第十四章](#第十四章提交審核上線) | 提交審核上線 |
| [第十五章](#第十五章常見問題排查) | 常見問題排查 |
| [附錄A](#附錄a完整-checklist) | 完整 Checklist |
| [附錄B](#附錄b名詞解釋) | 名詞解釋 |

---

## 第一章：我們的專案現況分析

### 1.1 目前的技術架構

```
好牧人 ChurchReport 現有系統：

┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core (.NET 10) Web Application                      │
│                                                              │
│  前端：                                                       │
│    ├── DevExtreme 21.2.7（DataGrid、Form、Gallery、Toast 等）│
│    ├── jQuery + Bootstrap                                    │
│    ├── LIFF SDK 2.x（已整合，用於 LINE 登入）                 │
│    └── Razor Views (.cshtml)                                 │
│                                                              │
│  後端：                                                       │
│    ├── Controllers（MVC 路由）                                │
│    │   ├── AuthenticationController（LINE/帳密登入）           │
│    │   ├── SmallGroupController（小組管理）                    │
│    │   ├── DedicationController（奉獻金流）                    │
│    │   └── PersonalController（個人資料）                      │
│    ├── CRM (Dynamics 365) 連線                               │
│    ├── 金流整合：永豐QPay / 高鉅MyPay / 台新TSPG              │
│    └── Session + Cookie 身份驗證                              │
│                                                              │
│  伺服器：                                                     │
│    ├── 雲端機房：jesus.speechmessage.com.tw:807               │
│    └── 公司機房：jesusback.speechmessage.com.tw:803           │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 目前已經有的 LINE 整合

我們的系統**已經整合了 LIFF SDK**，這代表轉換到 LINE Mini App 的路程已經走了 60%！

```
✅ 已有的基礎：
  ├── LIFF SDK 2.x 已引入（在 cshtml 中用 CDN 載入）
  ├── liff.init() 已實作（在 LineIdLoginView.cshtml）
  ├── liff.getProfile() 已實作（取得用戶 displayName、userId）
  ├── LINE Login OAuth 2.0 已實作（支援電腦版 LINE）
  ├── LIFF ID 已在 appsettings.json 設定
  │     ├── BindingLiffId: "1653819697-YkPyPkr6"（綁定頁面）
  │     └── LoginLiffId: "2007621061-Exd9BGv8"（登入頁面）
  ├── 環境偵測已實作（detectEnvironmentAndChooseMethod）
  ├── LINE Login Channel 已建立
  │     ├── ChannelId: "2007621061"
  │     └── CallbackUrl: "https://jesus.speechmessage.com.tw:807/..."
  └── Session + Cookie 身份管理已完善（含 Session Bleeding 防護）

❌ 還需要做的：
  ├── 建立 LINE Mini App Channel（目前只有 LINE Login Channel）
  ├── 取得 Mini App 專用的三組 LIFF ID（Developing/Review/Published）
  ├── 頁面 UI 適配 LIFF Browser Safe Area
  ├── 加入 loading.gif
  ├── 處理外部瀏覽器開啟情境
  └── 提交 LINE 審核
```

### 1.3 需要轉換的 7 個頁面

| # | 檔案路徑 | 功能 | 轉換難度 |
|---|---------|------|:-------:|
| 1 | `Views/Authentication/LineIdLoginView.cshtml` | LINE ID 登入主頁 | ⭐ 低 |
| 2 | `Views/Authentication/LineLiffView.cshtml` | LINE LIFF 綁定註冊頁 | ⭐ 低 |
| 3 | `Views/Authentication/Login.cshtml` | 帳號密碼登入頁 | ⭐⭐ 中 |
| 4 | `Views/Dedication/DediationLineLoginView.cshtml` | 奉獻 LINE 登入頁 | ⭐ 低 |
| 5 | `Views/Dedication/DedicationFeeView.cshtml` | 奉獻紀錄查詢頁 | ⭐⭐ 中 |
| 6 | `Views/Home/_GeneralGroupGrids.cshtml` | 小組牧養資料網格 | ⭐⭐⭐ 高 |
| 7 | `Views/Dedication/DediationLineLoginView.cshtml` | （同 #4，重複列出）| — |

---

## 第二章：LINE Mini App vs 現有 LIFF 差異

### 2.1 我們目前用的是「LIFF App」，要升級成「LINE Mini App」

```
┌──────────────────────────────────────────────────────────────────┐
│                 兩者的關係                                         │
│                                                                  │
│  LIFF App（我們目前用的）                                         │
│  └── LINE Mini App（LIFF App 的超集）✅ 我們的目標                 │
│                                                                  │
│  差異比較：                                                       │
│  ┌──────────────────┬──────────────────┬───────────────────────┐ │
│  │    項目          │   LIFF App       │  LINE Mini App        │ │
│  ├──────────────────┼──────────────────┼───────────────────────┤ │
│  │ Channel 類型     │ LINE Login       │ LINE MINI App         │ │
│  │ 用戶入口         │ 只能透過連結     │ LINE 搜尋/首頁/分享   │ │
│  │ Service Message  │ ❌ 不支援       │ ✅ 支援              │ │
│  │ Quick-fill       │ ❌ 不支援       │ ✅ 支援              │ │
│  │ 主畫面捷徑       │ ❌ 不支援       │ ✅ 支援              │ │
│  │ 審核要求         │ 不需要          │ 需要 LINE 審核        │ │
│  │ Custom Path      │ ❌ 不支援       │ ✅ 支援              │ │
│  │ 載入畫面         │ 沒有            │ 有專屬載入動畫        │ │
│  │ Header/Footer    │ 沒有            │ LINE 提供 Header      │ │
│  │ LIFF SDK         │ ✅ 一般版       │ ✅ 相同 + 額外 API    │ │
│  └──────────────────┴──────────────────┴───────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘

結論：
  我們的 LIFF 程式碼幾乎不用改！
  主要是「建新的 Mini App Channel」+「UI 微調」+「送審」
```

### 2.2 對用戶的好處

```
轉換前（現在）：
  教友在 LINE 收到連結 → 點開 → 跑 LIFF → 登入 → 使用

轉換後（Mini App）：
  教友在 LINE 搜尋「好牧人」→ 直接開啟 → 自動登入 → 使用 ✅
  教友在 LINE 首頁 → 看到好牧人圖示 → 點一下就開啟 ✅
  教友加捷徑到手機桌面 → 像原生 App 一樣使用 ✅
```

---

## 第三章：整體架構圖（好牧人專屬）

### 3.1 轉換後的完整系統架構

```
╔══════════════════════════════════════════════════════════════════════════════╗
║                          LINE Platform                                      ║
║                                                                             ║
║  ┌───────────────────────────────────────────────────────────────────────┐  ║
║  │                   LINE Developers Console                             │  ║
║  │                                                                       │  ║
║  │   Provider：好牧人 (或音訊科技)                                        │  ║
║  │   │                                                                   │  ║
║  │   ├── 【既有】LINE Login Channel (ID: 2007621061)                     │  ║
║  │   │     └── 用於 Server-side OAuth（電腦版 LINE）                     │  ║
║  │   │                                                                   │  ║
║  │   └── 【新建】LINE MINI App Channel  ← ⭐ 這是要新建的               │  ║
║  │         ├── Developing 環境                                           │  ║
║  │         │     ├── LIFF ID: (新) xxxx-Developing                       │  ║
║  │         │     └── Endpoint: https://jesus.speechmessage.com.tw:807/   │  ║
║  │         ├── Review 環境                                               │  ║
║  │         │     ├── LIFF ID: (新) xxxx-Review                           │  ║
║  │         │     └── Endpoint: https://jesus.speechmessage.com.tw:807/   │  ║
║  │         └── Published 環境                                            │  ║
║  │               ├── LIFF ID: (新) xxxx-Published                        │  ║
║  │               └── Endpoint: https://jesus.speechmessage.com.tw:807/   │  ║
║  └───────────────────────────────────────────────────────────────────────┘  ║
║                                                                             ║
║  ┌──────────────────────┐                                                  ║
║  │   LINE App           │                                                  ║
║  │  （教友的手機）       │                                                  ║
║  │                      │                                                  ║
║  │  ┌────────────────┐  │    入口方式：                                     ║
║  │  │ LINE MINI App  │  │    • LINE 搜尋「好牧人」                         ║
║  │  │ ┌────────────┐ │  │    • LINE 首頁推薦                               ║
║  │  │ │ 好牧人     │ │  │    • 好友分享連結                                ║
║  │  │ │ Header     │ │  │    • QR Code 掃描                               ║
║  │  │ ├────────────┤ │  │    • 主畫面捷徑 (Add to Home Screen)             ║
║  │  │ │            │ │  │                                                  ║
║  │  │ │ 你的網頁   │ │  │                                                  ║
║  │  │ │ (Razor     │ │  │                                                  ║
║  │  │ │  Views)    │ │  │                                                  ║
║  │  │ │            │ │  │                                                  ║
║  │  │ ├────────────┤ │  │                                                  ║
║  │  │ │ Footer     │ │  │                                                  ║
║  │  │ └────────────┘ │  │                                                  ║
║  │  └────────────────┘  │                                                  ║
║  └──────────┬───────────┘                                                  ║
╚═════════════╪══════════════════════════════════════════════════════════════╝
              │ HTTPS (LIFF SDK / AJAX)
              │
┌─────────────▼────────────────────────────────────────────────────────────┐
│                                                                           │
│   好牧人 ASP.NET Core 後端伺服器                                           │
│   (jesus.speechmessage.com.tw:807)                                        │
│                                                                           │
│   ┌───────────────────────────────────────────────────────────────────┐   │
│   │  Controllers                                                      │   │
│   │  ├── AuthenticationController（LINE 登入 / 帳密登入 / 綁定）       │   │
│   │  │     ├── LineIdLoginView     → Mini App 主入口                  │   │
│   │  │     ├── LineLiffView        → 新用戶綁定頁                     │   │
│   │  │     ├── Login               → 帳號密碼登入                     │   │
│   │  │     ├── SaveUserLineId      → AJAX: 儲存 LINE ID              │   │
│   │  │     ├── LineLoginStart      → Server-side OAuth 起點           │   │
│   │  │     └── LineCallback        → OAuth Callback                   │   │
│   │  ├── DedicationController（奉獻管理）                              │   │
│   │  │     ├── DediationLineLoginView → 奉獻 LINE 登入               │   │
│   │  │     ├── QPayView              → 奉獻主頁                      │   │
│   │  │     └── DedicationFeeView     → 奉獻紀錄查詢                   │   │
│   │  ├── SmallGroupController（小組管理）                              │   │
│   │  │     ├── IntegrateView         → 綜合報告                       │   │
│   │  │     ├── MultiGroupView        → 多小組報告                     │   │
│   │  │     └── HappyGroup            → 幸福小組                       │   │
│   │  └── PersonalController（個人資料）                                │   │
│   └───────────────────────────────────────────────────────────────────┘   │
│                                                                           │
│   ┌───────────────────────────────────────────────────────────────────┐   │
│   │  後端服務                                                         │   │
│   │  ├── Dynamics 365 CRM 連線池 (ICrmConnectionPool)                 │   │
│   │  ├── 金流服務 (IPayment → QPay/MyPay/TSPG)                       │   │
│   │  ├── LINE Messaging API (LineNotifyUtility)                       │   │
│   │  ├── Session + Cookie 身份管理                                    │   │
│   │  └── MemoryCache (InMemoryDataContext)                            │   │
│   └───────────────────────────────────────────────────────────────────┘   │
│                                                                           │
└───────────────────────────────────────────────────────────────────────────┘
              │
              ▼
┌───────────────────────────────────────┐
│  Dynamics 365 CRM                      │
│  (speechmessage.com.tw:7777)           │
│                                        │
│  Organization: jesus                   │
│  ├── 會友資料 (Contact)                │
│  ├── 小組紀錄                          │
│  ├── 奉獻紀錄                          │
│  └── 出席紀錄                          │
└───────────────────────────────────────┘
```

### 3.2 頁面導航流程圖（Mini App 版）

```
教友在 LINE 打開好牧人 Mini App
                │
                ▼
    ┌─────────────────────────┐
    │  LineIdLoginView.cshtml  │ ← Mini App Endpoint URL 指向這裡
    │  （LINE 登入主頁）        │
    │                         │
    │  • 自動偵測環境          │
    │  • LIFF SDK / OAuth     │
    │  • 顯示教會資訊          │
    │  • Hero 圖片輪播         │
    └─────────┬───────────────┘
              │
     liff.getProfile() → 取得 userId
     AJAX POST → /Authentication/SaveUserLineId
              │
      ┌───────┴───────────────────────┐
      │                               │
   已綁定                            未綁定
      │                               │
      ▼                               ▼
  data.message                 ┌─────────────────────┐
  ≠ "尚未綁定"                 │ LineLiffView.cshtml  │
      │                        │（綁定註冊頁面）       │
      │                        │                     │
      │                        │ • 輸入姓名/電話      │
      │                        │ • 綁定 LINE 帳號     │
      │                        └──────────┬──────────┘
      │                                   │ 綁定成功
      │◄──────────────────────────────────┘
      │
      ├── DisplayViewType == "MultiGroupView"
      │   └──► /SmallGroup/MultiGroupView/{ActiveListId}
      │         └── _GeneralGroupGrids.cshtml（小組牧養網格）
      │
      ├── DisplayViewType == "IntegrateView"
      │   └──► /SmallGroup/IntegrateView/{ActiveListId}
      │         └── _GeneralGroupGrids.cshtml（綜合報告網格）
      │
      └── DisplayViewType == "HappyGroupView"
          └──► /SmallGroup/HappyGroup

─── 奉獻流程（獨立入口）───────────────────────────────────────

教友點擊奉獻連結
      │
      ▼
┌─────────────────────────────────┐
│ DediationLineLoginView.cshtml   │
│（奉獻 LINE 登入頁）              │
│                                 │
│ • LIFF SDK / OAuth              │
│ • 登入後導向 QPayView           │
└────────────┬────────────────────┘
             │ 登入成功
             ▼
┌─────────────────────────────────┐
│ QPayView.cshtml                  │
│（奉獻主頁 - 金流選擇）           │
│                                 │
│ • 選擇奉獻類別                   │
│ • 輸入金額                       │
│ • 選擇付款方式                   │
│   (永豐QPay/高鉅MyPay/台新TSPG)  │
└────────────┬────────────────────┘
             │ 查詢奉獻紀錄
             ▼
┌─────────────────────────────────┐
│ DedicationFeeView.cshtml         │
│（奉獻紀錄查詢）                   │
│                                 │
│ • 日期範圍查詢                   │
│ • DataGrid 顯示奉獻明細          │
└─────────────────────────────────┘
```

### 3.3 三個環境部署對應圖

```
┌──────────────────────────────────────────────────────────────────────────┐
│               好牧人 LINE MINI App Channel（三個環境）                     │
│                                                                          │
│  ┌────────────────────┐  ┌────────────────────┐  ┌───────────────────┐  │
│  │   Developing        │  │    Review           │  │   Published       │  │
│  │  （開發測試）        │  │  （送審環境）        │  │ （正式上線）       │  │
│  │                     │  │                     │  │                   │  │
│  │  LIFF ID: A         │  │  LIFF ID: B         │  │  LIFF ID: C       │  │
│  │                     │  │                     │  │                   │  │
│  │  Endpoint URL:      │  │  Endpoint URL:      │  │  Endpoint URL:    │  │
│  │  ↓                  │  │  ↓                  │  │  ↓                │  │
│  └────────┬────────────┘  └────────┬────────────┘  └────────┬──────────┘  │
└───────────┼────────────────────────┼────────────────────────┼────────────┘
            │                        │                        │
            ▼                        ▼                        ▼
  ┌──────────────────┐    ┌───────────────────┐    ┌──────────────────────┐
  │ 開發/測試選擇之一 │    │ 穩定 HTTPS 環境   │    │ 正式伺服器            │
  │                  │    │                   │    │                      │
  │ 方案 A:          │    │ jesus.speech      │    │ jesus.speech         │
  │  ngrok → HTTPS   │    │ message.com.tw    │    │ message.com.tw       │
  │  → localhost:807 │    │ :807              │    │ :807                 │
  │                  │    │                   │    │                      │
  │ 方案 B:          │    │ (和正式同一台也   │    │ • 所有教友可存取      │
  │  直接指向正式     │    │  可以，只要夠穩)  │    │ • Custom Path 啟用   │
  │  伺服器的         │    │                   │    │ • Service Message    │
  │  測試路由         │    │                   │    │   可正式發送          │
  └──────────────────┘    └───────────────────┘    └──────────────────────┘

💡 好牧人的情況：
   因為只有一台伺服器 (jesus.speechmessage.com.tw:807)，
   三個環境可以指向同一台，用不同的 LIFF ID 區分即可。
   在 appsettings.json 中根據環境切換對應的 LIFF ID。
```

---

## 第四章：需要轉換的頁面清單與對應關係

### 4.1 頁面功能對照表

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  # │ 現有頁面                        │ 功能         │ 在 Mini App 中的角色  │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  1 │ LineIdLoginView.cshtml          │ LINE 登入    │ ⭐ Mini App 主入口    │
│    │ Route: /Authentication/         │              │ Endpoint URL 指向    │
│    │   LineIdLoginView/{param}       │              │ 這個頁面             │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  2 │ LineLiffView.cshtml             │ 綁定註冊     │ 新教友首次使用時的   │
│    │ Route: /Authentication/         │              │ 註冊綁定流程         │
│    │   LineLiffView/{param}          │              │                      │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  3 │ Login.cshtml                    │ 帳密登入     │ 管理人員後台入口     │
│    │ Route: /Authentication/Login    │              │ (可從 Mini App 跳轉) │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  4 │ DediationLineLoginView.cshtml   │ 奉獻登入     │ 奉獻專用入口         │
│    │ Route: /Dedication/             │              │ (可設定獨立 Custom   │
│    │   DediationLineLoginView/{id}   │              │  Path)               │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  5 │ DedicationFeeView.cshtml        │ 奉獻紀錄查詢 │ 登入後的功能頁面     │
│    │ Route: /Dedication/             │              │                      │
│    │   DedicationFeeView             │              │                      │
├────┼─────────────────────────────────┼──────────────┼──────────────────────┤
│  6 │ _GeneralGroupGrids.cshtml       │ 小組牧養網格 │ 登入後的核心功能     │
│    │ 嵌入在 IntegrateView /          │              │ (嵌入在 Layout 中)   │
│    │ MultiGroupView 中               │              │                      │
└────┴─────────────────────────────────┴──────────────┴──────────────────────┘
```

### 4.2 改動範圍評估

```
改動量小（✅ 幾乎不用改程式碼）：
  ├── LineIdLoginView.cshtml     → 換 LIFF ID、加 Safe Area CSS
  ├── LineLiffView.cshtml        → 換 LIFF ID、加 Safe Area CSS
  ├── DediationLineLoginView.cshtml → 換 LIFF ID、加 Safe Area CSS
  └── Login.cshtml               → 加 Safe Area CSS

改動量中（⚠️ 需調整 JavaScript 邏輯）：
  └── DedicationFeeView.cshtml   → 可能需要加 LIFF 判斷

改動量大（🔧 需要 UI 重構）：
  └── _GeneralGroupGrids.cshtml  → DataGrid 需要做手機版優化

新增項目：
  ├── appsettings.json → 新增 MiniApp 區段設定
  ├── wwwroot/loading.gif → LINE 載入動畫
  ├── wwwroot/css/mini-app-safe-area.css → Safe Area CSS
  └── Middleware/MiniAppDetectionMiddleware.cs → Mini App 偵測中間件（選用）
```

---

## 第五章：前置準備工作

### 5.1 台灣地區：申請 LINE Mini App 開發許可

> ⚠️ **這是最重要的第一步！台灣無法自行建立 Mini App Channel！**

**申請管道**：
1. 前往 https://tw.linebiz.com/service/other-solutions/line-mini-app/
2. 點擊「立即申請」或「聯絡我們」
3. 填寫申請表單：
   - 公司名稱：（你的教會 / 音訊科技）
   - 服務名稱：好牧人教會管理系統
   - 服務說明：教會會友管理、小組牧養回報、線上奉獻
   - 預期上線時間
   - 聯絡人資訊

```
申請審核通常需要 1-4 週，請提前準備！
在等待期間，可以先進行步驟 5.2 ~ 5.5 的準備工作。
```

### 5.2 確認 LINE Developers Console 帳號

1. 前往 https://developers.line.biz/console/
2. 用你的 LINE 帳號登入
3. 確認你能看到已有的 Provider
4. 確認你能看到已有的 LINE Login Channel (ID: 2007621061)

### 5.3 確認伺服器 HTTPS 正常

```bash
# 測試雲端機房 HTTPS 是否正常
curl -I https://jesus.speechmessage.com.tw:807/

# 預期回應：HTTP/2 200 或 301/302（重導向到登入頁）
# 如果回應 SSL 錯誤，需要先修正 SSL 憑證
```

### 5.4 準備 Mini App 圖示

建立好牧人 Mini App 的 Channel Icon：

```
規格要求：
  ├── 格式：PNG 或 JPG
  ├── 尺寸：500 x 500 px（建議）
  │         最小 100 x 100 px
  ├── 不能有透明背景（必須有底色）
  ├── 不能包含 "LINE" 字樣
  └── 建議使用教會 LOGO 或好牧人圖案

建議設計：
  ┌──────────────┐
  │  ╭──────╮    │
  │  │ 🐑   │    │  ← 好牧人 LOGO
  │  │ 好牧人│    │
  │  ╰──────╯    │
  │  #4864b8 底色 │  ← 使用你 CSS 中的品牌色
  └──────────────┘
```

### 5.5 準備 Loading Icon

```
在 wwwroot 根目錄放置：
  wwwroot/loading.gif

規格：
  ├── 格式：GIF（動態）或 PNG（靜態）
  ├── 尺寸：建議 240 x 240 px
  └── 內容：可以是教會 LOGO 的載入動畫

如果暫時沒有，可以先用簡單的旋轉十字架動畫。
```

### 5.6 準備 Privacy Policy 頁面

LINE 審核要求必須有隱私政策頁面。需要建立一個可公開存取的 HTTPS 頁面：

```
建議 URL：https://jesus.speechmessage.com.tw:807/Privacy

內容需包含：
  ├── 服務名稱（好牧人教會管理系統）
  ├── 營運組織（教會名稱 / 統一編號：13054485）
  ├── 蒐集的個人資料類型（LINE 顯示名稱、LINE User ID）
  ├── 資料用途說明
  ├── 資料保護措施
  └── 聯絡方式（mengsunghu@gmail.com / 03-4316679）
```

---

## 第六章：在 LINE Developers Console 建立 Mini App Channel

> ⚠️ 台灣地區需先通過步驟 5.1 的許可申請，才能執行此章節

### 6.1 建立步驟（含截圖說明）

```
Step 1：登入 LINE Developers Console
        https://developers.line.biz/console/

Step 2：選擇你的 Provider
        ⚠️ 重要：必須和現有的 LINE Login Channel 在同一個 Provider 下！
        這樣 User ID 才會一致，教友不需要重新綁定

Step 3：點擊「Create a new channel」

Step 4：選擇 Channel Type = 「LINE MINI App」

Step 5：填寫 Channel 資訊：
```

| 欄位 | 填寫內容 |
|------|---------|
| **Region** | `Taiwan` |
| **Channel icon** | 上傳步驟 5.4 準備好的圖示 |
| **Channel name** | `好牧人` ⚠️ 不能包含 "LINE" |
| **Channel description** | `好牧人教會管理系統，提供小組牧養回報、線上奉獻、會友管理等服務` |
| **Email address** | `mengsunghu@gmail.com` |
| **Privacy policy URL** | `https://jesus.speechmessage.com.tw:807/Privacy` |
| **Terms of use URL** | （選填，可先不填）|
| **Service company's country/region** | `Taiwan` |

```
Step 6：勾選所有同意條款
        □ LINE Developers Agreement     ✅
        □ LINE MINI App Platform Agreement  ✅
        □ LINE MINI App Policy          ✅

Step 7：點擊「Create」

Step 8：點擊「Accept」確認資料使用同意

✅ 建立成功！
```

### 6.2 記錄重要資訊

建立完成後，立刻記錄以下資訊：

```
進入新建的 Mini App Channel：

📋 Basic settings 分頁：
   ├── Channel ID:      ____________ （記下來）
   └── Channel secret:  ____________ （記下來）

📋 Web app settings 分頁（或 LIFF 分頁）：
   ├── Developing LIFF ID:  ____________ （記下來）
   ├── Review LIFF ID:      ____________ （記下來）
   └── Published LIFF ID:   ____________ （記下來）
```

### 6.3 設定 Endpoint URL

在 Console 的「Web app settings」分頁，為三個環境設定 Endpoint URL：

```
Developing 環境：
  Endpoint URL: https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/MINI_APP_DEV_LIFF_ID
  ← 把 MINI_APP_DEV_LIFF_ID 替換成 Developing 的 LIFF ID

Review 環境：
  Endpoint URL: https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/MINI_APP_REVIEW_LIFF_ID
  ← 把 MINI_APP_REVIEW_LIFF_ID 替換成 Review 的 LIFF ID

Published 環境：
  Endpoint URL: https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/MINI_APP_PUBLISHED_LIFF_ID
  ← 把 MINI_APP_PUBLISHED_LIFF_ID 替換成 Published 的 LIFF ID
```

> 💡 **為什麼指向 `LineIdLoginView`？**  
> 因為這是我們的 LINE 登入主頁，已經有完整的 LIFF 初始化 + 環境偵測 + 自動導向邏輯。  
> Mini App 開啟時會自動走過這個流程，然後導向對應的功能頁面。

### 6.4 加入測試人員

```
Console → Mini App Channel → 「Roles」分頁 → 「Add Tester」

加入以下人員為 Tester（用 LINE User ID 或掃 QR Code）：
  ├── 你自己（開發者）
  ├── 牧師 / 教會同工（內部測試）
  └── 其他開發夥伴

⚠️ 在 Developing 和 Review 環境中，只有 Tester 才能存取！
```

---

## 第七章：appsettings.json 新增設定

### 7.1 新增 MiniApp 設定區段

在 `appsettings.json` 的 `Liff` 區段後面，新增 `MiniApp` 區段：

```jsonc
// ==============================================
// ✅ LINE Mini App 設定 (LINE Mini App Configuration)
// ==============================================
// 用於 LINE Mini App Channel 的三個環境
// 請在 LINE Developers Console 建立 Mini App Channel 後填入
"MiniApp": {
    // Mini App Channel 基本資訊
    "ChannelId": "在Console取得後填入",           // Mini App Channel ID
    "ChannelSecret": "在Console取得後填入",       // Mini App Channel Secret
    
    // 三個環境的 LIFF ID
    "DevelopingLiffId": "在Console取得後填入",    // Developing 環境 LIFF ID
    "ReviewLiffId": "在Console取得後填入",        // Review 環境 LIFF ID  
    "PublishedLiffId": "在Console取得後填入",     // Published 環境 LIFF ID
    
    // 當前使用的環境（切換用）
    // 可選值："Developing", "Review", "Published"
    "ActiveEnvironment": "Developing",
    
    // Mini App Endpoint 基底 URL
    "EndpointBaseUrl": "https://jesus.speechmessage.com.tw:807",
    
    // Privacy Policy URL（LINE 審核需要）
    "PrivacyPolicyUrl": "https://jesus.speechmessage.com.tw:807/Privacy",
    
    // Service Message 模板（通過審核後使用）
    "ServiceMessage": {
        "Enabled": false,  // 通過審核前設為 false
        "Templates": {
            "DedicationConfirmed": "dedication_confirmed_zh-TW",
            "WeeklyReminder": "weekly_reminder_zh-TW"
        }
    }
}
```

### 7.2 環境切換邏輯說明

```
appsettings.json 中的 LIFF ID 對照：

【現有的（保留不動）】
  Liff:BindingLiffId   = "1653819697-YkPyPkr6"  ← 既有 LINE Login 的 LIFF
  Liff:LoginLiffId     = "2007621061-Exd9BGv8"  ← 既有 LINE Login 的 LIFF

【新增的（Mini App 專用）】
  MiniApp:DevelopingLiffId  = "xxxxxxxxxx-xxxxxxxx"  ← Mini App Developing
  MiniApp:ReviewLiffId      = "xxxxxxxxxx-xxxxxxxx"  ← Mini App Review
  MiniApp:PublishedLiffId   = "xxxxxxxxxx-xxxxxxxx"  ← Mini App Published

切換邏輯：
  在 Controller 中根據 MiniApp:ActiveEnvironment 決定使用哪個 LIFF ID
  TempData["Proponent"] = 對應環境的 LIFF ID
```

---

## 第八章：建立 Mini App Middleware（中間件）

### 8.1 建立 Mini App 環境偵測中間件（選用）

建立一個中間件來偵測請求是否來自 LINE Mini App：

**檔案路徑**：`ChurchReport/Middleware/MiniAppDetectionMiddleware.cs`

```csharp
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// LINE Mini App 環境偵測中間件
    /// 偵測請求是否來自 LINE LIFF Browser，
    /// 並將偵測結果存入 HttpContext.Items 供後續使用。
    /// </summary>
    public class MiniAppDetectionMiddleware
    {
        private readonly RequestDelegate _next;

        public MiniAppDetectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 偵測 User-Agent 是否包含 LINE 相關標識
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            
            // LINE LIFF Browser 的 User-Agent 包含 "Line" 或 "LIFF"
            var isLineBrowser = userAgent.Contains("Line/") || 
                                userAgent.Contains("LIFF");
            
            // 將偵測結果存入 HttpContext.Items
            context.Items["IsLineMiniApp"] = isLineBrowser;
            context.Items["UserAgent"] = userAgent;

            await _next(context);
        }
    }
}
```

### 8.2 建立隱私政策頁面 Action

在 `AuthenticationController` 或建立新的 Controller，加入隱私政策 Action：

```csharp
/// <summary>
/// 隱私政策頁面（LINE Mini App 審核需要）
/// </summary>
[HttpGet]
[Route("/Privacy")]
public IActionResult Privacy()
{
    return View();
}
```

建立對應的 View：`Views/Authentication/Privacy.cshtml`

---

## 第九章：改造現有 .cshtml 頁面

### 9.1 通用改動：建立 Safe Area CSS

**建立檔案**：`wwwroot/css/mini-app-safe-area.css`

```css
/* ====================================================================
   LINE Mini App Safe Area 適配
   ====================================================================
   LINE Mini App 在 LIFF Browser 中執行時，頂部有 LINE 的 Header，
   底部某些裝置有 Home Indicator。需要用 CSS env() 來處理安全區域。
   ==================================================================== */

/* 確保內容不被 LINE Header 或 iOS 瀏海/Home Indicator 遮擋 */
html {
    /* 啟用 viewport-fit=cover 後，用 env() 取得安全區域 */
    padding-top: env(safe-area-inset-top, 0px);
    padding-bottom: env(safe-area-inset-bottom, 0px);
    padding-left: env(safe-area-inset-left, 0px);
    padding-right: env(safe-area-inset-right, 0px);
}

/* LINE Mini App Header 高度大約 44-56px，確保主要內容不被遮住 */
body.mini-app-mode {
    /* 為 LINE Header 預留空間 */
    padding-top: env(safe-area-inset-top, 0px);
}

/* 底部固定元素需要額外的 padding */
.mini-app-footer-safe {
    padding-bottom: calc(env(safe-area-inset-bottom, 0px) + 8px);
}

/* 確保 DevExtreme LoadPanel 在 Safe Area 內 */
.dx-loadpanel-wrapper {
    top: env(safe-area-inset-top, 0px) !important;
}

/* Toast 通知避開 LINE Header */
.dx-toast-wrapper {
    top: calc(env(safe-area-inset-top, 0px) + 60px) !important;
}
```

### 9.2 頁面 ①：LineIdLoginView.cshtml（Mini App 主入口）

**改動範圍**：極小

**需要修改的地方：**

```html
<!-- 1. <head> 中加入 viewport-fit=cover -->
<!-- 原本 -->
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1" />
<!-- 改為 -->
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, viewport-fit=cover" />

<!-- 2. 引入 Safe Area CSS -->
<link href="~/css/mini-app-safe-area.css" rel="stylesheet" />

<!-- 3. LIFF SDK 版本確認（保持使用 edge/2 即可） -->
<script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>
<!-- ✅ 已經是最新版，不需要改 -->
```

**JavaScript 部分不需要改**，因為：
- `liff.init()` 已經有了 ✅
- `detectEnvironmentAndChooseMethod()` 已經有了 ✅  
- `liff.getProfile()` 已經有了 ✅
- `UpdateLineUserId()` AJAX 已經有了 ✅

**唯一需要確認的是 LIFF ID**：
```javascript
// 這行已經是動態從 TempData 取得，不需要改
liff.init({ liffId: '@TempData["Proponent"]' })
// Controller 會根據環境設定正確的 LIFF ID ✅
```

### 9.3 頁面 ②：LineLiffView.cshtml（綁定註冊頁）

**改動範圍**：極小

```html
<!-- 1. 加入 viewport-fit=cover -->
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">

<!-- 2. 引入 Safe Area CSS -->
<link href="~/css/mini-app-safe-area.css" rel="stylesheet" />
```

### 9.4 頁面 ③：Login.cshtml（帳號密碼登入）

**改動範圍**：小

```html
<!-- 1. 加入 viewport-fit=cover -->
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, viewport-fit=cover" />

<!-- 2. 引入 Safe Area CSS -->
<link href="~/css/mini-app-safe-area.css" rel="stylesheet" />

<!-- 3. 加入「從 Mini App 返回」的導航（選用） -->
<!-- 在登入頁底部加入返回 Mini App 的按鈕 -->
<script>
    // 如果是從 Mini App 進來的，顯示「返回」按鈕
    if (typeof liff !== 'undefined') {
        try {
            if (liff.isInClient()) {
                // 顯示關閉 Mini App 的按鈕
                document.getElementById('miniAppBackBtn').style.display = 'block';
            }
        } catch(e) {}
    }
</script>
```

### 9.5 頁面 ④：DediationLineLoginView.cshtml（奉獻 LINE 登入）

**改動範圍**：極小（和 LineIdLoginView 一樣的改法）

```html
<!-- 1. viewport-fit=cover -->
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">

<!-- 2. Safe Area CSS -->
<link href="~/css/mini-app-safe-area.css" rel="stylesheet" />
```

### 9.6 頁面 ⑤：DedicationFeeView.cshtml（奉獻紀錄查詢）

**改動範圍**：中等

```html
<!-- 1. viewport-fit=cover（目前這頁的 <head> 比較簡陋，需要補完） -->
<head>
    <meta charset="UTF-8">
    <meta http-equiv="X-UA-Compatible" content="IE=edge">
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
    
    <!-- 加入 Safe Area CSS -->
    <link href="~/css/mini-app-safe-area.css" rel="stylesheet" />
    
    <link rel="shortcut icon" href="~/favicon.ico" type="image/x-icon">
    <meta name="description" content="好牧人奉獻紀錄查詢">
    <title>好牧人 - 奉獻紀錄</title>
</head>
```

### 9.7 頁面 ⑥：_GeneralGroupGrids.cshtml（小組牧養網格）

**改動範圍**：主要是 DataGrid 在手機上的顯示優化

```
這是 Partial View（嵌入在 IntegrateView / MultiGroupView 中），
它本身不需要 <head> 和 viewport 設定。

需要注意的是：
  • DataGrid 在 LIFF Browser（手機寬度）中需要適當的欄位隱藏策略
  • ColumnHidingEnabled(true) 已經設了 ✅
  • Scrolling → Virtual 已經設了 ✅
  
可能需要額外調整的：
  • 在小螢幕時隱藏較不重要的欄位
  • 確保 DataGrid 的 touch scrolling 正常運作
```

### 9.8 所有頁面的共同改動總結

```
所有涉及的 .cshtml 頁面都需要做的事：

1. ✅ viewport 加上 viewport-fit=cover
   <meta name="viewport" content="..., viewport-fit=cover">

2. ✅ 引入 Safe Area CSS
   <link href="~/css/mini-app-safe-area.css" rel="stylesheet" />

3. ✅ LIFF SDK 保持 edge/2（不需要改版本）
   <script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>

4. ✅ LIFF ID 已經是從 TempData 動態取得（不需要改）
   liff.init({ liffId: '@TempData["Proponent"]' })

就這樣！核心程式碼幾乎不用改！
```

---

## 第十章：Controller 後端改造

### 10.1 AuthenticationController 改造

需要讓 `LineIdLoginView` Action 支援 Mini App 的 LIFF ID：

**檔案**：`AuthenticationController.LineLogin.cs`

```csharp
/// <summary>
/// LINE ID 登入視圖
/// ✅ 改造重點：支援 Mini App 環境的 LIFF ID 自動選擇
/// </summary>
[HttpGet]
[Route("/Authentication/LineIdLoginView/{LineIdLoginViewPatameter}")]
public IActionResult LineIdLoginView(string LineIdLoginViewPatameter)
{
    try
    {
        var images = BuildHeroImages(
            "~/assets/images/church-001.jpg",
            "~/assets/images/church-002.jpg"
        );

        InMemoryContext.LineBindingViewModel.Images = images;
        
        // ✅ Mini App 支援：
        // 如果傳入的參數是 Mini App 的 LIFF ID，直接使用
        // 如果是其他參數，保持原有邏輯
        TempData["Proponent"] = LineIdLoginViewPatameter;

        return View(InMemoryContext.LineBindingViewModel);
    }
    catch (Exception e)
    {
        return HandleError(e, "LineIdLoginView");
    }
}
```

> 💡 **好消息**：因為你的 `TempData["Proponent"]` 已經是從路由參數動態帶入的，  
> 所以當 Mini App 的 Endpoint URL 包含正確的 LIFF ID 時，自動就會生效！  
> **不需要改任何後端程式碼！**

### 10.2 新增 Privacy Policy Action

**建議在 `AuthenticationController.Core.cs` 中新增：**

```csharp
/// <summary>
/// 隱私政策頁面（LINE Mini App 審核必備）
/// </summary>
[HttpGet]
[Route("/Privacy")]
[AllowAnonymous]  // 不需要登入就能存取
public IActionResult Privacy()
{
    return View("Privacy");
}
```

### 10.3 新增 Mini App 用的 Helper 方法（選用）

```csharp
/// <summary>
/// 取得當前 Mini App 環境的 LIFF ID
/// 根據 appsettings.json 中 MiniApp:ActiveEnvironment 的設定回傳對應 ID
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

---

## 第十一章：Safe Area 與 UI 適配

### 11.1 LINE Mini App 的畫面結構

```
┌────────────────────────────────────────┐
│ ┌────────────────────────────────────┐ │ ← iOS 狀態列 (電池、時間、信號)
│ │     LINE Mini App Header           │ │ ← LINE 提供的 Header
│ │  [← 返回]  好牧人  [⋮ 選單]        │ │    (約 44-56px 高)
│ ├────────────────────────────────────┤ │
│ │                                    │ │
│ │                                    │ │
│ │       你的 Web App 內容             │ │ ← 這是你的 cshtml 渲染區域
│ │       (LIFF Browser Body)          │ │
│ │                                    │ │
│ │    ┌────────────────────────┐      │ │
│ │    │  Hero 圖片輪播          │      │ │
│ │    └────────────────────────┘      │ │
│ │    ┌────────────────────────┐      │ │
│ │    │  聯絡資訊卡片           │      │ │
│ │    └────────────────────────┘      │ │
│ │                                    │ │
│ │                                    │ │
│ ├────────────────────────────────────┤ │
│ │      LINE Mini App Footer          │ │ ← LINE 提供的 Footer
│ │  [分享]  [加到主畫面]              │ │    (含分享按鈕等)
│ └────────────────────────────────────┘ │
│ ▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬ │ ← iOS Home Indicator
└────────────────────────────────────────┘

⚠️ 注意事項：
  • LINE Header 和 Footer 是 LINE 自動加上的，你不需要做
  • 你的 <body> 內容會顯示在中間的區域
  • 需要用 CSS env(safe-area-inset-*) 避免內容被遮住
  • 你原本的 footer-note (Copyright) 需要額外 padding
```

### 11.2 Hero 區域高度調整建議

```
目前 LineIdLoginView.cshtml 的 Hero 高度約 300-340px，
在 Mini App 中因為上方多了 LINE Header，
建議在 mini-app-safe-area.css 中加入：

/* 在 LIFF Browser 中適當縮小 Hero 高度 */
@media (max-height: 700px) {
    .hero-wrapper {
        height: 220px !important;
        min-height: 180px;
    }
}
```

---

## 第十二章：本地開發測試流程

### 12.1 使用 ngrok 進行本地測試

```bash
# 1. 啟動你的 ASP.NET Core 專案（在 Visual Studio 中按 F5）
#    預設跑在 https://localhost:807 或 http://localhost:5000

# 2. 啟動 ngrok 隧道
ngrok http https://localhost:807
# 或（如果你的本地是 HTTP）
ngrok http 5000

# 3. ngrok 會顯示：
#    Forwarding  https://abc123def456.ngrok.io -> https://localhost:807
#    把這個 HTTPS 網址複製起來

# 4. 到 LINE Developers Console，更新 Developing 環境的 Endpoint URL：
#    https://abc123def456.ngrok.io/Authentication/LineIdLoginView/你的LIFF_ID

# ⚠️ 注意：每次重啟 ngrok，URL 會變，需要重新更新 Console
```

### 12.2 直接使用正式伺服器測試（推薦）

```
因為你已經有 HTTPS 的正式伺服器 (jesus.speechmessage.com.tw:807)，
最簡單的做法是：

1. 把程式碼部署到正式伺服器
2. 在 Console 設定 Developing 的 Endpoint URL 指向正式伺服器
3. 只有 Tester 才能存取 Developing 環境，不會影響其他人

Developing Endpoint URL:
  https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Developing的LIFF_ID}

這樣就不需要 ngrok，也不用擔心 URL 變動的問題！
```

### 12.3 測試步驟

```
測試清單：

1. 基本功能測試
   □ 在手機 LINE App 中開啟 Mini App → 看到登入頁面
   □ LIFF 初始化成功（Console 無錯誤）
   □ 自動取得 Profile（顯示教友名稱）
   □ 已綁定教友 → 自動導向 IntegrateView 或 MultiGroupView
   □ 未綁定教友 → 導向 LineLiffView 綁定頁面

2. 奉獻流程測試
   □ 開啟奉獻入口 → LIFF 登入成功
   □ 導向 QPayView → 顯示奉獻表單
   □ 金流（永豐/高鉅/台新）流程正常
   □ 奉獻紀錄查詢正常

3. UI 適配測試
   □ Hero 圖片在 LIFF Browser 中正常顯示
   □ 內容不被 LINE Header 遮住
   □ Footer 不被 iOS Home Indicator 遮住
   □ DataGrid 在手機寬度下可正常操作
   □ Toast / LoadPanel 顯示位置正確

4. 多平台測試
   □ iOS LINE App ✅
   □ Android LINE App ✅
   □ 電腦版 LINE → 偵測到電腦版 → 使用 Server-side OAuth ✅

5. 錯誤情境測試
   □ 網路斷線 → 顯示友善錯誤訊息
   □ LIFF 初始化失敗 → fallback 到 OAuth
   □ Session 過期 → 正確導向重新登入
```

---

## 第十三章：三個環境部署

### 13.1 部署流程

```
Step 1：開發與測試（Developing）
         │
         │  確認功能都正常
         │
         ▼
Step 2：部署到穩定環境（Review）
         │
         │  設定 Review 環境的 Endpoint URL
         │  確保 LINE 審核員可以正常測試
         │
         ▼
Step 3：提交審核申請
         │
         │  等待 LINE 審核（數個工作天）
         │
         ├─── 審核不通過 → 修改 → 重新部署 Review → 重新提交
         │
         ▼
Step 4：審核通過 ✅
         │
         │  設定 Published 環境的 Endpoint URL
         │
         ▼
Step 5：正式上線 🎉
```

### 13.2 好牧人的實際部署設定

因為我們只有一台主要伺服器，三個環境的 Endpoint URL 都指向同一台，  
差別只在路由參數中的 LIFF ID：

```
Developing:
  https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Developing_LIFF_ID}

Review:
  https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Review_LIFF_ID}

Published:
  https://jesus.speechmessage.com.tw:807/Authentication/LineIdLoginView/{Published_LIFF_ID}
```

### 13.3 appsettings.json 環境切換

```jsonc
// 開發測試時
"MiniApp": {
    "ActiveEnvironment": "Developing"   // ← 使用 Developing 的 LIFF ID
}

// 送審時
"MiniApp": {
    "ActiveEnvironment": "Review"       // ← 使用 Review 的 LIFF ID
}

// 正式上線時
"MiniApp": {
    "ActiveEnvironment": "Published"    // ← 使用 Published 的 LIFF ID
}
```

---

## 第十四章：提交審核上線

### 14.1 審核前的完整 Checklist

```
【必要條件 - 缺一不可】
  □ Channel icon 已上傳（500x500 PNG，好牧人 LOGO）
  □ Channel name = "好牧人"（不含 "LINE"）
  □ Channel description 清楚說明服務
  □ Privacy policy URL 可公開存取
  □ Review 環境 Endpoint URL 已設定且可存取
  □ 頁面在 LIFF Browser 中正常載入（3秒內）
  □ 核心功能可正常操作（登入、小組回報、奉獻）

【審核員測試帳號】
  □ 準備一個測試用的教友帳號（已綁定 LINE）
  □ 記錄測試步驟（給審核員看）：
    1. 開啟 Mini App
    2. 自動 LINE 登入
    3. 看到小組管理頁面
    4. 可以操作 DataGrid
    5. 可以導向奉獻頁面

【品質要求】
  □ 無 JavaScript console 錯誤
  □ 所有圖片正常載入
  □ 手機直向和橫向都能正常顯示
  □ 載入畫面有 loading 提示（不會出現白屏）
```

### 14.2 提交步驟

```
1. 登入 LINE Developers Console
2. 進入好牧人 Mini App Channel
3. 點擊「Submit for review」
4. 填寫審核說明：

   服務說明：
   好牧人是一個教會管理系統，提供會友小組牧養回報、
   線上奉獻、個人資料管理等功能。教友可以透過 LINE 登入，
   直接在 LINE 中管理小組事務和進行線上奉獻。

   主要功能：
   1. LINE 帳號綁定與自動登入
   2. 小組牧養回報（出席、探訪、代禱事項）
   3. 線上奉獻（支援永豐QPay/高鉅MyPay/台新TSPG）
   4. 奉獻紀錄查詢
   5. 個人資料維護

   測試步驟：
   1. 開啟 Mini App 後，系統自動進行 LINE 登入
   2. 登入成功後，自動導向小組管理頁面
   3. 可在 DataGrid 中查看和編輯小組成員資料
   4. 點擊奉獻功能，可進入線上奉獻流程

5. 提交等待審核
```

### 14.3 審核可能的退件原因與對策

| 退件原因 | 對策 |
|---------|------|
| 隱私政策頁面無法存取 | 確認 `/Privacy` 路由正常，HTTPS 無錯誤 |
| 載入速度太慢 | 優化圖片大小、啟用 Brotli 壓縮（已有）|
| 頁面有白屏或錯誤 | 檢查 Console 日誌、確認所有靜態資源可存取 |
| 功能無法正常使用 | 準備測試帳號、確認 CRM 連線正常 |
| Channel name 包含 "LINE" | 修改名稱 |
| 不符合 Mini App Policy | 檢閱 https://terms2.line.me/LINE_MINI_App?lang=en |

---

## 第十五章：常見問題排查

### 15.1 LIFF 初始化錯誤

```
問題：liff.init() 失敗，顯示 INVALID_ARGUMENT
原因：LIFF ID 填錯了
解決：
  1. 確認 Console 中 Mini App Channel 的 LIFF ID
  2. 確認 Endpoint URL 中的 LIFF ID 與 liff.init() 使用的一致
  3. ⚠️ Mini App 的 LIFF ID ≠ LINE Login 的 LIFF ID！
     Mini App 有自己的三組 LIFF ID
```

### 15.2 教友的 userId 對不上

```
問題：教友之前綁定的 userId 和 Mini App 取到的不一樣
原因：不同 Provider 下的 User ID 不同
解決：
  ⭐ 最重要的一點：Mini App Channel 必須建在
     和原有 LINE Login Channel 同一個 Provider 下！
  
  如果已經建錯 Provider，需要：
  1. 刪除建錯的 Channel
  2. 在正確的 Provider 下重新建立
  3. 教友不需要重新綁定（因為同 Provider = 同 userId）
```

### 15.3 電腦版 LINE 無法開啟

```
問題：電腦版 LINE 開啟 Mini App 後白屏或錯誤
原因：電腦版 LINE 的 LIFF Browser 限制
解決：
  你的程式碼已經有 detectEnvironmentAndChooseMethod() ✅
  它會偵測到電腦版環境，自動切換到 Server-side OAuth。
  
  確認 LineLogin 設定正確：
  appsettings.json:
    "LineLogin": {
        "ChannelId": "2007621061",
        "CallbackUrl": "https://jesus.speechmessage.com.tw:807/Authentication/LineCallback"
    }
```

### 15.4 DevExtreme 元件在 LIFF Browser 中樣式異常

```
問題：DataGrid / Gallery / Toast 在 LIFF Browser 中顯示異常
原因：LIFF Browser 的 CSS 相容性問題
解決：
  1. 確保 DevExtreme CSS 載入順序正確：
     dx.common.css → dx.light.css → 你的 CSS
  2. 避免 CSS 衝突：
     LINE 會注入自己的 CSS，可能影響你的樣式
     使用更具體的 CSS selector 來覆蓋
  3. DataGrid 觸控滑動：
     確認 Scrolling → Virtual 模式已啟用（你已經有了 ✅）
```

### 15.5 Session 在 Mini App 中失效

```
問題：頁面跳轉後 Session 丟失，需要重新登入
原因：Cookie SameSite 設定問題
解決：
  你的 Startup.cs 已經設定了 ✅：
    options.Cookie.SameSite = SameSiteMode.Lax;
  
  如果還是有問題，確認：
  1. HTTPS 正常運作（Cookie SecurePolicy = Always）
  2. Cookie Domain 設定正確
  3. LIFF Browser 不會清除 Cookie
```

---

## 附錄A：完整 Checklist

```
═══════════════════════════════════════════════════════
 好牧人 LINE Mini App 導入 - 完整 Checklist
═══════════════════════════════════════════════════════

Phase 0：申請許可（台灣地區必要）
  □ 向 LINE Taiwan 申請 LINE Mini App 開發許可
  □ 等待審核通過（1-4 週）

Phase 1：Console 設定（1-2 天）
  □ 登入 LINE Developers Console
  □ 在同一個 Provider 下建立 LINE Mini App Channel
  □ 填寫 Channel 資訊（名稱、描述、圖示）
  □ 記錄 Channel ID / Secret
  □ 記錄三組 LIFF ID（Developing / Review / Published）
  □ 設定 Developing 環境 Endpoint URL
  □ 加入自己和測試人員為 Tester

Phase 2：後端設定（1 天）
  □ appsettings.json 新增 MiniApp 區段
  □ 填入 Mini App Channel ID / Secret / LIFF IDs
  □ 建立 Privacy Policy 頁面和路由
  □ （選用）建立 MiniAppDetectionMiddleware

Phase 3：前端改造（1-2 天）
  □ 建立 wwwroot/css/mini-app-safe-area.css
  □ 建立 wwwroot/loading.gif
  □ LineIdLoginView.cshtml：加 viewport-fit=cover + Safe Area CSS
  □ LineLiffView.cshtml：加 viewport-fit=cover + Safe Area CSS
  □ Login.cshtml：加 viewport-fit=cover + Safe Area CSS
  □ DediationLineLoginView.cshtml：加 viewport-fit=cover + Safe Area CSS
  □ DedicationFeeView.cshtml：加 viewport-fit=cover + Safe Area CSS
  □ _GeneralGroupGrids.cshtml：確認手機版顯示正常

Phase 4：測試（3-5 天）
  □ iOS LINE App 測試
  □ Android LINE App 測試
  □ 電腦版 LINE 測試（Server-side OAuth）
  □ 登入流程完整測試
  □ 綁定流程完整測試
  □ 小組管理功能測試
  □ 奉獻流程完整測試
  □ 奉獻紀錄查詢測試
  □ 錯誤情境測試

Phase 5：送審（3-7 個工作天）
  □ 部署到 Review 環境（設定 Endpoint URL）
  □ 確認 Channel 資訊完整
  □ 確認隱私政策可存取
  □ 準備審核說明文件
  □ 準備測試帳號
  □ 提交審核

Phase 6：上線
  □ 審核通過！
  □ 設定 Published 環境 Endpoint URL
  □ appsettings.json 切換 ActiveEnvironment = "Published"
  □ （選用）設定 Custom Path
  □ 正式環境最終測試
  □ 🎉 通知教友可以開始使用！

═══════════════════════════════════════════════════════
```

---

## 附錄B：名詞解釋

| 名詞 | 英文 | 解釋 |
|------|------|------|
| LIFF | LINE Front-end Framework | LINE 前端框架，讓網頁可以和 LINE App 互動 |
| LIFF ID | — | 辨識你的 LIFF App 的唯一 ID，格式如 `1234567890-AbCdEfGh` |
| LIFF Browser | — | LINE App 內建的瀏覽器，用來顯示 LIFF App / Mini App |
| Channel | — | 在 LINE Developers Console 中代表一個服務的設定單位 |
| Provider | — | Channel 的上層組織，代表公司/品牌。同 Provider 的 userId 一致 |
| Endpoint URL | — | LINE 開啟你的 Mini App 時，實際載入的網頁 URL |
| Safe Area | — | iOS 的安全區域，避開瀏海和 Home Indicator 的範圍 |
| Verified Mini App | — | 通過 LINE 審核的 Mini App，擁有完整功能 |
| Unverified Mini App | — | 還沒通過審核的 Mini App，功能受限，只有 Tester 可用 |
| Service Message | — | LINE Mini App 的推播通知功能，不需加官方帳號好友 |
| Custom Path | — | 自訂短網址，如 `miniapp.line.me/good-shepherd` |
| OAuth 2.0 | — | 授權機制。我們用它讓電腦版 LINE 用戶也能登入 |
| Channel Access Token | — | 後端呼叫 LINE API 時的身份憑證 |
| viewport-fit=cover | — | CSS 設定，讓網頁延伸到安全區域之外，再用 env() 控制邊界 |

---

## 📎 參考資料

| 資源 | 網址 |
|------|------|
| LINE Mini App 官方文件 | https://developers.line.biz/en/docs/line-mini-app/ |
| LINE Mini App 快速入門 | https://developers.line.biz/en/docs/line-mini-app/quickstart/ |
| LINE Mini App API 參考 | https://developers.line.biz/en/reference/line-mini-app/ |
| LIFF SDK 參考 | https://developers.line.biz/en/reference/liff/ |
| LINE Mini App 開發指南 | https://developers.line.biz/en/docs/line-mini-app/develop/develop-overview/ |
| 現有 Web App 轉 Mini App | https://developers.line.biz/en/docs/line-mini-app/develop/web-to-mini-app/ |
| LINE Mini App Policy | https://terms2.line.me/LINE_MINI_App?lang=en |
| LINE Taiwan 申請 | https://tw.linebiz.com/service/other-solutions/line-mini-app/ |
| LINE Developers Console | https://developers.line.biz/console/ |

---

*文件版本：v1.0*  
*建立日期：2025 年 7 月*  
*專案：ChurchReport 好牧人教會管理系統*  
*分支：Jesus_5.0.4.LineMiniApp*  
*作者：GitHub Copilot 自動產生，需經開發者確認*
