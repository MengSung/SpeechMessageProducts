## 分析:P3-09 實機驗收與階段 3 收尾

我沒有讀 `P3-09-codex-prompt.txt`、04 檔或 §8.3,只依任務描述分析。凡涉及「第 1~7 步」的具體內容,執行前要先對照 04 檔原文。

### Critical(違反即停,不得繼續)

1. **秘密處理**
   - 密碼只在同一個行程內取用,不印出、不存檔、不寫入 transcript:
     `$cred = Import-Clixml C:\Users\Administrator\.speechmessage\test-vm-admin.clixml; Invoke-Command -VMName D365APP01 -Credential $cred -ScriptBlock {...}`
   - 禁止 `-Verbose`、`Start-Transcript`,也禁止把 `$cred` 或 `$env:` 值送進 log。
   - Channel secret/token 用 `dotnet user-secrets` 載入。
   - 可用 `git grep -n -I -E "<secret 前 4 碼>"` 查證無洩漏,但不要輸出結果內容,只看是否有命中。
   - 給 VM 的設定改用環境變數,由 PowerShell Direct 在 session 內設定,且不寫入 appsettings。
   - 若 secret 無法在 VM 內不落地地傳入,必須停下回報,不可改存成檔案。

2. **目標 VM 確認**
   - 先查 `Get-VM D365APP01`,確認狀態為 Running,且 IP 為 192.168.50.20。
   - 全程不對 `D365DC01` 執行任何指令。
   - 所有指令都以 `-VMName D365APP01` 明確指定。
   - 驗證點:session 內 `$env:COMPUTERNAME` 必須等於 D365APP01,不符就停止。

3. **IIS 備份先於一切變更**
   - 在 VM 內執行 `%windir%\system32\inetsrv\appcmd.exe add backup P3-09-pre-<yyyyMMddHHmm>`。
   - 再用 `appcmd list backup` 確認備份存在。
   - 同時記下 `appcmd list site` 和 `list apppool` 的快照,作為還原與比對基準。
   - 沒有備份就不得動 IIS。

4. **只新增、不修改 CRM**
   - 新增獨立 site `LineWebhookTest` 與獨立 app pool,使用新的 binding(host header = `linewebhook-test.speechmessage.com.tw`,443)。
   - 憑證只能「引用」既有萬用憑證的 thumbprint,不得匯入、更換或刪除。
   - 風險是 443 binding 衝突。必須確認 goodshepherdback 的 binding 沒有被覆蓋。
   - 若 CRM 與新 site 共用 IP:443,須啟用 SNI(`sslFlags=1`)。
   - 變更前後都要比對 goodshepherdback 的 binding/pool/憑證 hash 是否不變。若有差異,立即用備份還原並停止。

5. **資料庫**
   - 使用新建的獨立測試 DB,名稱要明確(例如 `SpeechMessage_P3_09_Test`)。
   - 連線字串需證明指向該 DB,且與 CRM DB 不同。可查 `SELECT DB_NAME()`,並確認連線字串中沒有 CRM DB 名稱。
   - 若不能確定隔離(例如只有 CRM 的 SQL 帳號可用),就停止回報。不得「先用 CRM DB 試試」。
   - 收尾時 DROP 測試 DB,或清空並逐表 `COUNT(*)=0` 作為 0 殘留證據。

6. **不得假稱完成**
   - 第 1~7 步中任一步失敗或前置不足,該步標「未通過/未執行」,並附原因。
   - P3-09 不得標記為驗收完成,階段 3 也不得收尾。

### Warning

**A. 建議執行順序(每步都可回復)**
- **0. 前置(只讀)**
  - 先確認 git 工作樹乾淨(`git status`)。
  - 再修三個紀錄檔的 CRLF。修完用 `git diff --stat` 和 `git ls-files --eol` 確認沒有混合換行,並跑 §8.3。
  - 之後每個紀錄型提交前都再跑 §8.3。
- **1. 查詢 VM 與 IIS 現況**
  - 這一步只讀:`Get-VM`、PowerShell Direct 測試連線、`Get-WindowsFeature Web-Server`、`appcmd list site`。
- **2. IIS 備份**(見 Critical 3)。
- **3. 安裝或確認 .NET 10 ASP.NET Core Hosting Bundle**
  - 驗證:`dotnet --list-runtimes` 要有 `Microsoft.AspNetCore.App 10.x`。
  - 驗證:`Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*` 要有 Hosting Bundle,且 `AspNetCoreModuleV2` 存在於 IIS 模組。
  - 風險:安裝 Bundle 後通常需要 `iisreset`,這會短暫影響 CRM site,屬於全機影響。
  - 建議改成:先確認 bundle 已存在。若不存在,是否允許 `iisreset` 要視為需要使用者核可的決定,不要自行決定。
  - 若只需重啟新 app pool 就可行,優先用這個做法。
- **4. 建立 DB**,並部署發佈輸出到獨立目錄,例如 `C:\inetpub\LineWebhookTest`。
- **5. 建立 app pool 與 site**
  - app pool 設為 `managedRuntimeVersion=""`(No Managed Code)。
  - site 掛上 443 binding 與憑證。
- **6. VM 內本機驗證**
  - 在 VM 內先用 hosts 或 `curl --resolve` 打自己,確認 TLS 與應用都正常。
  - 再從外部測試。
