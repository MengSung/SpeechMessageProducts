# P2-01 Program 共用註冊與資源所有權

## 盤點及本輪範圍

以 `c5dd2d9b` 為基線，工作樹乾淨。Main 仍直接 Attach，RunApplication 另加 singleton，
沒有使用 Hosting 註冊／adapter。原 finally 順序式清理，任一 Dispose 失敗會跳過後續資源。
本輪只改 Program 組合根、相關特性測試及進度／任務證據，不連外、不解除 P2-04 限制。

## 執行設計

1. 先新增實際組合根接點測試與 WebApplication 組態所有權探針；紅燈保存。
2. Program 建立唯一 adapter，透過 AddSpeechMessageHosting 將同一組外部 owner／adapter
   借給容器；衝突時撤回候選 registration，不部分發布、不關閉借用的 diagnostics。
3. 開關／Development guard → owner及共用註冊 → sender → ICU → Startup／HTTP 的順序不變。
4. 清理接點必須逐階嘗試解除綁定、非同步 drain、sender、尚未移交的 configuration。
   清理錯誤只有最多四個原例外；正常停止時 aggregate 原故障，已有主要例外時保留原始傳播，
   診斷管道自身已在停止，不遞迴通知，固定 stderr 回報每個失敗階段。
5. 以真實空白 WebApplication（不啟動服務）確認成功／失敗 Build 的組態接管點，
   再依實測避免 Host 與外層 Program 重複 Dispose 同一組態 provider。
6. 更新原 ICU 啟動順序斷言，不刪除安全順序要求；執行 Debug／Release 相關回歸及品質守門。

## 驗收邊界

只屬 P2-01 的主機接入進展。CultureCompleteness 生成、正式語系 guard 接入、
R2／R3／R9 搬移及整包驗收仍須繼續；P2 保持 0／12，不封存本子任務。
主會話 inline，無子代理／外部模型；獨立只讀檢查及不同組態驗證可並行。
