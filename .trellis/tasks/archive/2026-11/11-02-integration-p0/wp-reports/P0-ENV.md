# P0-ENV　指定 goodshepherdback 為開發測試組織（完成本機設定）

## 範圍
- 修改：appsettings.Development.json — CRM Organization／ServerUrl、QPAY_ORGANIZATION 改為 goodshepherdback；Domain／Username／Password 空白遮蔽基底，僅由 User Secrets 提供；Package01FeeReadsEnabled=false。
- 新增：DevelopmentOrganizationSettingsTests.cs — 五個設定契約、單案例組態與暫存檔 owner；實際 Development 檔配獨立假基底，不讀使用者 secrets，不建立外部 client。
- 修改：3.3 的 00 §10 將使用者回報交叉參照 P0-12 更正為 P0-13。核對 README、01、02 的 P1-11／P1-12、04 的 P5／P7，均已具備 goodshepherdback 與 D23／D24 規則，未另改動。
- 新增：端點盤點及驗證摘要；更新任務與 3.3 進度 DATA。
- 未做：正式基底機密移除、付款回呼／登入入口變更、ADFS 驗證、任何 D365／LINE 連線。3.2 凍結與已完成 WP 歷史回報不動。

## TDD 紀錄
- 原列測試全部保留，刪除 0：CRM 組織與主機、帳密不可沿用、Web API 關閉、LINE 別名／token 與 QPAY、P0-SEC 守門通過。
- 紅燈：五個案例失敗，分別為舊 CRM／帳密仍可讀／旗標 true／舊 QPAY／守門拒絕。所有憑證斷言先轉成布林，失敗不印實值。
- 綠燈：5／5。初輪直接合併 repo 檔；重構後只複製實際 Development 檔，其餘用假基底與假憑證。再以修改前 Development 檔重跑 5／5 紅燈，恢復後 5／5 綠燈（red-settings-fake-baseline、green-settings）。
- 測試不改程序環境或目前目錄，組態根先 Dispose，再清除已核對的專有暫存根。假 token 為 PROD-FAKE，假帳密均帶 FAKE。
- 黃金檔變更：無。
- 歷史：外部提交 84ae1c17 已帶入當時測試及任務登錄，保留歷史；本次提交完成設定、假基底重構與綠燈，不 reset／rebase／amend。

## 驗證
- Release 建置：0 錯誤、76 警告（增量，基準 77；未新增抑制）。
- Release 全方案：771 通過、0 失敗、0 略過（原 766 加 5）；Debug tracer 7／7。
- appsettings.json 與 appsettings.Production.json 相對 24cca555 的 checkout 位元組完全相同，已套用 Git CRLF 轉換後比較；非 Development 仍不加開發 provider。
- 進度頁 3／3；Changed（Base=73b6e6cf）170 檔、排除 74 檔、0 問題；git diff --check 與暫存檢查通過，新增／修改檔逐位元組檢查全過，3.2 凍結副本 8 檔 SHA256 未變。
- 覆蓋率：沒有新增產品專案，不設新專案門檻。
- 沒有啟動網站或連線 D365／LINE。

## 仍沿用的端點盤點
只盤點 repo 基底＋Development，不讀真實 secrets 或程序環境的實值。22 個非本機、非 goodshepherdback URL 鍵包含正式入口及既有外部測試服務；只保存鍵名與主機，不保存完整 URL 或 query。

| 設定鍵 | 主機（不含路徑／query／機密） | 處理方式 |
| --- | --- | --- |
| LineLogin:CallbackUrl | jesus.speechmessage.com.tw | 保留正式入口，不作 Development 驗收目標 |
| MiniApp:EndpointBaseUrl | jesus.speechmessage.com.tw | 保留正式入口，不作 Development 驗收目標 |
| MiniApp:PrivacyPolicyUrl | jesus.speechmessage.com.tw | 保留正式入口，不作 Development 驗收目標 |
| Payment:Profiles:JesusTest:Endpoints:ApiBaseUrl | sandbox.sinopac.com | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| Payment:Profiles:MyPayProduction:Endpoints:ApiBaseUrl | ka.usecase.cc | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| Payment:Profiles:TaishinSandbox:Endpoints:ApiBaseUrl | tspg-t.taishinbank.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| Sinopac:Site | api.sinopac.com | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| Sandbox:Site_Xkey | apisbx.sinopac.com | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| Sandbox:Site | sandbox.sinopac.com | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| MyPay:Url | ka.usecase.cc | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| MyPay:NotifyUrl | jesus.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| MyPay:SuccessReturl | jesus.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| MyPay:FailureReturl | jesus.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| RETURN_URL | sunnyvalechback.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| BACKEND_URL | qpaybackend.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| TSPG:ApiBaseUrl | tspg-t.taishinbank.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| TSPG:TestApiRoot | tspg-t.taishinbank.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| TSPG:POST_BACK_URL | jesus.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| TSPG:RESULT_URL | jesus.speechmessage.com.tw | 保留既有正式／測試金流端點；P4 前禁止開發付款實測 |
| DynamicsAccess:Embedded:OrganizationWebApiBaseUri | sunnyvalechback.speechmessage.com.tw | 已停用費用 Web API；待 ADFS 驗證 |
| DynamicsAccess:Embedded:AuthorityUri | adfsdev91.speechmessage.com.tw | 已停用費用 Web API；待 ADFS 驗證 |
| DynamicsAccess:Embedded:ResourceUri | sunnyvalechback.speechmessage.com.tw | 已停用費用 Web API；待 ADFS 驗證 |

