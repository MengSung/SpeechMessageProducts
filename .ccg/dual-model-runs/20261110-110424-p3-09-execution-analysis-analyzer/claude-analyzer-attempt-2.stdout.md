## 分析:P3-09 實機驗收與階段 3 收尾

我沒有讀取 04 檔或 `P3-09-codex-prompt.txt`,以下依任務描述推導。需要對照原文的地方我會標示「以 04 檔為準」。

### 一、執行順序與前置驗證(安全、可回復)

**Critical**

1. **先做 Phase 0 唯讀盤點,通過才能動手。** 任何一項不符就停止。
   - `git status` 乾淨,分支為 `1.0.0.11.Integration.P3.Worktree`,HEAD 為 `014ff0af`。目前未追蹤的只有 `.ccg/` 三項,它們不得進入提交。
   - 先修正三個既有紀錄檔的 CRLF。修正後跑 §8.3,再單獨提交,不與其他變更混在一起。
   - 確認 `Get-VM` 只操作 `D365-9.1`(VM 名稱 D365APP01)。不得對 D365DC01 執行 `Get-VM`、`Enter-PSSession`、`Invoke-Command` 或 `Checkpoint-VM`。指令中必須寫死 VM 名稱,不用萬用字元或 `Get-VM | ForEach`。
   - 確認密碼來源只用 `Import-Clixml`,且只存在於記憶體的 PSCredential 變數。
2. **做任何變更前先建立回復點。** 順序如下:
   - 在 VM 內執行 `%windir%\system32\inetsrv\appcmd.exe add backup P3-09-pre-<yyyyMMddHHmm>`,記下備份名稱。
   - 另外匯出 applicationHost.config 的雜湊值(`Get-FileHash`),收尾時比對 CRM 相關區段未變。
   - 備份 SQL:對獨立測試 DB 做 `BACKUP DATABASE`,或記錄它是新建的、回復方式是 DROP。
   - 建議再做 Hyper-V checkpoint(只針對 D365APP01)。這不能取代 appcmd backup,但回復成本最低。
   - 先確認 checkpoint 不會造成 D365DC01 與 D365APP01 的 AD 時間或 USN 回溯問題。生產複本型 D365 加上 checkpoint 還原有 AD 信任風險,因此我把它列為選用,並以 appcmd backup 為主。
3. **變更順序(由低到高風險):**
   1. 安裝 .NET 10 Hosting Bundle,前提是其安裝不會重啟 W3SVC 而影響 CRM。見下文。
   2. 建立獨立 DB 與 SQL 登入權限。
   3. 建立新 app pool(No Managed Code),再建立新 site 與 binding。
   4. 部署應用程式,secret 以環境變數或本機注入,不寫入檔案。
   5. 本機以 curl 先驗證,再驗證 DNS/TLS 外部路徑。
   6. 依 04 檔第 1~7 步跑實機。
   7. 清理並驗證殘留為 0。

**Warning**

- 每一步都要有「前置條件、動作、驗證、回復」四欄。驗證失敗就先回復,不要往下繼續。
- 提交順序:紀錄型提交各自在提交前跑 §8.3。

### 二、各元件風險與決定性驗證

| 元件 | 風險 | 決定性驗證 |
|---|---|---|
| PowerShell Direct | 僅限 Hyper-V host 本機;用錯 VM 名稱會誤入 DC;憑證不能序列化到磁碟 | `Invoke-Command -VMName 'D365-9.1' -Credential $cred { hostname; (Get-NetIPAddress -AddressFamily IPv4).IPAddress }`,必須回 D365APP01 與 192.168.50.20,否則立即停止 |
| IIS | 改到 CRM site、binding、pool 或憑證;`iisreset` 會中斷 CRM | 只用 `appcmd` 或 `WebAdministration` 對新物件操作,禁用 `iisreset`。操作前後比對 `Get-Website`、`Get-IISAppPool` 中 goodshepherdback 的 state、bindings、`certificateHash` 完全不變 |
| IIS binding / SNI | 443 已被 goodshepherdback 使用,新 site 若沒設 SNI 且 hostname 錯,會搶走 CRM 流量 | 新 binding 必須有 hostname `linewebhook-test.speechmessage.com.tw` 並啟用 `sslFlags=1`(SNI)。驗證:`netsh http show sslcert` 比對前後,另外用 `curl --resolve` 分別請求 CRM 主機名,確認回應不變 |
| SQL | 誤連 CRM DB;登入權限過大 | 連線字串的 Initial Catalog 必須是新建庫。收尾比對 `sys.databases` 中 CRM DB 的 `modify_date` 未變。使用最小權限的專用登入,不用 sa |
| DNS/TLS | DNS 在 Seednet,尚未傳播;萬用憑證是否涵蓋 `*.speechmessage.com.tw` 且未過期;出口 NAT 與 hairpin | `Resolve-DnsName linewebhook-test.speechmessage.com.tw -Server 8.8.8.8`,應回 220.134.52.83。`openssl s_client -connect ... -servername linewebhook-test.speechmessage.com.tw`,檢查 SAN、到期日、完整鏈。必須從外網驗證,例如手機網路,不能只在內網驗證,因為內網驗證可能因 hairpin NAT 而誤判 |
| .NET 10 Hosting Bundle | 安裝後 IIS 模組更新,可能重啟 W3SVC;與 CRM 的 .NET Framework pool 共存 | 安裝前先確認維護窗口與 CRM 使用狀況。安裝後 `dotnet --list-runtimes` 要有 `Microsoft.AspNetCore.App 10.x`,`AspNetCoreModuleV2` 存在。再確認 CRM 站台 HTTP 200。若安裝需重啟系統,必須停止並回報,取得使用者核定後才繼續 |
| LINE | Webhook URL 驗證需要 HTTPS 外部可達,並要求簽章驗證 | 以 LINE Console 的 Verify 按鈕,或自行簽章的 POST 驗證 200。另測錯誤簽章必須回 401 或 403 |

