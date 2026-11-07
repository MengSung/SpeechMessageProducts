# P2-09a 3.4執行計畫

1. 唯讀核對舊來源四類運算式，離線harness生成八筆合成黃金；不執行舊SDK／圖片／LINE流程。
2. 新增三個計畫同名測試、嵌入黃金資源，保留全部原案例；個人相容入口先缺API／NotImplemented行為紅燈。
3. 最小實作個人尾欄相容入口，保留原一參數輸出與2048字元預算；雙宿主測試、繁中說明與位元組檢查。
4. 一頁報告、DATA及分包提交；提交後短路徑乾淨複本跑00 §8.1，通過即驗收a包並推P2分支。
5. P2-09整包要等b包；a驗收後釋放active名額，不把圖片當a的完成條件。

## 唯一檔案寫入者及界線
主代理：GoodShepherd.Core/QrCodes/QrCodeContentCodec.cs、QrCodes/README.md；GoodShepherd.Core.Tests/QrCodes/QrCodeLegacyCompatibilityTests.cs、Golden/qr-content-legacy.tsv及測試csproj的資源項；本任務與3.4 DATA。
不得改ChurchReport、Hosting、P2-03、schema／部署或秘密。P2-07後續不可同時修改本測試csproj，通用進度頁只由主代理串行寫入。

## 實際執行
2026-11-07 22:13 Asia/Taipei (UTC+08:00)：八筆來源黃金完成；兩個行為紅燈後最小實作通過。net48／net10.0各24個測試通過；原20案例不變。尚待提交後乾淨守門。
