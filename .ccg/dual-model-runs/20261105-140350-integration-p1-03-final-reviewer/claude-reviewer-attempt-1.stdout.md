# P1-03 LINE SDK 最終補審結論（唯讀固定快照，cb932c01 基準）

## 審查範圍聲明
本輪僅依據任務提供的固定內容逐行比對，未擴讀快照外任何檔案、未執行建置/測試、未再呼叫任何模型。SHA256 清單未重新計算驗證（受限於「禁止執行」），僅信任其作為固定輸入的聲明。

---

## 舊項目關閉判定

### C1（raw 驗簽未接舊 HTTP 入口）— **判定：CLOSED**
`Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs` 第 34-46 行證據：
```csharp
var body = await request.Content.ReadAsByteArrayAsync()...
...
if (string.IsNullOrEmpty(xLineSignature) || !LineSignature.Verify(body, xLineSignature, channelSecret))
    throw new InvalidSignatureException(...);
var content = new UTF8Encoding(false, true).GetString(body);
```
驗簽序列正確：先取原始 bytes → `LineSignature.Verify` 對 raw bytes 做 HMAC → 驗證通過才以嚴格 UTF8（`throwOnInvalidBytes: true`）解碼。`WebhookRawSignatureTests.cs` 的 charset-mismatch 與 tampered-byte 兩案例與此實作一致，且是對同一 `GetWebhookEventsAsync` 公開舊入口直接測試，不是另開新 API。沒有發現第二條繞過此路徑的 webhook 解析入口殘留於快照內。**結論：關閉，證據充分。**

### W2（content 雙重 Dispose）— **判定：CLOSED**
`Line.Messaging/ContentStream.cs` Dispose 邏輯（第 150-168 行附近）：owner（response）存在時僅 `owner.Dispose()`，否則才 `stream?.Dispose()`，且在 lock 內先清空欄位再處置，防止重入重複釋放。對應 `LineMessagingClient.GetOwnedStreamAsync`：
```csharp
return new ContentStream(await response.Content.ReadAsStreamAsync()..., response.Content.Headers, response);
```
成功路徑把 response 唯一轉交 ContentStream；失敗路徑在 catch 內才 `response.Dispose()`。單一所有權轉移，無雙重釋放路徑。`Download_StreamOwnsResponse_UntilDisposed` 測試亦覆蓋此契約。**結論：關閉。**

### W1（net48 測試宿主 ≠ net462 產品）— **判定：非缺陷（既核定矩陣）**
`LineSdkFoundationTests.Assembly_TargetFramework_MatchesProduct` 以反射讀取 `TargetFrameworkAttribute` 斷言實際載入組件為 `.NETFramework,Version=v4.6.2`（NETFRAMEWORK 編譯分支），非測試宿主自身的 net48。這證明「net48 測試宿主載入 net462 產品 DLL」的事實，而非誤將測試宿主框架當作產品框架。配合 `Line.Messaging.Tests.csproj` 僅測試專案雙目標 net48/net10，產品專案 `Line.Messaging.csproj` 仍是 net462/net10，矩陣與 D21 核定一致。**結論：非本輪缺陷，不要求改產品為 net48，也未見任何「已實機沙箱測試」的失實宣稱字樣。**

### Gemini：TryComputeLength 於 disposed 後回 false 建議 — **判定：無具體可觸發的產品故障，維持現狀**
`BorrowedStreamContent.TryComputeLength` 目前對已清空的 `_stream` 丟 `ObjectDisposedException`。追蹤呼叫鏈：`BorrowedStreamContent` 僅在 `UploadRichMenuJpegImageAsync`/`PngImageAsync` 建立，交給 `HttpRequestMessage.Content` 後進入 `ChannelHttpTransport.SendAsync`：
```csharp
internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, ...)
{
    using (request) { ...; return await client.SendAsync(request, completion)...; }
}
```
`request`（與其 `Content`）的 Dispose 發生在 `client.SendAsync` **完成之後**（`using` 作用域覆蓋整個 await），而 `TryComputeLength`／`SerializeToStreamAsync` 只會在傳輸進行中、即 `client.SendAsync` 尚未返回前被呼叫。因此不存在「content 已 Dispose 但仍被要求計算長度」的可達路徑；即使 `client.SendAsync` 擲出 transport 例外，`using(request)` 的 Dispose 仍在整個呼叫結束後才執行。
**結論：Gemini 建議缺乏具體失敗場景，目前以 `ObjectDisposedException` 明確拒絕（fail-fast 防禦誤用／未來重構誤增重試路徑）優於靜默回傳 `length=0,false`，維持現狀即可，不需修改。**

