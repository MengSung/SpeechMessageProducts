# P1-03 LINE SDK 雙目標正式驗收

日期：2026-11-05。狀態：本工作包驗收完成；**整個 P1 尚未完成**。

## 需求 → 實作 → 測試 → 執行證據

| 需求 | 實作位置 | 決定性測試與結果 |
| --- | --- | --- |
| 六類訊息先鎖舊行為，再雙目標逐字一致 | `Line.Messaging.Tests/LineSdkGoldenTests.cs`、`Golden/*.json` | Text、Flex、Template、Image、QuickReply、RichMenu；舊 net10 capture 6/6、搬後兩宿主各 6/6；不自動更新 golden |
| 產品 net462／net10，Framework 測試宿主 net48 | 兩個 SDK csproj、`LineSdkFoundationTests.cs` | 反射驗載入產品是 net462，不把 net48 當部署輸出；本次雙宿主全套 96/96、101/101 |
| Sender 長度 1～20、https 圖示 | `Line.Messaging/Messages/Sender/Sender.cs`、`LineMessageModelTests.cs` | 邊界、過長、非 https 拒絕與序列化 |
| Background 線性漸層 | `Line.Messaging/Flex/Style/Background.cs`、`LineMessageModelTests.cs` | 實際 JSON 欄位與既有 Flex 相容 |
| webhookEventId／redelivery | `Line.Messaging/Webhooks/`、`LineSdkFoundationTests.cs` | 官方格式形狀的合成 JSON 反序列化，舊 payload 缺選用欄位仍相容 |
| 傳輸無關 raw-byte HMAC 與固定時間比對 | `Line.Messaging/LineSignature.cs` | 獨立 HMAC 向量、合法／篡改／格式錯誤／空 secret；HMAC 確定性 Dispose |
| 保留並接妥原 HTTP webhook 入口 | `Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs`、`WebhookRawSignatureTests.cs` | 先 raw bytes 驗簽再 strict UTF-8；錯誤 charset 不得重編碼，tamper 拒絕 |
| 共用 HttpClient 不跨 channel 洩漏 | `ChannelHttpTransport.cs`、Messaging／LIFF client 與 lifecycle tests | 每 request Authorization，不改 DefaultRequestHeaders；A/B 1000 次交錯、排空及 fault assertions |
| stream／response／request／content 唯一 owner | `BorrowedStreamContent.cs`、`ContentStream.cs` 及 lifecycle tests | 成功／傳輸失敗／Dispose race／重複 Dispose；借入 stream/client 不關閉，response 只轉交單一 owner |
| 失敗不可洩漏遠端 body／token／使用者 URL | `Extensions/HttpResponseMessageExtensions.cs`、client 及 lifecycle tests | HTTP 故障固定安全訊息，原始失敗仍傳回 host，不在 SDK 新建 LINE notifier |

全部要求均有實際案例；新增／保留測試未刪除以換取通過。
目前只有三份 SDK 來源使用 `#if`，未達正式計畫超過 30 檔的停止條件。
實際清單為 `BorrowedStreamContent.cs`、`LineSignature.cs`、
`Webhooks/WebhookRequestMessageHelper.cs`（只計 SDK 正式來源，排除 bin／obj）。

## TDD 與相容性證據

- `artifacts/p1-03/capture/`：舊 net10 六類合成 golden 捕獲 6/6。
  更早 `p1-03-golden-baseline.log` 曾因草稿與實際輸出不一致而 5 紅／1 綠；
  它不是成功證據，未刪除或改寫。
- `artifacts/p1-03/dual-green/`：兩目標各 6/6 逐字比對。
- `artifacts/p1-03/features-red/`：18 失敗／8 通過；
  `features-green/`、`complete/` 保存功能實作後綠燈。
- `artifacts/p1-03/lifecycle-red/`：23/23 失敗；
  `liff-red/`：11/11 失敗；`webhook-raw-red/`：1 紅／1 綠。
  對應安全修補後結果在 `security-green/`，沒有把 Mock 通過稱為 LINE 實機通過。
- 最新重新建置與完整 SDK 測試：
  `artifacts/p1-03-final-sdk.log`、`artifacts/p1-03-final-sdk/`；
  net48 載 net462 **96/96**、net10 **101/101**，0 失敗／略過。

## 審查收斂

最終 run：
`.ccg/dual-model-runs/20261105-140350-integration-p1-03-final-reviewer/`。
機器摘要為 `dualModelComplete=true`、`quotaBlocked=false`、
`degradedFallback=false`；兩模型均無新增 Critical／Warning。

- 舊 C1：raw 驗簽未接原 HTTP 入口，已修且直接入口測試證實，兩模型確認關閉。
- 舊 W2：content／response 重複 Dispose，已改單一 owner，兩模型確認關閉。
- 舊 W1：net48 測試與 net462 產品不同是 D21 核定矩陣，不是缺陷。
- disposed 的 `TryComputeLength` 改回 false 建議未提供可達故障：
  request 在整個 await 完成後才 Dispose；維持明確 ObjectDisposed 防禦誤用。
- Claude 的既有 `VerifyContentPreparationAsync` JSON catch 行為 Info
  不在本工作包異動區，保留後續追蹤，不冒稱本輪已修。
- 外模沒有執行測試、也未重新計算 hashes；主會話已本地比對 29 份固定快照，
  27 份來源／golden 正規化文字相同，2 份 csproj 僅 review 副本省略公開作者 metadata，
  實際產品仍未改。`artifacts/p1-03-review-snapshot-integrity.json`。

## 完整守門與資源契約

本輪完整方案、過渡 filter、Debug tracer、打包、各新模組／目標 coverage、
編碼及 duplicates 的逐項證據沿用同輪 `wp-reports/P1-02-acceptance.md`：
完整方案 **1836/1836**，本地 filter **1145/1145**；不是 hosted Actions。
SDK 是既有專案，未將整個歷史 SDK 宣稱為新模組 80% coverage；必要新增功能、
安全及生命週期各有直接斷言。原 NoWarn 不擴大，沒有降低既有門檻。

SDK 不保存 Session、HttpContext、租戶快取或背景佇列；
transport 由組合根管理，SDK 的 per-client credential 不寫共享 headers。
操作結束釋放 request/response；下載 response 最長由回傳 ContentStream
唯一持有至 caller Dispose；borrowed transport/stream 的最終 Dispose 由 caller 負責。
Dispose 停止新請求，不擅自取消另一 client 的共用 HttpClient。
真正傳輸失敗保持傳回 host 最終共用通知邊界，不在 SDK 自行重複通知。

修改區使用繁中維護註解、UTF-8 無 BOM／CRLF／final CRLF；
交付前再跑 Changed 編碼與重複守門，原 timestamp 模型證據不改 bytes。
本工作包不需要 VM／LINE 實際寫入，沒有實機推播或 hosted CI 證據。
未改正式計畫，不需 Word 修正報告。本工作包已知阻斷性缺陷：無。
