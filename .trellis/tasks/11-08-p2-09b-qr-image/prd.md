# P2-09b QR圖片產生待辦處理器

依據：實作計畫3.4／03 P2-09b、00 §7.1；使用者已授權兩手放開，沿用已驗收P2-07與P2-09a。

## 完成定義（不加碼）
- [ ] QrImageGenerateHandler : IOutboundTaskHandler，net10以ZXing.Net＋ImageSharp產PNG；內容只經P2-09a codec。
- [ ] Generate_ImageDecodesToSameContent：真PNG經ZXing解回同一舊內容。
- [ ] Generate_ContentTooLong_RejectedBeforeRender：超過容量先拒絕，不產圖。
- [ ] Handler_ResourcesReturnToBaseline：成功／失敗／取消後圖片與stream歸零，A/B不串內容。
- [ ] 00§8.1完整乾淨守門；既有專案不另設80%門檻，無本包實機要求。
- [ ] 一頁回報、DATA同步、驗收後推送本分支並封存；不啟動P3。

時間盒半個工作天或15提交；原始TRX/log只留evidence，不提交。
