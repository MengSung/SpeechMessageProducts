# 雙模型分析與裁決
Run 20261105-084222-integration-p1-focused-analysis-analyzer：ok=true、dualModelComplete=true、quotaBlocked=false、degradedFallback=false。前一廣泛Claude兩次timeout保留原證據，縮小為完整自足設計prompt後雙方完成；不冒稱讀碼審查。
- 固定時間HMAC：接受，共用固定32byte比較、實測向量；net10可用BCL，net462 XOR無早退，不能SequenceEqual。
- SDK條件引用及assets：接受，R4評估每TFM並加還原圖驗證；host持自己的SDK依賴，Framework測載net462。
- key須最後啟用：部分採納。Dataverse key建立後服務非同步建立索引，工具不能任意手動啟用；在所有entity/attribute建立與驗證後CreateKey，verify僅Active成功。不得為通過刪key／回滾existing。
- cache generation+publish同鎖、inflight容量與owner drain：接受；同步SDK不支援取消，不能用Task.Run假裝取消或逾時後Dispose仍使用的client。來源需operation-owned有限transport timeout，不可背景遺棄工作；drain有界失敗必顯式報告，不宣稱資源回收。
- 憑證／例外／snapshot不含秘密、scope由server authorization：接受，加入A/B與negative測試。
- CLDR版本／跨target DST驗證：接受，不建立未授權排程，更新流程寫spec。
- partialapply：停止保留已新增項，重新metadata與plan冪等續作，不自動破壞性rollback。
- obsolete product overload：新呼叫必傳product，保舊路徑相容，scan新增點。
- wildcard escape：僅LIKE需處理；此四方法讀精確鍵，採eq，不擅改查詢語義。
- Account7vs5：以附錄逐欄定義與phase說明為準，schema前再核對原設計。

目前injected P0故障狀態為歷史：實際P0已封存，見2edc3470/cb932c01及P0補審summary，不重做P0。
