# P1-11 最終審查紀錄

主會話直接審查，非獨立第二人。逐檔檢查Schema純模型、嚴格檔案邊界、metadata來源、planner、SDK引擎、CLI及五份測試；完整檔頭與XML註解保留。

## 已修正的阻斷問題

- verify曾沿用plan而讀未發布metadata，可能將尚未可用的結構回報成功。新增實際CLI回歸，正確fixture在舊入口確認Expected1/Actual0；改為專用Verify、RetrieveAsIfPublished=false。來源欄位存在有獨立斷言，修後Deployment117/117。最初fixture誤用不存在的欄名及其失敗輸出一併保留，不當成已修成功。
- 真實組織多型lookup最多379目標、內建publisher空prefix、7259個steps分頁、同prefix不同publisher皆已各自真紅／綠修正。Core業務5000列上限沒有提高；metadata十頁／五萬列獨立預算。
- 新增12個資源後，原測試硬編12鍵造成全方案失敗；獨立完整24鍵清單與精確key集合核對修正，保留原不可變及並行斷言。
- 只啟用entity audit不足以滿足欄位稽核；建立欄位同步設定audit並精確比對。時間點UserLocal/DateAndTime，未提供任何既有日期轉換入口。
- published/unpublished涉及表功能約束不符時，apply在任何寫入之前拒絕；此投影不宣稱涵蓋所有UI未發布定義。

## 安全與生命週期核對

固定HTTPS／唯一組織白名單及server組織ID；無跨命令cache。每命令一個租約，同組織apply持原有OS寫鎖並重新讀取、完整重算plan。lease先歸還再釋放鎖。
完整候選與正式ID驗證後才發布snapshot；同步SDK不得因caller取消而提前歸還連線。A/B千次、百快照弱參照、並行同plan兩apply、錯誤保真與真共用日誌測試均通過。
只允許Create／AddSolutionComponent與定向PublishXml，不Update/Delete舊metadata或角色／安全權限。before快照CreateNew／flush(true)／close成功後才發第一個寫入；快照不是資料庫備份。
strict JSON拒重複／未知屬性、大小／深度有界；apply重驗schema hash、完整metadata fingerprint及plan hash，hash不當授權。pending key不能通過。
輸出及例外通知只保留安全摘要；不匯出業務列／secure或unsecure設定本文／trace／秘密。正常取消130，內部timeout仍通報。

## 外部審查實際狀態

依執行層要求，經專案自癒入口分別嘗試雙後端analysis及review；最新為`20261106-190143-p111-schema-published-review-reviewer`（實際run名稱以summary.json為準）。
Gemini服務錯誤／Claude配額阻擋，無任何可用輸出；dualModelComplete=false、degradedFallback=false、completedBackends=[]。不是雙模型或單模型審查成功，沒有冒稱無外部Critical。使用者已明確免除雙模型等待，繼續本機逐項自審與驗證；供應商缺證據如實保留。

## 證據與邊界

`artifacts/p1-11-20261106/solution-verified`17TRX逐筆核對4372/4372，Deployment117、原62案保留；strict0警告／錯誤；Deployment獨立coverage88.43%，Localization97.87%/98.19%。完整00§8及工具守門摘要見P1-11-acceptance.md與evidence/p111-final。
真實唯讀metadata匯出566表／22678欄、4829 filters／7259 steps，真實plan88項逐項審閱；這些不是apply或P1-12通過。
本地原始metadata被自動checkpoint納入時，改為停止追蹤並加入精確ignore；原檔仍在、SHA不變，另提交摘要與SHA，不改寫原始診斷證據。
備份／獨立復原與實機寫入屬P1-12；主P1未完成前不可歸檔主Goal。
