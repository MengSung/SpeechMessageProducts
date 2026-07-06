# 系統安全稽核報告
## Security Vulnerability Audit & Session Leakage Audit — ChurchReport(好牧人)

---

| 項目 | 內容 |
|---|---|
| 報告版本 | v1.0 |
| 稽核日期 | 2026-07-06 |
| 稽核對象 | ChurchReport 主網站（ASP.NET Core / net10.0，DevExtreme 21.2.7，後端 Dynamics 365 CRM） |
| 稽核方式 | 白箱原始碼靜態審查（無滲透測試、無攻擊執行、無破壞性操作） |
| 稽核框架 | OWASP Top 10（2021）、OWASP ASVS |
| 稽核人 | 資深資安稽核顧問（防禦性 / 授權稽核角度） |
| 機密等級 | 內部機密（含未修補弱點位置，請勿外流） |

> **閱讀提醒**：本報告以修復為導向，僅描述「問題在哪、為什麼、怎麼修、怎麼驗證」，不含任何可操作的攻擊 payload、繞過步驟或資料竊取方法。所有結論皆以實際 `檔案:行號` 為證據。檔案路徑相對於 Web 專案根目錄 `ChurchReport/`（方案下的 `ChurchReport/ChurchReport/`）。

---

## 目錄

1. [Executive Summary（執行摘要）](#1-executive-summary執行摘要)
2. [Scope（範圍）](#2-scope範圍)
3. [Methodology（方法論）](#3-methodology方法論)
4. [Findings 總表](#4-findings-總表)
5. [Critical Findings（必須立即修復）](#5-critical-findings必須立即修復)
6. [High Risk Findings（高風險）](#6-high-risk-findings高風險)
7. [Medium / Low Risk Findings](#7-medium--low-risk-findings)
8. [Session Leakage Analysis（逐項）](#8-session-leakage-analysis逐項)
9. [Authentication & Authorization Review](#9-authentication--authorization-review)
10. [API Security Review](#10-api-security-review)
11. [Frontend Sensitive Data Exposure Review](#11-frontend-sensitive-data-exposure-review)
12. [Logging & Error Handling Review](#12-logging--error-handling-review)
13. [Recommended Remediation Plan（修復計畫）](#13-recommended-remediation-plan修復計畫)
14. [Verification Checklist（驗證清單）](#14-verification-checklist驗證清單)
15. [附錄 A：已驗證為良好的安全設計](#附錄-a已驗證為良好的安全設計)
16. [附錄 B：本次稽核實際檢視之檔案](#附錄-b本次稽核實際檢視之檔案)

---

## 1. Executive Summary（執行摘要）

整體而言，本系統在 **Session 的「基礎設施層」相當扎實**：Session／Auth Cookie 皆設定 `HttpOnly`、`Secure`（Release）、`SameSite=Lax`；全站回應 `no-store` 並加上 `Vary: Cookie`；LINE Login 採伺服器端 OAuth 2.0 且 `state` 為密碼學亂數並經驗證；資料存取一律走 CRM SDK 的參數化 `QueryExpression`（基本免疫 SQL Injection）；前端未把任何 Token 放進 `localStorage`。這些是可靠的基礎，應予維持。

然而，**真正的高風險不在「Session cookie 本身」，而在「身分憑證的處理」與「授權的執行方式」**。本次確認以下必須處理之問題：

- **機密外洩（Critical / C-1）**：`appsettings.json` 內含大量明文機密——CRM **網域管理員密碼**、LINE／金流密鑰——且此檔案已納入 git 版控與歷史，正式環境亦未覆寫。
- **憑證處理（Critical / C-2、C-3）**：使用者密碼以 **明文儲存並明文比對**；登入成功的 JSON 回應 **把密碼原封不動回傳前端**。
- **授權執行（High / H-2、H-3）**：全站 **未使用 `[Authorize]`／認證管線**（`SignInAsync` 從未被呼叫），授權改由各 Action 自行檢查 Session；屬「opt-in」而非「預設拒絕」。`/Personal/GetContactImage(sBatch)` 即因此形成 **IDOR** 破口，可越權取得任意會友照片。
- **身分可被 Header 影響（High / H-1）**：系統會從 **`Referer` 標頭** 解析 LINE User ID 當作登入身分，而 LINE User ID 對 LINE 使用者等同「密碼」。
- **Session Fixation 防護實為無效（High / H-5）**：登入／登出以 `Session.Clear() + CommitAsync()` 宣稱「重新產生 Session ID」，但此作法在 ASP.NET Core **並不會輪替 Session ID**，註解與實際行為不符。

**風險定位**：本系統遭利用的最短路徑並非「破解加密」，而是「憑證與授權設計上的缺口」。**優先處置**：(1) 立即輪替所有外洩密鑰並將機密移出版控 →(2) 停止回傳密碼、改用雜湊密碼 →(3) 補上預設拒絕的全域授權 →(4) 修正 Session ID 輪替與 Referer 身分推導。

**風險統計**

| 等級 | 數量 | 代表項目 |
|---|---|---|
| Critical | 3 | 明文機密入庫、明文密碼、密碼回傳前端 |
| High | 5 | Referer 身分、無認證管線、Personal 照片 IDOR、OAuth 開放重導、Session Fixation |
| Medium | 5 | 登出未失效、無 CSRF、例外訊息外洩、缺安全標頭、XFF 稽核 |
| Low | 4 | 敏感值入 URL、無登入節流、失效過濾器、版控雜訊 |

---

## 2. Scope（範圍）

僅限所提供之本機原始碼與設定，主體為 Web 專案 `ChurchReport/`：

- **啟動與中介層**：`Program.cs`、`Startup.cs`、`Startup.Caching.cs`、`Middleware/*`
- **設定檔**：`appsettings.json`、`appsettings.Production.json`、`appsettings.Development.json`、`web.config`
- **認證 / Session**：`Controllers/AuthenticationController/*`、`SessionAttribute.cs`、`BaseChurchController.cs`
- **資料 / 檔案 API**：`Controllers/PersonalController.ImageUpload.cs`、`Controllers/MemberInfoController.cs`、`Controllers/ApiControllers/*`
- **前端**：`Views/**/*.cshtml`（Token／敏感資料暴露、XSS 面向）

**不在範圍**：實際滲透測試、正式環境動態掃描、第三方套件 CVE 深度比對、CRM 伺服器本身組態、金流供應商端組態。

---

## 3. Methodology（方法論）

依 OWASP Top 10 / ASVS 對下列面向做靜態追蹤與資料流分析：

1. 認證與授權設計（憑證儲存、身分建立、授權執行點）
2. Session 生命週期（建立、驗證、輪替、銷毀）
3. Cookie 屬性（HttpOnly / Secure / SameSite / 名稱 / 期限）
4. 憑證 / Token 儲存與傳遞（後端、前端、URL、記錄）
5. API 越權與 IDOR（物件級授權）
6. 輸入輸出安全（SQL / Command Injection、XSS）
7. CORS / CSRF
8. 記錄與錯誤處理（敏感資訊外洩）
9. 機密管理（版控、設定檔）

**重要判讀原則**：本專案大量使用 `#if DEBUG` 與 `System.Diagnostics.Debug.WriteLine`。後者具 `[Conditional("DEBUG")]`，於 **Release 建置會被編譯器整行移除**。因此本報告嚴格區分「Release 實際行為」與「Debug-only 行為」，避免高估或低估風險。

---

## 4. Findings 總表

| ID | Severity | Category | Description | Evidence | Impact | Recommendation（摘要） |
|---|---|---|---|---|---|---|
| **C-1** | Critical | Secrets Mgmt | 明文機密寫死於設定檔且已進 git | `appsettings.json:165,182,207,246,266,296…` | CRM 網域管理員接管、金流盜刷、LINE 冒名 | 輪替全部密鑰、移至機密庫、清理 git 歷史 |
| **C-2** | Critical | AuthN Design | 密碼明文儲存 + 明文比對，無雜湊 / 無鎖定 | `AuthenticationController.Private.cs:71-81` | DB／CRM 外洩即全站帳密外洩；可暴力破解 | 改雜湊（PBKDF2 / Argon2）、加登入節流與鎖定 |
| **C-3** | Critical | Sensitive Data in Response | 登入成功 JSON 回傳使用者密碼 | `AuthenticationController.Private.cs:437-448` | 憑證外洩到前端 / 記錄 / 快取 | 從回應移除 `account` / `password` |
| **H-1** | High | Broken AuthN | 由 `Referer` 標頭推導登入身分（LINE ID） | `BaseChurchController.cs:740-765,869-889` | 身分受用戶端可控標頭影響 | 移除 Referer 身分推導，身分只來自伺服器端 Session |
| **H-2** | High | Broken Access Control | 全站無 `[Authorize]`／認證管線，授權靠各 Action 自檢 | 全域（`SignInAsync` 零出現） | 任一漏檢端點即未授權可存取 | 加預設拒絕之全域授權原則 |
| **H-3** | High | IDOR | `/Personal/GetContactImage(sBatch)` 依 GUID 回傳任意會友照片、無授權 | `PersonalController.ImageUpload.cs:498,661` | 會友照片（PII）可被越權擷取 | 比照 MemberInfo 加 `CanViewContact` 把關 |
| **H-4** | High | Open Redirect / Token Leak | OAuth 完成後導向 `{returnUrl}/{lineUserId}`，`returnUrl` 未驗證 | `AuthenticationController.LineLoginOAuth.cs:452-513` | 憑證（LINE ID）外洩至外部站、釣魚跳轉 | `Url.IsLocalUrl` 白名單，ID 不放 URL |
| **H-5** | High | Session Fixation | `Session.Clear()+CommitAsync()` 不會輪替 Session ID | `AuthenticationController.Private.cs:171-205`、`.Session.cs:42` | 登入前後 Session ID 不變，可被固定 | 登入時刪舊 cookie + 換發新 Session ID |
| **M-1** | Medium | Session Invalidation | 登出僅清資料，未 SignOut / 未輪替 cookie | `AuthenticationController.Session.cs:30-65` | 登出後 cookie 仍有效可被重用 | 登出時 `SignOutAsync` + 刪除 cookie |
| **M-2** | Medium | CSRF | 無防偽 Token，僅靠 SameSite=Lax | 全域（`ValidateAntiForgeryToken` 僅 1 處） | 同站 / 子網域 / GET 型 CSRF 未防 | 全域啟用 Antiforgery，狀態變更端點驗證 |
| **M-3** | Medium | Info Disclosure | 例外訊息 `exception.Message` 回傳前端 | `BaseChurchController.cs:350-364`、`PersonalController.ImageUpload.cs:446,466` | 洩漏 CRM／內部細節輔助攻擊 | 回傳一般化訊息，詳情僅入伺服器記錄 |
| **M-4** | Medium | Security Headers | 缺 HSTS / HttpsRedirection / X-Frame-Options / CSP / Referrer-Policy | `Startup.cs:708-739` | Clickjacking、降級、Referer 外洩 | 補齊安全標頭，尤其 `Referrer-Policy` |
| **M-5** | Medium | Audit Integrity | 由可偽造的 `X-Forwarded-For` 取信任 IP | `SessionValidationMiddleware.cs:213-233` | 稽核 IP 可被偽造 | 只信任已知反向代理鏈 |
| **L-1** | Low | Data in URL | LINE ID / 帳密路徑出現在 URL / query | `Views/.../*LineUserId*`、`SmallGroupReportView.cshtml:82` | 進入瀏覽器歷史 / 記錄 / Referer | 敏感值改走 POST body |
| **L-2** | Low | Brute Force | 登入無速率限制 / 帳號鎖定 | `AuthenticationController.Login.cs:66` | 線上暴力破解 | 加節流、鎖定、稽核 |
| **L-3** | Low | Dead / Broken Filter | `CheckSessionOutAttribute` 為 `async override void`，實質無效 | `SessionAttribute.cs:26-71` | 誤以為有防護 | 移除或以正確 Filter 重寫 |
| **L-4** | Low | Repo Hygiene | `.suo`、`NuGet.config.bak` 等入庫 | 方案根目錄 | 資訊 / 雜訊 | 加入 `.gitignore` |

---

## 5. Critical Findings（必須立即修復）

### C-1 明文機密寫死於設定檔且已納入版控

- **Severity**：Critical
- **Category**：Secrets Management / Configuration
- **Description**：`appsettings.json` 直接以明文保存多組高敏感憑證，且未被 `.gitignore` 排除、存在於 git 提交歷史、正式環境 `appsettings.Production.json` 未覆寫（即正式站實際使用這些明文）。
- **Evidence**：
  - CRM 網域管理員密碼：`"Password": "hu9840"`（`appsettings.json:246`），使用者 `"Username": "SPEECHMESSAGE\\Administrator"`（`:245`）—— **Windows 網域管理員** 等級。
  - `LineLogin:ChannelSecret`（`:182`）、`MiniApp:ChannelSecret`（`:207`）、`LinePay:ChannelSecret`（`:266`）、`ChannelAccessToken`（`:165,169`）。
  - 金流密鑰：Sinopac `A1/A2/B1/B2/XKeyID`（`:296,334-341`）、MyPay `Key`（`:307,365`）、TSPG（`:493-511`）。
  - `git log --all -- ChurchReport/appsettings.json` 顯示多筆歷史提交；`.gitignore` 無對應規則。
- **Impact（避免攻擊細節）**：任何能取得原始碼、儲存庫、備份或發佈檔者，即可取得 CRM 網域管理員權限（等同接管會友資料庫）、以商店密鑰進行金流相關操作、以 LINE 密鑰冒名推播。屬全系統淪陷等級。
- **Recommendation**：
  1. **立即輪替** 上述所有密碼／密鑰／Token（一律視為已洩漏）。
  2. 機密移至環境變數 / .NET User-Secrets / 雲端 KeyVault；`appsettings.json` 僅留佔位符。
  3. 以 `git filter-repo`（或 BFG）**清除歷史** 中的機密後強制更新，並通知所有 clone 者重新拉取。
  4. CRM 連線改用 **最小權限服務帳號**，勿使用網域 Administrator。
- **Safe Verification**：CI 導入機密掃描（gitleaks / trufflehog），對 `appsettings*.json` 檢查不得含已知欄位與高熵字串；確認正式環境改由環境變數注入，且啟動記錄不得印出機密。

### C-2 密碼明文儲存與明文比對

- **Severity**：Critical
- **Category**：Authentication Design
- **Description**：登入以 CRM 聯絡人欄位 `new_app_pass` 保存密碼，且以字串直接比對；全程無雜湊、無 salt、無失敗鎖定。
- **Evidence**：`AuthenticationController.Private.cs:71-81`——讀取 `new_app_pass` 後 `if (storedPassword != viewModel.Password) return (false, "", "密碼錯誤");`
- **Impact**：CRM 或其備份一旦外洩，等同所有 App 使用者密碼明文外洩；亦使 C-3（回傳密碼）成為可能；並可對登入端點做線上暴力破解。
- **Recommendation**：導入單向雜湊（ASP.NET Core `PasswordHasher<T>` / Argon2id / PBKDF2），`new_app_pass` 改存雜湊值，登入改用固定時間比對；搭配 L-2 的節流與鎖定。提供漸進式遷移（使用者下次成功登入時將明文改寫為雜湊）。
- **Safe Verification**：測試環境檢視 CRM 欄位值應為雜湊格式（非可讀明文）；單元測試驗證相同密碼多次雜湊結果不同（含 salt）且可驗證。

### C-3 登入回應把密碼回傳前端

- **Severity**：Critical
- **Category**：Sensitive Data Exposure（API Response）
- **Description**：登入成功回應的 JSON 內含使用者密碼欄位。
- **Evidence**：`AuthenticationController.Private.cs:437-448`——`CreateLoginResponse` 回傳 `Json(new { … account = viewModel.Account, password = viewModel.Password })`。帳密登入時 `password` 即真實密碼；LINE 登入時 `password` 即 LINE User ID（等同憑證）。
- **Impact**：憑證進入瀏覽器記憶體 / 主控台 / 前端錯誤追蹤 / 中間快取，擴大外洩面；與 C-2、H-1 形成鏈結。
- **Recommendation**：從所有登入相關回應移除 `account`、`password` 欄位（前端僅需顯示名稱與導向資訊）。並全面稽核是否有其他 Action 回傳 `password` / `new_app_pass`。
- **Safe Verification**：攔截登入回應 JSON，確認不含 `password` / `account` / `new_app_pass`；加入自動化測試斷言回應欄位白名單。

---

## 6. High Risk Findings（高風險）

### H-1 由 `Referer` 標頭推導登入身分

- **Severity**：High ｜ **Category**：Broken Authentication
- **Description**：系統在 Session 缺密碼時，會從 `Referer` 標頭以正規表示式取出 LINE User ID，並以其建立登入身分。由於 LINE 使用者的 `_LoginPassword` 就是 LINE User ID，等於以「用戶端可控、且會出現在 URL／記錄中的識別碼」作為登入憑證。
- **Evidence**：`BaseChurchController.cs:869-889`（`TryGetLineUserIdFromRequest()`，`Regex "U[a-zA-Z0-9]{32}"` 讀 `Request.Headers["Referer"]`）；`:740-765`（Session 密碼為空時，用其呼叫 `SetupListManager("LineIdLogin", lineUserId, …)` 並寫入 `_LoginAccount/_LoginPassword`）。
- **Impact**：身分建立受可控標頭影響，削弱認證可信度；與 H-4（LINE ID 外洩至外部站）串連後風險升高。
- **Recommendation**：移除以 Referer 推導身分的路徑；LINE 身分僅能來自 **伺服器端 OAuth 交換後寫入的 Session**。若需相容舊流程，至少要求對應 Session 標記存在，且絕不接受來自 Referer / Query 的身分。
- **Safe Verification**：在無有效 Session 下，帶任意 `Referer` 存取受保護頁面應被導向登入，而非被視為該 LINE 使用者。

### H-2 全站未使用認證 / 授權管線（非「預設拒絕」）

- **Severity**：High ｜ **Category**：Broken Access Control
- **Description**：授權完全靠各控制器自行讀 Session；認證票證從未簽發。
- **Evidence**：全 `.cs` 中 `SignInAsync` 出現 **0 次**；`[Authorize]` 僅見於 `DiagnosticsController.cs`（且該檔 `#if DEBUG`，Release 不編譯）。`Startup.cs` 採舊式 `UseMvc`，`UseAuthentication()` 有註冊但因從未 SignIn 而形同虛設。`BaseChurchController.EnsureCorrectUserData()`（`:644-773`）在無 Session 時只是 `return`，不阻擋請求。
- **Impact**：安全性取決於「每個 Action 都記得自檢」；任何新加或遺漏檢查之端點即未授權可達（H-3 為實例）。
- **Recommendation**：導入 **預設拒絕** 的全域授權（`FallbackPolicy = RequireAuthenticatedUser`），或以全域 ActionFilter 強制「未登入即導向 Login」；登入改為簽發認證票證（`SignInAsync`），讓 Session 與認證一致。公開端點（登入、LIFF、健康檢查、金流 callback）以白名單 `[AllowAnonymous]` 標示。
- **Safe Verification**：對每個控制器動作做「未登入存取」矩陣測試，預期一律 302→Login 或 401，唯白名單端點例外。

### H-3 IDOR：Personal 照片端點未授權即可取任意會友照片

- **Severity**：High ｜ **Category**：IDOR / Broken Object Level Authorization
- **Description**：Personal 的照片讀取端點以傳入 GUID 直接回傳 CRM `entityimage`，未做授權，也無登入檢查；而 MemberInfo 的對應端點皆有 `CanViewContact` 把關——同一份 PII 在 MemberInfo 受保護、在 Personal 卻是繞道。
- **Evidence**：`PersonalController.ImageUpload.cs:498`（`GetContactImage(string contactId,…)`）、`:661`（`GetContactImagesBatch([FromBody] BatchImageRequest)`）——皆未呼叫任何授權檢查。對照 `MemberInfoController.cs:271,346`（對應端點）逐一 `CanViewContact` / `CanViewContactsBatch`。
- **Impact**：具備（或蒐集到）會友 contact GUID 者，可越權批次擷取會友大頭照（PII，可能含未成年），並繞過 MemberInfo 的授權設計。
- **Recommendation**：於 Personal 兩個讀取端點加入與 MemberInfo 相同的 `CanViewContact` / `CanViewContactsBatch` 把關；「取自己的照片」路徑改為以 Session 登入身分解析，不接受任意 `contactId`。
- **Safe Verification**：以 A 使用者的 Session 請求 B 的 `contactId`，預期被拒（403／預設圖）；批次端點對不可見 GUID 不回傳影像。

### H-4 OAuth 完成導向未驗證的 returnUrl（開放重導 + 憑證外洩）

- **Severity**：High ｜ **Category**：Open Redirect / Token Leakage
- **Description**：`returnUrl` 取自 `LineLoginStart` 的 query 參數並存入 Session，回呼後直接以字串內插導向，且路徑附帶 LINE User ID；全專案無 `Url.IsLocalUrl` 驗證。
- **Evidence**：`AuthenticationController.LineLoginOAuth.cs:452-513`，關鍵：`return Redirect($"{returnUrl}/{lineUserId}");`（`:513`）。
- **Impact**：可被導向外部網域，且把 LINE User ID（等同憑證）以路徑形式送到外部站，兼具開放重導與釣魚風險。
- **Recommendation**：對 `returnUrl` 強制 `Url.IsLocalUrl()` 或站內白名單；導向目標 **不要** 攜帶 LINE User ID（改由 Session 取用）。
- **Safe Verification**：`LineLoginStart?returnUrl=<外部網址>` 完成後應只導向站內既定頁，且 URL 不含 LINE ID。

### H-5 Session Fixation 防護實際無效

- **Severity**：High ｜ **Category**：Session Fixation
- **Description**：登入 / 登出以 `Session.Clear()+CommitAsync()` 並於註解宣稱「強制重新生成 Session ID」，但 ASP.NET Core 的 `ISession.Clear()/CommitAsync()` **只清資料、不換 Session Key / Cookie**，故登入前後 Session ID 不變。
- **Evidence**：`AuthenticationController.Private.cs:171-205`（`InitializeUserSession`，註解稱「強制重新生成 Session ID」）、`AuthenticationController.Session.cs:42`（登出 `Session.Clear()`）、`BaseChurchController.cs`（`RegenerateSessionId`，同一誤解）。此點與既有記錄一致（Session.Clear 不輪替 Session ID）。
- **Impact**：攻擊者若能於登入前讓受害者使用某個已知 Session ID，登入後該 ID 仍有效 → 典型 Session Fixation。
- **Recommendation**：登入時真正輪替——刪除舊 Session cookie（`Response.Cookies.Delete(".ChurchReport.Session")`）並建立全新 Session 後再寫入身分；或改以認證票證為主體（`SignInAsync`，票證本身即換發）。同步修正誤導性註解。
- **Safe Verification**：記錄登入前後的 Session cookie 值，應 **不同**；舊 ID 於登入後再帶入應無法還原已登入狀態。

---

## 7. Medium / Low Risk Findings

### M-1 登出未真正失效認證票證 / 未輪替 cookie
- **Evidence**：`AuthenticationController.Session.cs:30-65` 只 `Session.Clear()`，未 `SignOutAsync`、未刪 cookie；`ExtendSession`（`:92-104`）為空實作。
- **Recommendation**：登出時 `SignOutAsync` + `Response.Cookies.Delete(...)`，並回應 `Cache-Control: no-store`。
- **Safe Verification**：登出後帶原 cookie 存取受保護頁應被拒。

### M-2 缺乏 CSRF 防護
- **Evidence**：全專案 `[ValidateAntiForgeryToken]` 僅 1 處（DEBUG 的 Diagnostics）。目前僅靠 `SameSite=Lax`（`Startup.cs:576`）。
- **Impact**：對「同站 / 子網域」與「GET 觸發之狀態變更」無保護。
- **Recommendation**：全域啟用 Antiforgery，狀態變更端點（`/Personal/UploadContactImage`、`/MemberInfo/UpdateContactInfo`、`/MemberInfo/ResyncLineProfiles` 等）於表單 / AJAX 帶 token 並驗證。
- **Safe Verification**：缺 token 的跨站狀態變更 POST 應回 400。

### M-3 例外訊息回傳前端
- **Evidence**：`BaseChurchController.cs:350-364`（AJAX 回 `message = exception.Message`，並以 `?ErrorMessage=` 導向）；`PersonalController.ImageUpload.cs:446,466`（`CRM 更新失敗: {faultEx.Message}`）。
- **Recommendation**：回一般化訊息 + 關聯 ID，詳情僅入伺服器記錄。
- **Safe Verification**：觸發錯誤時回應不含堆疊 / CRM 內部字樣。

### M-4 缺少安全標頭
- **Evidence**：`Startup.cs:708-739` 只設 `Cache-Control / Pragma / Expires / X-Content-Type-Options / Vary`。缺 `Strict-Transport-Security`、`UseHttpsRedirection` / `UseHsts`、`X-Frame-Options`（或 CSP `frame-ancestors`）、`Content-Security-Policy`、`Referrer-Policy`。
- **Impact**：Clickjacking、傳輸降級、Referer 外洩（與 L-1／H-1 相乘）。
- **Recommendation**：補齊上述標頭；`Referrer-Policy: no-referrer`（或 `same-origin`）尤其重要。
- **Safe Verification**：回應標頭含上述項目；外部標頭評分工具等級提升。

### M-5 稽核 IP 取自可偽造的 X-Forwarded-For
- **Evidence**：`SessionValidationMiddleware.cs:213-233` 直接取 `X-Forwarded-For` 首段。
- **Impact**：IP 僅供稽核（變動不強制登出），但會污染稽核紀錄。
- **Recommendation**：僅信任已知代理鏈（`ForwardedHeaders` + `KnownProxies / KnownNetworks`；`appsettings.json:155` 已有 `TrustAllProxies:false` 基礎）。

### L-1 敏感值出現在 URL / Query
- **Evidence**：多個 LINE View 以 query 傳 `LineUserId`；`Views/Home/SmallGroupReportView.cshtml:82` 導向含 `AccountPassword` 的路徑。
- **Recommendation**：敏感值改走 POST body。

### L-2 登入無節流 / 鎖定
- **Evidence**：`AuthenticationController.Login.cs:66` `ProcessLogin` 無失敗計數。
- **Recommendation**：加速率限制、帳號鎖定與失敗稽核。

### L-3 失效的 Session 過濾器
- **Evidence**：`SessionAttribute.cs:26-71` `CheckSessionOutAttribute` 為 `async override void`（例外無法捕捉、時序不可靠）且主要邏輯已註解。
- **Recommendation**：移除或以正確 ActionFilter 重寫。

### L-4 版控雜訊
- **Evidence**：`.suo`、`NuGet.config.bak` 等入庫。
- **Recommendation**：以 `.gitignore` 排除。

---

## 8. Session Leakage Analysis（逐項）

針對常見 10 個 Session Leakage 檢查點的逐項結論：

| # | 檢查點 | 結論 | 風險 | 依據 |
|---|---|---|---|---|
| 1 | Session ID 是否出現在 URL / Query / Referer | **Session cookie ID：否**；但 **LINE User ID（等同 LINE 使用者憑證）是，且被信任於 Referer** | High | `LineLoginOAuth.cs:513`、`BaseChurchController.cs:873` |
| 2 | Token 是否寫入 localStorage / JS 可讀處 | **否**（僅存 `locale`） | 無 | `Views/**`（僅 `sessionStorage locale`） |
| 3 | Cookie 是否缺 HttpOnly / Secure / SameSite | **否，皆已設定**（Release：Secure=Always） | 良好 | `Startup.cs:551-576,595-623` |
| 4 | 登入是否重生 Session ID（防 Fixation） | **未真正重生**（Clear+Commit 不換 ID） | High | H-5 |
| 5 | 登出後 Session 是否確實失效 | **資料有清，但 cookie / 認證票證未失效 / 未輪替** | Medium | M-1 |
| 6 | Token 是否進 Console / Server / Exception log | **Release：否**（Token 記錄皆 `Debug.WriteLine`，Release 被移除）；但例外訊息會回前端 | Medium | M-3；`LineLoginOAuth.cs:382,420` 為 Debug-only |
| 7 | API 回應是否回傳多餘 Session / Token / 敏感資料 | **是，登入回應含密碼** | Critical | C-3 |
| 8 | 前端錯誤追蹤是否會上傳 Token / Cookie | **未見前端錯誤追蹤 SDK**；Cookie 為 HttpOnly，JS 不可讀 | 低 | 全域搜尋無第三方追蹤 |
| 9 | 跨站請求是否會帶出 Session | **SameSite=Lax 已阻擋跨站 POST 帶 cookie**；但無 CSRF token 深層防護 | Medium | `Startup.cs:576`；M-2 |
| 10 | 多裝置 / Remember Me / 自動續期風險 | **無 Remember Me / 無持久化 Token**；Session 30 分鐘滑動、認證票證 30 分鐘；無多裝置管理 | 低 | `Startup.cs:553,601`、`web.config sessionState timeout=60` |

> **關鍵補充**：第 6 點的良好慣例是——Token / 敏感輸出一律放在 `System.Diagnostics.Debug.WriteLine`（`[Conditional("DEBUG")]`），Release 建置會整行移除；`EnableTrace` 預設 false、`stdoutLogEnabled=false`、寫入 `Trace.log` 的 listener 受 `#if DEBUG` 保護。**請將「敏感資料只走 Debug 通道」視為需維持的安全不變量**。唯一在 Release 仍會外流的是「例外訊息回傳前端」（M-3）。

---

## 9. Authentication & Authorization Review

- **登入設計**：帳密走 CRM `contact.new_app_acount / new_app_pass`（明文，C-2）；LINE 走伺服器端 OAuth 2.0，`state` 為 32-byte 密碼學亂數並於回呼比對（`LineLoginOAuth.cs:97-98,144-151`）——**此部分 CSRF 防護正確**。`nonce` 有產生但未見與 `id_token` 比對（建議補上，低風險）。
- **授權模型**：無 `[Authorize]` / 角色 / 政策；授權邏輯散落於 `MemberInfoController` 的 `CanViewContact` / `CanViewContactsBatch`（設計良好，且已批次化以避免 N+1）與 `BaseChurchController` 的 Session 檢查。**核心問題在於這是「opt-in」而非「預設拒絕」**（H-2），導致 Personal 照片端點漏洞（H-3）。
- **建議主軸**：統一為「登入 = 簽發認證票證 + 預設拒絕的全域授權」，把既有 `CanViewContact` 這類資源級授權保留為第二層（物件級 IDOR 防護）。

---

## 10. API Security Review

- **IDOR**：MemberInfo 系列端點（`Detail` / `LoadContactPresentRecords` / `LoadContactStorLessons` / `GetContactImage` / `UploadContactImage` / `UpdateContactInfo`）**皆有** `CanViewContact` 物件級把關，值得肯定。**破口在 Personal 的 `GetContactImage` / `GetContactImagesBatch`**（H-3）。建議全面盤點 `ApiControllers/*`（`AssignSmallGroupController`、`SchedulerDataController`、`ShepherdMethodLookupController`、`SpiritLeaderLookupController`）是否同樣缺少登入 / 授權檢查——依 H-2 的整體結構，這些需逐一確認。
- **注入**：資料存取一律 CRM SDK `QueryExpression` + `ConditionExpression`（參數化），**無** 原生 SQL、`FetchXml` 字串拼接、`Process.Start` / shell → **SQL / Command Injection 風險低**。
- **輸入驗證**：GUID 皆 `Guid.TryParse` 驗證；檔案上傳有型別 / 副檔名 / 大小檢查（見第 11 節）。
- **金流 callback**：`MyPay / TSPG / QPay` 的 NotifyUrl / PostBackUrl 屬必須匿名端點，務必確認皆有 **簽章 / 雜湊驗簽**（本次未深入金流驗簽邏輯，列為後續建議項）。

---

## 11. Frontend Sensitive Data Exposure Review

- **Token 儲存**：前端 **未** 使用 `localStorage` / `sessionStorage` 存放任何 Token；`sessionStorage` 僅存 `locale`。良好。
- **Cookie**：Session / Auth cookie 均 `HttpOnly`，JS 無法讀取。良好。
- **檔案上傳（`PersonalController.ImageUpload.cs:90-480`）**：驗證副檔名（`.jpg/.jpeg/.png/.gif`）、Content-Type、大小上限 5MB，並以 **ImageSharp 重新編碼為 JPEG**（等於中和潛在惡意內容），存入 CRM `entityimage`（非落地檔案系統、非可執行路徑）。上傳對象自綁為登入 contact。此路徑 **安全性良好**。
- **XSS 面向**：前端為 Razor + DevExtreme，Razor 預設 HTML 編碼。需注意——因無 CSP（M-4），一旦某處以 `Html.Raw` / `@:` 輸出未編碼的使用者資料，將缺乏第二層防護。建議全域搜尋 `Html.Raw(` 逐一檢視（本次未發現高風險點，但無 CSP 使後果放大）。
- **敏感值入 URL**：LINE User ID 透過 query 傳遞（L-1），配合缺 `Referrer-Policy`，可能經 Referer 外流。

---

## 12. Logging & Error Handling Review

- **良好**：所有 Token / 帳密 / Profile 輸出皆 `Debug.WriteLine`（Release 移除）；`EnableTrace=false`、`Profiling:Enabled=false`、`stdoutLogEnabled=false`、Production `LogLevel=Warning`。寫入 `Trace.log` 的 listener 受 `#if DEBUG` 保護（`Program.cs:30-39`）。
- **需修正**：例外訊息回傳前端（M-3）；`PaymentDebugLog.MaskSensitiveData=true` 已預設遮罩（良好），但 `Enabled` 開啟時仍應定期輪替 / 清理該目錄。
- **建議**：導入結構化記錄與關聯 ID，前端只拿到關聯 ID，詳情留伺服器端。

---

## 13. Recommended Remediation Plan（修復計畫）

### P0（24–72 小時內）
1. 輪替 `appsettings.json` 內全部密鑰 / 密碼（C-1），機密移出版控並清理 git 歷史；CRM 改最小權限帳號。
2. 登入回應移除 `password` / `account`（C-3）。
3. Personal 照片端點加入 `CanViewContact` 把關（H-3）。

### P1（2 週內）
4. 密碼改雜湊儲存與比對 + 登入節流 / 鎖定（C-2 / L-2）。
5. 導入 `SignInAsync` + 全域 `FallbackPolicy = RequireAuthenticatedUser`，公開端點加 `[AllowAnonymous]`（H-2）。
6. 真正輪替 Session ID（登入刪舊 cookie / 換發）+ 登出 `SignOutAsync` + 刪 cookie（H-5 / M-1）。
7. 移除 Referer 身分推導（H-1）；OAuth `returnUrl` 加 `IsLocalUrl` 且導向不帶 LINE ID（H-4）。

### P2（1 個月內）
8. 全域 Antiforgery（M-2）；補齊安全標頭含 `Referrer-Policy`、HSTS、X-Frame-Options、CSP（M-4）。
9. 例外訊息一般化（M-3）；盤點 `ApiControllers/*` 授權；確認金流 callback 驗簽。
10. X-Forwarded-For 只信任已知代理（M-5）；清理版控雜訊與失效過濾器（L-3 / L-4）。

---

## 14. Verification Checklist（驗證清單）

- [ ] 原始碼 / 歷史掃描（gitleaks）對 `appsettings*.json` 無高熵 / 已知機密命中；正式環境啟動記錄不含機密。
- [ ] 舊密鑰於供應商後台已停用 / 輪替（CRM、LINE、各金流）。
- [ ] 登入回應 JSON 不含 `password` / `account` / `new_app_pass`（自動化斷言）。
- [ ] CRM `new_app_pass` 存的是雜湊；相同密碼兩次雜湊值不同且可驗證。
- [ ] 未登入存取每個控制器動作 → 一律導向 Login / 401，唯白名單匿名端點例外。
- [ ] A 使用者請求 B 的 `contactId`（Personal 與 MemberInfo 兩路）→ 皆被拒。
- [ ] 登入前後 Session cookie 值不同；舊 Session ID 無法還原登入狀態。
- [ ] 登出後帶原 cookie 存取受保護頁 → 被拒。
- [ ] `LineLoginStart?returnUrl=<外部網址>` → 只導向站內，URL 不含 LINE ID。
- [ ] 帶任意 `Referer` 之未登入請求 → 不被視為任何 LINE 使用者。
- [ ] 缺 antiforgery token 的跨站狀態變更 POST → 400。
- [ ] 回應標頭含 HSTS / X-Frame-Options（或 CSP frame-ancestors）/ Referrer-Policy / CSP。
- [ ] 觸發伺服器錯誤 → 前端只見一般化訊息 + 關聯 ID，無堆疊 / CRM 內部字樣。

---

## 附錄 A：已驗證為良好的安全設計

> 以下項目經檢視確認為良好，維護時 **請勿誤刪 / 誤改**，以免造成回歸。

- **Cookie 安全屬性**：Session／Auth Cookie 皆 `HttpOnly` + `Secure`(Release, `Always`) + `SameSite=Lax`；Auth Cookie 另命名 `.ChurchReport.Auth` 以避免與 Session Cookie 混淆（`Startup.cs:551-576,595-623`）。
- **SameSite=Lax 為刻意選擇**：為相容 LINE LIFF 登入而由 Strict 調整為 Lax，屬有意識的取捨（`Startup.cs:569-576`）。
- **`SessionBleeding:EnableResponseCaching=false` 為刻意關閉**：避免登入後動態頁被快取；`VaryByQueryKeys` 在此環境不可用（既有記錄）。
- **全站無快取 + Vary: Cookie**：防 Web Cache Deception / Proxy 共用（`Startup.cs:708-739`）；另有 `WebCacheDeceptionMiddleware`。
- **LINE OAuth `state`**：密碼學亂數並於回呼比對（正確 CSRF 防護）。
- **無 CORS 萬用字元**：未設定跨來源政策，預設同源。
- **資料存取全參數化**：CRM SDK `QueryExpression`，無原生 SQL / FetchXml 拼接 / shell 執行。
- **前端無 Token 落地**：`localStorage` / `sessionStorage` 僅存 `locale`。
- **檔案上傳安全**：型別 / 大小驗證 + ImageSharp 重新編碼；存入 CRM 而非檔案系統。
- **敏感輸出僅走 Debug 通道**：`Debug.WriteLine`（Release 移除）、`EnableTrace=false`、`stdoutLogEnabled=false`、Trace listener `#if DEBUG`。
- **MemberInfo 物件級授權**：`CanViewContact` / `CanViewContactsBatch` 完整把關且已批次化。
- **正式環境 Host 驗證**：`appsettings.Production.json` 已設 `AllowedHosts`。

---

## 附錄 B：本次稽核實際檢視之檔案

- `ChurchReport/Program.cs`
- `ChurchReport/Startup.cs`
- `ChurchReport/appsettings.json`、`appsettings.Production.json`、`appsettings.Development.json`、`web.config`
- `ChurchReport/SessionAttribute.cs`
- `ChurchReport/Middleware/SessionValidationMiddleware.cs`
- `ChurchReport/Controllers/BaseChurchController.cs`
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Login.cs`
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs`
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.Session.cs`
- `ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs`
- `ChurchReport/Controllers/PersonalController.ImageUpload.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Controllers/DiagnosticsController.cs`
- `ChurchReport/Views/**/*.cshtml`（Token / 敏感資料 / XSS 面向抽樣）

**尚待補件以完成之後續章節**：金流 callback 驗簽（`Payments/*`、`MyPayController`、`TSPGController`、`PaymentReturnController`）、`ApiControllers/*` 四端點完整內容、`Views` 中 `Html.Raw(` 用法清單、CRM 服務帳號實際權限範圍。

---

*報告結束 — 本報告為防禦性稽核，不含可操作之攻擊細節。*
