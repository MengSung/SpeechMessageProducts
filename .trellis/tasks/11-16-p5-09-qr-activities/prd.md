# P5-09 QR 活動與規則

依05A修訂2執行固定做法1～7。承接SundayQrCodeWorkflow、CalculateQrCodeScanNumberWorkflow、PersonalQrCodeWorkflow，保留InputParameter/OutputResult及各自觸發、深度、計數與寫入語意。使用既有QrCodeContentCodec與QrImageGenerate，不在沙箱畫圖。需要通知的活動Live查詢前拒絕，Shadow完整意圖（P5-PROP-006）。

## 驗收

- 四個QR具名測試與模組固定測試，Core雙宿主、真活動Shadow及CAS。
- 3～6案scenarios/expected，reading證據可追溯，不製造legacy.json。
- 兩DLL正式打包檢查；goodshepherdback停用plan/apply/verify=0，新舊啟用皆0。live=deferred，不重試追蹤。
- 固定八項guard及乾淨guard通過後才push並ls-remote，最多15提交。
- 首筆提交順手修正P5-08推送敘述及git mv封存結構，不改已推歷史。
