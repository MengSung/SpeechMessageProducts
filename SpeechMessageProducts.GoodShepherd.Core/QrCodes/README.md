# QrCodes

本模組是雙目標 QR 內容編碼及 QR 領域規則的預定位置；P2-02 尚未新增編碼器或圖片產生器。

## 階段 2 與階段 5 搬移範圍

- P2-09：QR 內容編碼放在此雙目標核心，供產生端與 ChurchReport 掃碼端共用；圖片由 net10 對外待辦處理器產生，不將 ZXing.Net／ImageSharp 帶入雙目標核心。
- P5-09：盤點 `SundayQrCodeWorkflow`、`CalculateQrCodeScanNumberWorkflow`、`PersonalQrCodeWorkflow`，重建 `MeetingQrCodeActivity`、`MeetingScanCountActivity`、`PersonalQrCodeActivity`。
- P6-01：ChurchReport 的四種 QR utility 改呼叫共用編碼，並以掃碼黃金測試證明相容。

QR 格式、穩定身分與錯誤處理須由 P2-09／P5-09 測試確立後再實作；此導讀不代表已通過圖片或掃碼驗收。
