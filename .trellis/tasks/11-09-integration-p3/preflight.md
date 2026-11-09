# 階段3前置缺項清單（附錄K）

2026-11-09 13:20 Asia/Taipei (UTC+08:00)；開工後30分鐘內唯讀複核。這是前置證據，不是P3-09驗收。使用者DMZ欄位仍是`<FQDN或IP>`／`<https://...>`模板，沒有實際端點。

| 項目 | 哪包需要 | 實際核對及狀態 | 誰做什麼 |
|---|---|---|---|
| TestChannelSecret／TestChannelToken／TokenKey（完整鍵名皆Deployment:Organizations:goodshepherdback:開頭） | 03／09 | 三鍵exists=true、format=true；值未輸出。沿用secret，不重新發行 | 已備，不需使用者操作 |
| 測試Messaging Channel ID | 09 | 非秘密1646814154；沿用同日P2-12 Verified=true紀錄，未使用token打API | 實機前核對 |
| DMZ具名主機／公開HTTPS／憑證到期 | 09 | **缺**實際端點；13:16 Hyper-V唯讀CIM成功只回主機系統，不能據此判定VM不存在或猜DMZ | 提供非秘密FQDN/IP及URL；若須對外DNS／防火牆／購證依§9-1由本人處理 |
| DMZ遠端連入／部署IIS／重啟權限 | 09 | **未驗證**：無具名端點，未嘗試遠端，故無本輪遠端錯誤碼 | 端點確認後Codex以既有身分作一次有界唯讀查詢 |
| 測試主機SQL／建庫權限 | 09 | **未驗證**：無可靠端點，不以LocalDB取代實機 | 端點確認後Codex唯讀查權限 |
| 本機MSSQLLocalDB | 04 | **通過唯讀前置**：info exit0、SQL查詢exit0、17.0.4025.3、IS_SRVROLEMEMBER(sysadmin)=1；未建庫，查後stop還原原Stopped基線 | 04直接用本機做真SQL整合測試與清理，不等遠端 |

**尚缺三類**：DMZ／HTTPS／憑證、遠端權限、測試主機SQL。請僅回非秘密端點，可貼單行：`Write-Output 'DMZ=<實際FQDN或IP>; HTTPS=<實際公開URL或未設定>; SQL=<已知端點或未知>'`。不貼帳密、token、連線字串。無需等待回覆，離線包照常進行；P3-09前全部到位才做。

**耐久證據**：本任務ignored `evidence/phase3-preflight-local-readonly.log`、`phase3-secret-key-booleans.json`；P3-00原始證據已搬至`evidence/p300-evidence/`及`evidence/p300-preflight-evidence/`，原manifest位元組SHA不變；遷移manifest SHA256 `883adcc00afe28ece3f072af9a32f4b9618c725a0357f5289ef300d32147e88e`。TEMP刪除被執行政策拒絕，沒有繞過；耐久副本已驗證可用。
