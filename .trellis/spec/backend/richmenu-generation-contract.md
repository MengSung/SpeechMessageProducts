# P2-08 多語宮格產生契約

## 適用範圍與介面
RichMenus net10的`RichMenuGenerateHandler : IOutboundTaskHandler`接受已由Host授權的TextContext、教會代碼、啟用語系、八個穩定組合/頁籤及借用catalog/processor/renderer。
`BuildAlias(churchCode, combination, tab, culture)`只允許小寫ASCII英文字母、數字、底線與連字號，總長32內；標準文化尾碼轉為小寫（zh-TW→zh-tw），qps-ploc尾碼為qps。教會／組合／頁籤含大寫時拒絕，不截字或清洗業務識別，避免把不同識別暗中合併。
`RichMenuImageRenderer(Font)`以固定ImageSharp3.2.0及Drawing2.1.7繪製；官方NuGet的Drawing依賴ImageSharp>=3.1.11，未升ImageSharp。

## 輸入、輸出與所有權
每語系八頁、文化至多64、每頁1～20格、每格256字；chatBar完整取字1～14，先驗再進有截斷setter的SDK。
全部候選取字後才查provider；正式ID必須唯一，合法同名不同ID完整保留，不沿用舊Provisioning按Name GroupBy/First。
menu與alias容量各1000，先檢查需要新增的頁數，再render/create/upload。相同alias指向相同生成版本時重用；不同版本先拒絕交產品換綁workflow，不能在單純產生時暗換alias而留下直接綁定者。
PNG最大1MiB、2500×843/1686；逐張無像素池、using Image，stream成功移交handler並Dispose。Font只含固定字型，Host須提供涵蓋啟用文化的字形；不得搬OS字型入repo。
每筆scope一個handler，不留跨工作設定或token。現有processor無ct，Host必須設定有界HTTP timeout，每次await到底；取消在邊界檢查，不能WaitAsync遺棄傳輸。

## 失敗矩陣
- alias非法、重複頁、chatBar/格子越界：I/O前拒絕。
- provider重複ID、alias衝突或容量不足：render/create/upload前失敗；原例外交Worker共用例外入口，容量不會吞成成功。
- 圖面無法容納：拒絕，不截文字。render/upload/alias失敗：反序補償只刪本次自有ID；已嘗試發布的alias先核對仍指自有ID。alias異主或查刪失敗只保留清理錯誤，不刪改他人alias，仍繼續刪本次自有menu。
- 補償失敗：AggregateException保留原失敗及清理失敗，交Host告警，不假稱殘留0。

## 正常／基本／禁止
正常：同教會zh-TW與qps各八張，scope不變且字典覆寫同時影響圖面/chatBar。
基本：只啟用zh-TW就只產八張；相同版本重試無重複上傳。
禁止：對全部系統語系產圖、按顯示名稱去重、拿外部文化當授權，或改寫借用模型/全域ImageSharp設定。

## 驗證
五個計畫同名案例，另驗重複正式ID、upload故障/清理/重試、A/B各100筆屏障重疊、真ImageSharp像素差異/尺寸及未Dispose配置基線。
測試用repo既有Roboto，沒有下載或複製字型，沒有D365/LINE實機。舊RichMenus特性案例完整保留。
`Alias_CultureSuffix_UsesProviderLowercase`驗zh-TW／en-US／qps-ploc輸出；`Alias_UppercaseBusinessIdentity_RejectsRatherThanMerges`分別驗三種業務識別拒絕大寫。反例：直接送zh-TW尾碼會被LINE拒絕；正例：只標準化語系，保留業務識別的嚴格邊界。

2026-11-09實機發現alias建立及查詢回400，當次自有menu已清除；查證LINE Messaging API「Create rich menu alias」只允許`a-z`、`0-9`、`_`、`-`。本段記錄實作修正依據，不代表修正版已通過實機。
