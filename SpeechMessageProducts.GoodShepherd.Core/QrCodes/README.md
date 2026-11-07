# QrCodes

本模組提供雙目標 `QrCodeContentCodec`，只處理 `QrCodeId` 參數中的內容，不處理完整 LIFF 導覽 URL、圖片或資料授權。

## P2-09a 內容相容

- 課程保留 `_` 格式及小寫 `enroll` 特例；主日保留 `@`／`_` 兩種分支；小組保留週報 ID 與簽到代碼。
- 個人單參數入口保留純 GUID；雙參數入口保留舊產生端 `GUID@尾欄`，包含 null／空尾欄及尾欄中的 `_`／`@`。尾欄不是身分，解碼僅回傳聯絡人資料庫 ID。
- 黃金 `GoodShepherd.Core.Tests/Golden/qr-content-legacy.tsv` 由父工作樹舊 Dynamics365Line 的四類產生端字串運算式、合成資料離線產生，不依賴被測 codec 的輸出。來源 SHA 與運算式記於本機 `artifacts/p2/p209a-research/source-harness-manifest.json`，報告只提交摘要與 SHA。
- 純同步呼叫最多接受 2048 個 UTF-16 字元；個人相容入口在組合前檢查長度。解碼失敗清空輸出，編碼錯誤不回顯內容。沒有快取、Session、連線或背景資源。
- GUID 語法不是驗證或授權；消費端仍須依伺服器組織與使用者權限讀取資料。三個計畫同名測試及原有案例在兩個測試宿主執行；不是圖片或 D365 實機證據。

## 階段 2 與階段 5 搬移範圍

- P2-09：QR 內容編碼放在此雙目標核心，供產生端與 ChurchReport 掃碼端共用；圖片由 net10 對外待辦處理器產生，不將 ZXing.Net／ImageSharp 帶入雙目標核心。
- P5-09：盤點 `SundayQrCodeWorkflow`、`CalculateQrCodeScanNumberWorkflow`、`PersonalQrCodeWorkflow`，重建 `MeetingQrCodeActivity`、`MeetingScanCountActivity`、`PersonalQrCodeActivity`。
- P6-01：ChurchReport 的四種 QR utility 改呼叫共用編碼，並以掃碼黃金測試證明相容。

圖片處理器等待 P2-07 與 P2-09a；此導讀不代表已通過 P2-09b 圖片或 P6-01 掃碼接入驗收。