### 三、P3-09 第 1~7 步的證據與清理

我沒有 04 檔原文,以下為通用證據規格,以 04 檔步驟為準逐項對照。

**Warning**

- 每一步都留下「指令、時間戳、結果摘要、通過或失敗」。證據只存脫敏後的輸出:不含 token、secret、密碼、完整 Authorization 標頭,也不含使用者 LINE ID 明文(以雜湊或末四碼代替)。
- 證據中出現 secret 的風險點是:IIS 站台的環境變數設定、`appcmd` 輸出,以及 stdout 的連線字串。這幾處輸出前要先過濾。
- 失敗步驟照實寫「失敗」,並附原因與回復動作,不得改寫成通過。
- 清理清單與驗證(須為 0):
  - IIS:新 site、app pool、檔案目錄、binding、`netsh` URL ACL。
  - SQL:測試 DB 與登入,或表內測試資料。
  - 本機:`$cred` 變數、暫存檔、`.ccg` 內是否有 secret(用 `git grep` 或 `Select-String` 掃已知前綴,不印出內容)。
  - LINE 側:測試用的 Webhook URL 設定是否需要還原。
  - 驗證指令例:`SELECT COUNT(*)` 各表、`Get-Website | ? Name -like '*linewebhook*'`、`git status`。
- 殘留測試資料為 0 要有查詢證據,不能只宣稱。

### 四、必須停止並回報的條件

**Critical**

1. VM 驗證不是 D365APP01 或 192.168.50.20,或操作會觸及 D365DC01。
2. `Import-Clixml` 失敗,例如 DPAPI 與使用者或機器不符。不得改用明文密碼。
3. appcmd backup 失敗,或無法確認備份存在。
4. 新 site 的 binding 會與 goodshepherdback 衝突(同 IP:443 且無 SNI),或需要修改 CRM 的 site、pool 或憑證才能完成。
5. 資料庫無法建立獨立測試庫,或連線字串指向 CRM DB。
6. Hosting Bundle 需要重啟系統或會中斷 CRM,而未取得核定。
7. 萬用憑證過期、SAN 不符,或 DNS 未指向 220.134.52.83。你不能自行修改 Seednet DNS,必須回報。
8. User Secrets 取不到 channel secret 或 token。不得用假值通過驗收。
9. 掃描發現 secret 已出現在 repo、log 或證據檔。要先停止、清除並回報,不得提交。
10. §8.3 失敗。

**Warning:** 外網不可達但內網可達,記為「前置不足」,不能算 P3-09 通過。04 檔任一步驟失敗,階段 3 就不得標示完成。

### 五、收尾順序

1. 清理並做殘留為 0 的驗證,附證據。
2. 完成 CRM 不變的比對(site、pool、binding、憑證雜湊、DB `modify_date`)。
3. 更新驗收紀錄與 P3-09 結果,如實寫通過、失敗或前置不足,之後跑 §8.3,再提交。
4. 秘密掃描:對暫存內容與將推送的提交掃描,確認 repo 公開所以不可含 secret。依既有決定,appsettings 內的帳密不再提出。
5. 階段 3 審查。可用 `/code-review`,對整個階段 3 diff(`main..HEAD`)做審查,結果寫入紀錄。有 Critical 發現就先修再審。
6. `git status` 乾淨(`.ccg/` 未追蹤項目要決定納入或忽略,不要混入提交)。
7. 推送:先 `git fetch`,確認不需要 force。推送前再次確認分支與遠端,不使用 `--force`。
8. 最後才更新進度頁,內容必須與紀錄一致:通過寫通過,有保留缺項就寫保留缺項。更新後再提交並推送。進度頁是最後一步,避免它先於事實。

**Info**

- 先前的 P3-09 驗收手冊提交(`014ff0af`)已存在,其步驟應與 04 檔核對一致。
- 以 `.ccg/dual-model-runs` 保存本次執行紀錄,但提交前確認其中不含 secret。
- 建議在整個流程開頭產出一份檢查清單檔,每個停止條件對應一個勾選欄,方便事後審計。

### Action Items

1. [ ] 修正三個紀錄檔 CRLF、跑 §8.3、單獨提交。
2. [ ] 驗證 VM 身分(D365APP01 / 192.168.50.20)與憑證載入。
3. [ ] 建立 appcmd backup、記錄 CRM 區段雜湊、建立獨立 DB。
4. [ ] 確認 Hosting Bundle 安裝不影響 CRM(或取得核定)。
5. [ ] 建立獨立 site 與 pool、SNI binding,比對 CRM 不變。
6. [ ] 驗證外網 DNS 與 TLS,再執行 04 檔第 1~7 步並留證據。
7. [ ] 清理、驗證殘留為 0、秘密掃描。
8. [ ] 階段審查、推送,最後更新進度頁。

---
SESSION_ID: 3ea09366-ab37-452a-9e57-a5b5d2ab091d
