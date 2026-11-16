# P6-01 QR 內容解析共用

2026-11-16 19:16 Asia/Taipei (UTC+08:00) 開始，時間盒 1 天，依 06A §5 P6-01。
P6-04 的七檔變更已 stash 且待 P6-PROP-007 核定；本包依使用者「等待時繼續不受影響包」
授權接續，不平行修改 P6-04，也不將其紅燈提交或計為驗收。

## 指定測試與完成清單

- [ ] `QrScan_GoldenContents_NewParserMatchesLegacyInlineParser`：連結既有 qr-content-legacy.tsv，不複製；逐欄對照原樣 LegacyInlineQrParser，含 @ 格式及底線屬性。
- [ ] `QrScan_DeclaredDifferences_AreExactlyTheListedInputs`：六類核定差異各至少一例，舊／新結果符合 06A 清單，其他黃金輸入不變。
- [ ] `QrScan_Malformed_ThrowsFormatExceptionWithoutEchoingContent`：空值、欄位不足及無效 GUID 拒絕，固定英文訊息不含原文。
- [ ] `QrUtilities_Source_NoInlineQrParsingRemains`：四檔語法樹沒有對 QrCodeIdString 的 Split 或 new Guid(arr[0])，各有 QrScanContent 委派。
- [ ] 加 ChurchReport → GoodShepherd.Core 專案參考並確認 R4。
- [ ] 新增 internal QrScanContent 的四個指定方法；只委派 Codec，不存狀態、不建立資源。
- [ ] 完整讀四個工具後，只在原切字串位置替換解析；掃描者查詢／加好友／簽到副作用順序與文字不變。
- [ ] HandleError 不依例外型別分流；若有分流照 06A 保留欄位不足的原例外。現已讀到其只依 AJAX header 分流，不依型別。
- [ ] 同步受影響檔首、成員文件及 QrCodes README；新文字 UTF-8 無 BOM／CRLF／末尾 CRLF。
- [ ] ChurchReport、GoodShepherd.Core 兩宿主、Hosting 架構測試通過。
- [ ] 程式提交後固定 guard 完整 00 §8.1；重複新增 0，無新專案、無實機。
- [ ] 一頁回報、進度 DATA、驗收時間／提交齊全後推送，ls-remote 核對。

只允許 D6-3 的 QR 格式拒絕差異，不改 NoWarn、簽到流程、畫面字句、JSON、
D365 寫入值、LINE 內容或 duplicate-baseline.json；不連 D365。
