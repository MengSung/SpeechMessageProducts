# 版本架構圖 — 1.0.0.1 / 1.0.0.2 / 1.0.0.3

用途：解釋三個版本分支在「Dynamics 365 連線架構」上的差異，以及 `Gateway` 與 `Embedded`
這兩個名詞到底代表什麼。

繪製日期：2026-08-17　　繪製時所在分支：`1.0.0.2.IsolateConnector`（HEAD `8571562c`）

---

## 檔案清單

| 檔案 | 內容 |
| --- | --- |
| `版本架構比較.drawio` | 可編輯原始檔，6 頁，對應下方 6 張 PNG。用 draw.io / diagrams.net 或 VS Code 的 Draw.io Integration 擴充開啟。 |
| `01-版本演進地圖.png` | 分支拓樸（誰從誰長出來）＋ 三版本各自做了什麼 |
| `02-v1.0.0.1-起點架構.png` | 連線器和產品綁在一起的原始狀態，以及它的 4 個問題 |
| `03-v1.0.0.2-抽出連線器.png` | 7 個新專案、允許的參考方向、呼叫契約、三道守門機制、目前卡點 |
| `04-Gateway與Embedded的差別.png` | **最重要的一張**。逐項對照兩種模式哪些相同、哪些不同 |
| `05-v1.0.0.3-子行程模型.png` | ControlPlane + Worker 子行程架構，以及相對 1.0.0.2 改了哪三件事 |
| `06-同一動作三版路徑對照.png` | 同一個「查奉獻費用」動作，三版各走幾道關卡、跨幾次行程 |

建議閱讀順序：01 → 02 → 03 → **04** → 05 → 06。

---

## 三句話版本

- **1.0.0.1** — 連線器住在產品裡。ChurchReport 自己抱 CRM SDK、自己存密碼、自己開一個
  singleton 連線池。任何一行程式碼都能組任意 FetchXML 送出去。
- **1.0.0.2** — 連線器搬到隔壁專案，但還住同一棟樓。抽成 7 個 .NET 10 專案，改走純 HTTP
  OData v4，加上能力白名單與容量控管，但真正打 CRM 的動作仍在同一個行程內。
- **1.0.0.3** — 危險的舊 SDK 搬去另一棟樓，用對講機溝通。承認 CE 8.2 IFD 只有 SOAP 走得通，
  於是把官方 CrmServiceClient 關進 net48 的獨立 worker 子行程，用 `WorkerProtocol` 溝通。

---

## Gateway 與 Embedded 的正確理解

它們**不是兩個版本，也不是兩套系統**，而是同一份連線器程式碼
（`SpeechMessage.Dynamics.WebApi`）的兩個「住處」：

| | Gateway 模式 | Embedded 模式 |
| --- | --- | --- |
| 連線器程式碼 | 同一個專案 | 同一個專案 |
| 呼叫契約 / 能力白名單 / 容量計畫 | 相同 | 相同 |
| 執行位置 | 獨立行程、可獨立機器 | 產品自己的行程內 |
| 網路跳躍 | 產品 → Gateway 多一次 HTTP | 無 |
| CRM 憑證位置 | 只在 Gateway | 在產品行程裡 |
| 典型用途 | 正式環境預設，5～10 產品共用 | VS 本機除錯 / 刻意隔離的單機部署 |

設計 SPEC 的原句：*"This changes the host location, not the connector/security contract."*

模式在**啟動時**由部署的 JSON（`DynamicsAccess:ExecutionMode`）決定，之後定案；
不能由使用者、LINE ID、瀏覽器 session 或請求欄位在執行期切換。

---

## 目前狀態（1.0.0.2 分支）

- `DynamicsAccess:Package01FeeReadsEnabled` = **`false`** — 新路徑已接好線但開關是關的，
  正式查詢仍走舊的 ToolUtility / SOAP。
- 卡點：目標 CE 9.1 是 IFD 環境，Web API 不能用 Windows NTLM；改走 ADFS OAuth 時
  ADFS 回 `MSIS9611`（只支援 authorization_code / refresh_token），且現有 ClientId
  未註冊在該台 on-prem ADFS。需要 ADFS 管理員先 `Add-AdfsClient` 註冊 client 與 redirect URI。
- 舊的 `PowerPlatform.Dataverse.Client` 專案仍在方案裡，此版尚未移除。

---

## 資料來源

- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` — 架構 SPEC
- `.trellis/tasks/07-23-dynamics-connection-compatibility/` — 設計、執行計畫與各階段驗證紀錄
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase3-tier-a-ifd-auth-blocker.md` — ADFS 卡點
- 1.0.0.3 分支的 `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` — Central/Local 與 8.2/9.1 決策說明
- 各 `*.csproj` 的 `ProjectReference` 與 `Description`
- `SpeechMessageProducts.ChurchReport/appsettings.json` 的 `DynamicsAccess` 區段
