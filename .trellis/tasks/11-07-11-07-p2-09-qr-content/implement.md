# P2-09 QR 內容編碼切片執行計畫

## 範圍

只完成 GoodShepherd.Core 雙目標純 QR 內容編碼／解碼及黃金測試。圖片處理器依賴 P2-07 OutboundTaskWorker；P2-07 尚未完成，因此此項保持 P2-09 的必要待辦，不以內容測試代替。

## 順序

1. 讀取目前 QR 導讀、四個 ChurchReport 掃描器的解析段、適用檔案說明／隔離規範及本任務設計。
2. 新增 `QrCodeContentCodecTests.cs`，先執行 net48 與 net10 測試；確認缺少 API 的紅燈並保存 TRX。
3. 新增雙目標純核心 API 與完整繁中檔案說明、XML 契約註解；完成後重跑聚焦測試。
4. 更新 `SpeechMessageProducts.GoodShepherd.Core/QrCodes/README.md`，說明已實作的內容格式與尚未完成的圖片依賴。
5. 執行 GoodShepherd.Core.Tests net48／net10 Debug、Release，必要的產品雙目標建置，檔案說明、I18n、UTF-8 無 BOM／CRLF／檔尾 CRLF 及 `git diff --check`。
6. 檢查是否有實際 QR 樣本或正式產生器可作獨立相容證據；若沒有，報告合成向量的來源及限制，P2-09 維持進行中，直到 P2-07 圖片解碼與整合驗收完成。
7. 主會話更新同一張階段 2 摘要及 P2-09 工作包欄位；不得新增同階段一般資訊卡。

## 檔案所有權

- 本子任務唯一寫入者：主會話。
- 允許新增：`SpeechMessageProducts.GoodShepherd.Core/QrCodes/QrCodeContentCodec.cs`、`SpeechMessageProducts.GoodShepherd.Core.Tests/QrCodes/QrCodeContentCodecTests.cs`。
- 允許修改：`SpeechMessageProducts.GoodShepherd.Core/QrCodes/README.md`、本任務文件與唯一進度頁。
- 不修改：ChurchReport 舊掃描器、`SpeechMessageProducts.sln`、任何既有 P2-01／02／03 程式與測試、P2-07／圖片專案。

## 驗收命令

```powershell
dotnet test .\SpeechMessageProducts.GoodShepherd.Core.Tests\SpeechMessageProducts.GoodShepherd.Core.Tests.csproj -f net48 -c Release
dotnet test .\SpeechMessageProducts.GoodShepherd.Core.Tests\SpeechMessageProducts.GoodShepherd.Core.Tests.csproj -f net10.0 -c Release
dotnet build .\SpeechMessageProducts.GoodShepherd.Core\SpeechMessageProducts.GoodShepherd.Core.csproj -c Release -warnaserror
python tools/RepoQuality/check_file_documentation.py
powershell -NoProfile -File .\tools\RepoQuality\Test-TextEncoding.ps1 -Scope Changed -Base 62b409c80
git diff --check
```

實際命令依 repository 既有還原／測試工具選項調整並記錄。共用輸出目錄不可由並行程序同時寫入。

## 回復點與剩餘依賴

內容 codec 是獨立檔案，不重寫舊掃描器。若契約測試顯示與實際舊掃描流程衝突，先修正 API／測試再接入。圖片產生、ZXing round-trip、OutboundTask 登記與 worker 停止／清理由 P2-07/P2-09 整合階段完成；缺少項目時不得封存成整包通過。