## AGENTS.md 檢查
- 新測試類別、方法、scope、組態 owner 與釋放順序均有繁中 XML 文件；新增／修改檔依位元組驗證 UTF-8 無 BOM、CRLF、檔尾 CRLF。
- 沒有新增產品 C#、共享使用者狀態、背景工作或 watcher；每案的組態根與假檔由 scope 確定釋放。
- 例外通知管線未變；既有 P0-SEC 守門保持有效，不新增通知出口。

## 風險與待決
- 基底 appsettings.json 仍有正式明文密碼與 LINE token，依核定不移除以免影響正式部署；由使用者輪替，回報不抄錄實值。
- 開發帳密空字串遮蔽基底，未提供 User Secrets 時無法連線是預期拒絕，不退回正式帳密。
- Embedded URL／ResourceUri 尚指舊組織，但費用查詢已停用；goodshepherdback ADFS 驗證前不走 Web API。
- 正式付款回呼可能寫入同 GUID 記錄，P4 前 Development 不得實測付款。LINE Login／MiniApp 正式入口不作開發驗收目標。
- goodshepherdback 清理尚未回報完成，任何程式、工具與測試不得連線；完成後仍遵守 D24，由使用者執行所有寫入。

## 需要使用者做的事
- 無。本包提交後接續 P0-06 離線工具。清理回報及測試帳密由使用者在後續實機前處理。

---

## 初次調查的歷史停止紀錄（保留原文；阻擋已由 P0-SEC 解決）

# P0-ENV　指定開發測試組織（安全調查停止回報）

## 範圍
- 新增：research/p0-env-development-token-blocker.md — 靜態設定讀取路徑與不能只靠 Development 覆寫的原因。
- 新增：wp-evidence/P0-ENV/local-findings.json — 只記錄鍵名、布林狀態、程式位置與停止條件，無 token／帳密／個資。
- 修改：任務狀態、implement.md 與實作進度.html 的核定 DATA 欄位 — 記錄 P0-05 提交與本包阻擋。
- 未做：未修改任何產品 .cs／appsettings／csproj；未修改 00／01／02／04 的組織操作計畫文字；未開始 P0-06。發現停止條件後，只保存回報及狀態。

## TDD 紀錄
- 原定先寫 Development 合併後 Organization=goodshepherdback、ServerUrl 主機一致的設定測試，及設定即可阻止正式 token 的測試。
- 本包在動手修改設定前的安全調查即遇到停止條件，尚未開始上述設定紅燈／綠燈；沒有刪除測試項目，也不把源碼分析冒充已執行的測試。
- LineUtilityClass 只讀 appsettings.json 的靜態設定，不讀 Development／User Secrets；所以僅測合併後的設定值，會漏掉真正的舊推播路徑。
- 黃金檔變更：無。

## 驗證
- 僅做本機源碼與設定結構核對；沒有建立 CRM／LINE client、啟動網站、查詢 D365、讀取 User Secrets 或使用真實 token。
- 實際找到 16 處 Jesus 退回邏輯，分布於 14 個檔案；不是原提示的 9 處。
- 最近完整驗證是 P0-05：Release 建置 0 錯誤、77 警告；Release 741／741、Debug tracer 7／7；編碼測試 PS 5.1／7 各 21／21，進度頁 3／3，Changed 113 檔且 0 問題。
- 本包未完成、未提交，不宣稱通過獨立 P0-ENV 全套驗證；遇停止條件後沒有重跑 Release 全套建置與測試。
- 本次停止紀錄驗證：進度頁 Node 測試 3／3；全階段 Changed 編碼守門（Base=73b6e6cf）檢查 113 檔、排除 74 檔、0 問題；7 個修改／新增檔皆通過 UTF-8 無 BOM、CRLF、檔尾 CRLF 位元組檢查，JSON 可解析，git diff --check 通過。未追蹤的停止證據由位元組檢查逐檔涵蓋，沒有暫存或冒稱已被 Changed 掃入。

## AGENTS.md 檢查
- 未修改產品 C# 或 Razor，不改隔離、通知、資源生命週期或正式行為。
- 不在回報／證據／進度頁保存 token、帳密、收件人 ID 或查詢所得個資；只記錄必要結構及程式位置。
- 沒有連線任何 D365 組織，goodshepherdback 仍視為需正式個資保護的副本；使用者回報清理完成前禁止連線。

## 風險與待決
- 只改 Development 設定，無法限制繞過 Host 設定的靜態 LINE 讀取路徑；需要產品程式的安全防護，屬使用者指定的 P2-01 範圍。
- 本包不藉移除正式基底 token、停用檢查或只驗證設定值來假稱已安全。
- goodshepherdback 舊 Plugin／Workflow、等候作業與 LINE token 清理尚未回報完成，任何程式／工具／測試均不得連線。

## 需要使用者做的事
- 決定 P2-01 安全啟動及 LINE token 來源防護如何安排。取得新指示前維持停止，暫勿啟動 Development 網站；本包不在階段 0 擅自修改產品程式。
