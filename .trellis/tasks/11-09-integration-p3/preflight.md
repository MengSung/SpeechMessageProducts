# 階段 3 前置缺項清單

2026-11-09 12:39 Asia/Taipei (UTC+08:00)；依實作計畫 3.5「00 §7.3、04 階段 3 開頭表、90 附錄 K」唯讀核對。本輪只交此頁，未啟動任何 P3 工作包，亦非 P3-09 實機驗收。

| 項目 | 適用工作包 | 狀態／證據 | 需要誰做什麼 |
|---|---|---|---|
| `Deployment:Organizations:goodshepherdback:TestChannelSecret` | P3-03、P3-09 | `exists=true; format=true`（32 碼十六進位） | 沿用既有鍵，不重新發行。 |
| `Deployment:Organizations:goodshepherdback:TestChannelToken` | P3-09 | `exists=true; format=true`（非空、無空白可列印 ASCII，至多 16 KiB） | 不使用 token 呼叫任何 API；格式不代表仍有效。 |
| `Deployment:Organizations:goodshepherdback:TokenKey` | P3-09 憑證解密 | `exists=true; format=true`（嚴格 Base64 解碼為 64 位元組） | 本輪未解密產品憑證。 |
| 測試 Messaging Channel ID（非秘密） | P3-09 | `1646814154`，數字字串；P2-12 身分證據 `Verified=true`，同日 07:57:42 操作紀錄明列核定 Channel。 | 採既有推播驗收紀錄，非 Login／MiniApp；未用目前 token 重驗所屬 Channel。 |
| DMZ 測試 IIS／HTTPS 網址／憑證到期日 | P3-09 | **未確認**具名主機；現行計畫及既有非機密文件只找到 DMZ 部署要求，沒有可靠端點。 | 擁有者只提供非機密 DMZ 主機端點及已知公開 HTTPS URL；不需帳密。 |
| 遠端連入、IIS 部署、服務重啟權限 | P3-04、P3-09 | **未驗證**：主機不明，未嘗試遠端命令，故無本輪遠端錯誤碼。本機有效系統管理員權限為 `false`；不能推論遠端權限。 | 端點確認後 Codex 以既有身分作一次有界唯讀查詢；本輪不部署、不重啟、不提權。 |
| 測試 SQL Server／建立測試資料庫權限 | P3-04、P3-09 | **未驗證**：測試 SQL 端點／執行個體尚未確認，未連線、未建立資料庫。 | 先確認 DMZ；再由 Codex 查既有非機密 SQL 端點及唯讀權限，不索取或讀取新憑證。 |

- **缺項共 3 類**：DMZ／HTTPS／憑證、遠端操作權限、SQL 端點與權限。擁有者目前只需回覆非機密端點；可貼單行 `Write-Output 'DMZ=<FQDN或IP>; HTTPS=<已知公開URL或未設定>'`，勿加入帳密。確認主機後才安排一次有界唯讀 IIS 站台／繫結／憑證與 SQL 權限查詢；不以 D365 主機名稱猜測 DMZ，也不把權限查詢當成部署或建庫驗收。
- **查核方法與界線**：由專案宣告的 User Secrets ID 定位 JSON，同一程序內解析，僅輸出上述鍵名與布林值，未執行 `dotnet user-secrets list`。12:37 本機 Hyper-V CIM 唯讀呼叫成功，但未確認任何 DMZ 身分；未將篩選結果解讀為不存在 VM。P2 歷史的 WinRM 1312 不是本輪實測。未連 D365／LINE、未變更防火牆／DNS、未讀憑證私鑰或其他帳密。
- **證據（原始輸出僅存 repo 外）**：`C:/Users/Administrator/AppData/Local/Temp/p300-preflight-evidence/`：`secret-key-booleans.json`、`local-rights-hyperv.json`、`history-source-hashes.json`、`file-documentation-check.log`。機密原值未寫入證據；上述時間為本輪查核，Channel 證據時間為既有事件。
- **Channel 依據**：既有 `.trellis/tasks/archive/2026-11/11-09-11-09-p2-12-live-acceptance/evidence/` 下的 `test-channel-identity.json` 與 `操作紀錄-20261109-075742.md`，SHA-256 已記於 repo 外索引；沒有改動歷史證據。
- **下一組離線工作**：P3-01「舊行為黃金資料」／P3-02「主機專案與端點（只組裝）」，**本輪均未開始**。P3-09 必須等前置全數到位；不以本頁宣稱 P3-00 守門或其他工作包完成。
