# P1-12 實機驗收與可復原前置

沿用使用者明確全自動授權；正式D23定義goodshepherdback是唯一開發測試組織、正式複本，須比照正式個資保護。
最新合法唯讀證據確認兩台VM可PowerShell Direct連入，API組織ID與本客體MSCRM_CONFIG的goodshepherdback完全一致。
實際DB名為GoodShepherd_MSCRM，不根據組織alias猜DB；VM亦有其他組織，不還原VM、不重啟或調整網域／帳號／安全政策。

1. 留存用途／組織ID／SQL映射及服務版本證據；只診斷D365DC01必要服務，不改DC。
2. 目標沒有現存backup歷史。使用SQL既有備份目錄建立唯一COPY_ONLY/CHECKSUM/COMPRESSION full backup，另外備份MSCRM_CONFIG；不覆寫、不打斷既有備份鏈。
3. RESTORE VERIFYONLY後，在全新P1RecoveryProbe_<GUID>資料庫驗證真正復原，所有資料／log檔使用唯一GUID路徑，禁止WITH REPLACE、禁止原DB名稱，RESTRICTED_USER且不註冊D365。
4. 檢查暫存DB ONLINE及DBCC CHECKDB PHYSICAL_ONLY；finally僅清理正式名稱與所有檔案路徑均吻合本run的暫存DB，保留兩份備份。未能清理即阻擋後續apply，不能宣稱資源已回收。
5. P1-11全部必要本機gates與自審完成後，重新唯讀plan，逐項審核hash與只新增內容；只用正式Deployment入口apply、verify全部key Active，第二輪新plan/apply零新增。
6. 後台確認SpeechMessageCore元件及安全／稽核／日期／樂觀並行政策；正式資料庫唯一鍵拒絕第二筆new_localizedtext，限定自己的合成資料與正式ID清理。不得發LINE或改舊記錄。
7. 逐項驗收、SHA證據／進度頁一般資訊、本地精確commit及子任務封存。P1-12無真正證據前不標完成。

備份與復原檢查只是實現既有安全前置，沒有改正式需求；不需Word計畫修正報告。
所有秘密只由既有User Secrets讀取、僅活於短程序；輸出不含密碼、token、個資或備份內容。備份留在客體既有SQL備份位置，不提交或送外部模型。