- **7. 第 1~7 步實機驗收**。
- **8. 清理與還原確認**。

**B. DNS/TLS 決定性驗證**
- `Resolve-DnsName linewebhook-test.speechmessage.com.tw`,必須解析到 220.134.52.83。若解析不到,這是前置不足,不是我能修的,應停止。
- `openssl s_client -connect <host>:443 -servername linewebhook-test.speechmessage.com.tw`,檢查憑證 CN/SAN 涵蓋該主機名、鏈完整、未過期。
- 外部可達性受路由器 NAT/443 轉發影響。若 LINE Console 驗證失敗,先分辨是 DNS、NAT、TLS 還是應用層問題,不要直接改 CRM 設定。
- 既有萬用憑證只涵蓋一層。`*.speechmessage.com.tw` 涵蓋 `linewebhook-test.speechmessage.com.tw`,可以通過。但它不涵蓋更深層子網域。

**C. PowerShell Direct 風險**
- 它需要 Hyper-V 主機與客體都是 Windows 10/2016 以上,且憑證要在客體內有效。
- Session 可能因 VM 重開而中斷。每個變更步驟應是冪等的(先判斷再建立),便於重跑。
- 不要把 `$cred` 傳進 `-ArgumentList`,以免進入 log。

**D. SQL 風險**
- 確認 SQL 認證方式(Windows 認證或 SQL 帳號)。SQL 帳號的密碼同樣視為秘密。
- 用 app pool identity 授權時,只給測試 DB 的最小權限,不給 sysadmin。
- migration 腳本先在測試 DB 跑,並記錄 schema 版本。

**E. .NET 10 Hosting Bundle**
- 安裝可能影響同機其他 ASP.NET Core 應用。CRM 若是 .NET Framework 4.x,通常不受影響,但仍需前後比對 `dotnet --list-runtimes`。
- 用 `web.config` 的 stdout log 排查啟動失敗(HTTP 500.30/502.5)。log 內不可含秘密。結束後關閉 `stdoutLogEnabled` 並刪除 log。

### 必須停止並回報的條件
- 無法取得 VM 管理權限、PowerShell Direct 連不上,或識別出的主機不是 D365APP01。
- 備份建立失敗。
- 無法證明 DB 隔離。
- 需修改 CRM site、binding、pool 或憑證才能繼續。
- DNS 未指向 220.134.52.83,或 NAT/443 外部不通(外部測試需要使用者介入)。
- 需要 `iisreset` 或重啟 CRM 相關服務,但尚未取得核可。
- Channel secret/token 不在本機 User Secrets,或無法不落地地傳入。
- 04 檔的步驟需要真實 LINE 帳號操作,例如手機傳訊或 Console 驗證,而自動化無法完成。這類步驟標示為「待人工」,不得偽造結果。
- 驗收測試出現未預期的資料寫入到非測試 DB。

### P3-09 第 1~7 步的證據與清理(通則)
- 每步保留:時間戳、指令(已遮蔽秘密)、HTTP 狀態碼、IIS log 對應行、DB 查詢結果(只含計數與非敏感欄位)。
- 簽章驗證的證據:
  - 錯誤簽章應得 401/403。
  - 正確簽章應得 200。
  - 錯誤簽章的測試請求用假 body,不要用真實 secret 計算。若需用真實 secret 產生簽章,必須在記憶體內完成,不得落地。
- 清理清單:
  - 停止並刪除 `LineWebhookTest` site 與 app pool。
  - 刪除部署目錄與 stdout log。
  - DROP 測試 DB。
  - 用 `appcmd list site/apppool` 與備份快照比對,確認回到初始狀態。必要時 `appcmd restore backup`。
  - 清除記憶體中的 `$cred`(`Remove-Variable cred`)。
  - 確認 User Secrets 沒有被複製到任何檔案。
  - 殘留測試資料為 0,需有 `COUNT(*)` 證據,或 DB 已不存在的證據。
- 清理本身若失敗,要如實寫入紀錄,不得略過。

### Info:收尾順序
1. 先確認 P3-09 的結論(通過/未通過/待人工),並完成清理與 0 殘留驗證。
2. 更新驗收紀錄檔,再跑 §8.3,通過後提交。紀錄型提交的說明要與實際結果一致。
3. 做階段 3 的整體審查(程式與文件)。審查發現問題就先修,不要先推送。
4. `git status` 確認乾淨,再 `git push`。這是對外動作,推送前確認目標分支與遠端。公開 repo 需特別再掃一次秘密(見 Critical 1)。
5. 最後更新進度頁。內容必須與已推送的紀錄一致。若 P3-09 未全部通過,進度頁就標示為「有保留項」,不得標示階段 3 完成。

### Action Items
1. [ ] 讀 `P3-09-codex-prompt.txt`、04 檔與 §8.3,對照上述假設。
2. [ ] 修正三個紀錄檔 CRLF 並跑 §8.3。
3. [ ] 只讀前置檢查:VM 身分、IIS 現況、DNS/TLS、Hosting Bundle、SQL 隔離。
4. [ ] 任何一項不足,先回報再等指示,不要硬做。
5. [ ] 全部前置通過後,依序執行:備份、部署、驗收、清理。

---
SESSION_ID: ee3e33d5-8c42-434d-b22b-f00d2f0b1823