---

## 本輪新增檢查（HMAC 入口 / per-request auth / 共用 HttpClient / Dispose race / 例外洩漏）

逐項覆核以下證據，**未發現新增 Critical**：

- **per-request auth 隔離**：`ChannelHttpTransport.SendAsync` 僅於單次 request 設定 `Authorization`，不寫入 `HttpClient.DefaultRequestHeaders`；`LineClientLifecycleTests.SharedTransport_TwoChannelsParallel_NoCredentialLeak` 與 `LiffAndStreamLifecycleTests.Liff_SharedTransport_ThousandRequestsRemainIsolated` 的 1000 次交錯案例與程式碼邏輯一致，無跨 channel 洩漏路徑。
- **例外不洩漏 body/token**：`HttpResponseMessageExtensions.EnsureSuccessStatusCodeAsync` 只保留 `HttpStatusCode`，不解析/回顯遠端 body；`LineMessagingClient.GetStringAsync` 對 401 另拋 `UnauthorizedAccessException` 但訊息為固定字串，不含 URL 或 userId。經比對全部對外拋出點（`LineResponseException`、`InvalidSignatureException`、`UserIdMismatchException`），皆為固定安全訊息，無格式化插入不受信任資料。
- **Dispose 冪等與借用語意**：`ChannelHttpTransport.Dispose`（`Interlocked.Exchange` 保護）、`LineMessagingClient.Dispose`/`LiffClient.Dispose` 皆冪等且不關閉借入 transport，與對應生命週期測試（`Dispose_BorrowedTransportSurvives_ClientStopsSending`、`Liff_Dispose_StopsSendingWithoutDisposingBorrowedTransport`）邏輯吻合。
- **HMAC fixed-time**：`LineSignature.Verify` 對格式/長度檢查（header 長度、Base64 合法性、digest 長度）屬非秘密早退，僅真正摘要比對使用 `CryptographicOperations.FixedTimeEquals`（net10）或手動 XOR 迴圈（net462），設計意圖與實作一致，且金鑰/摘要/輸入在 `finally` 中以 `Array.Clear` 清除。

---

## Critical 🔴
無。本輪未發現新增或未關閉的 Critical 問題。

## Warning 🟡
無新增 Warning。

## Info 🟢
1. **`LineMessagingClient.VerifyContentPreparationAsync` 的空 catch 區塊**（檔案既有程式碼，非本次 P1-03 異動範圍、SHA 清單中雖含整檔但此方法非本 WP 變更點）：JSON 解析失敗時靜默吞例外並預設回傳 `true`（視為已就緒）。非安全性問題、非 Dispose/洩漏問題，僅為既有行為；依任務範圍界定（不將 WP 外業務邏輯當本輪缺口）不計入本輪收斂判定，僅記錄供後續 WP 參考。
2. SHA256 清單真實性未獨立重算驗證（受「禁止執行」限制），本輪僅信任其作為固定輸入聲明；若需密碼學等級的輸入完整性保證，建議另由具執行權限的流程驗證。

## 總結
C1、W2 已確認關閉且有對應獨立測試支撐；W1 為既核定矩陣非缺陷；Gemini 的 TryComputeLength 建議經呼叫鏈追蹤判定無可達故障路徑，維持現狀即可。本輪未發現新增 Critical 或 Warning。P1-03 在本次唯讀補審範圍內可視為收斂完成，建議放行進入 P1 其餘項目；SDK 原始碼本輪未變更，僅為審查文件層級的最終確認。

---
SESSION_ID: cb60b235-e537-4954-9da5-747e3e4a33dd
