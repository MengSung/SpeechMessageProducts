# P5-12 一頁回報

2026-11-16 14:43 Asia/Taipei (UTC+08:00)：單人外送與活動狀態回寫、快速行銷每活動單人及分教會失敗彙總完成。兩DLL打包、停用部署及乾淨固定guard八項通過。收尾輕量檢查後才推送並核對遠端，接續P5-13。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| 單活動單人、原文保留、活動ID RetryKey、4xx與429/5xx分類 | Core net10／net48各367；十個規定同名測試、100活動與重跑測試 | 通過 |
| 六案reading、真外殼及部分信任 | Plugins615含CAS；與Core共用scenarios／expected；無legacy.json | 通過；不是舊DLL實跑 |
| 分教會失敗彙總、預設不啟用、四開關與排空 | Hosting147、Webhook122；真D365唯讀aggregate合成GUID查詢被接受，回count0 | 通過；非實機影子或推播 |
| 兩DLL17及停用部署 | package-check兩ok=true；plan 20261116-063646；apply／verify0；export 20261116-063942 | 通過；22新步驟停用、舊啟用步驟／流程皆0 |
| 乾淨固定guard | 0061cbea；Release 7115測試、Debug tracer7；編碼、重複碼、I18n、文件、進度頁皆0失敗 | 通過 |

基準reading／simulation；live=deferred、differences=null，不宣稱實機影子差異零。P5-PROP-006已核定缺口：D365需推播外殼Live查詢前拒絕，Host架構須P7前完成。23檔入站功能盤點、任意QuickCampaign systemuser來源教會與非文字／群組限制、新category狀態契約、彙總無跨程序／重啟持久去重，併階段審查，不能宣稱完整舊相容；彙總30秒為合作取消、同步SDK完成後才Dispose。全包含收尾及本次紀錄更正8筆提交，不改寫歷史；外部.turns.json保留。

本機守門證據SHA-256：`280ED9B7C4444EAA6E383AF3C180D1912901C433FBA478F7C9C807B78244FE4D`；原始輸出不提交。遠端SHA以推送後ls-remote對話為準。
