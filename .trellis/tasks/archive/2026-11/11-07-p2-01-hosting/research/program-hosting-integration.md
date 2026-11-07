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

## 實際結果與接續（2026-11-07）

原 Program 接入已保存於本地提交 `a1dcc3cb`；該提交在本會話接續時已存在。
紅綠燈與完整回歸原始證據均在本任務 `evidence/`，不改寫原始 log／TRX。

| 證據 | 結果與界線 |
| --- | --- |
| program-hosting-red.log | 初版測試編譯錯誤 CS0619；屬測試修正，不冒充產品行為紅燈 |
| program-hosting-behavior-red | 1／13；含 probe 缺 IServer，不能把全部失敗都算產品缺陷 |
| program-configuration-ownership | 真實未啟動 WebApplication 的組態移交探針 2／2 |
| program-cleanup-original-order-red | 抽取原清理邏輯後 8 通過／6 失敗，確實揭示漏清理與主要例外被覆蓋 |
| program-hosting-debug、program-hosting-release-final | 相關回歸各 90／90，含 15 個 Program 組合根／失敗清理案例 |
| program-hosting-foundation-release | Hosting 55／55 |
| program-solution-release-build.log | 23 警告／0 錯誤；來源證據 program-build-warning-provenance.json |
| program-file-documentation.log | 1261 檔／0 失敗 |

後續生成語系完成度與新政策的實作及驗證見 `culture-completeness-checkpoint.md`。
上述無外部模型的敘述只對應原 Program 切片；接續收到較高優先 CCG 設定後確有呼叫
外部分析與審查，但兩端均無可用輸出，不能稱完成外審。P2 保持 0／12。
