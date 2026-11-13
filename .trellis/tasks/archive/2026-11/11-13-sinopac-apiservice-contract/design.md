# 設計：永豐 APIService 外層服務名稱

## 邊界

只修正 `SpeechMessage.Payments` 對 Sinopac QPay 外層 envelope 的 `APIService` wire value，並補齊 provider 與 AllotQuery 測試。保留 `SinopacApiService` enum 的既有數字值作為內部識別，不允許數字值進入 JSON。Nonce、AES、Sign、內層 JSON、錯誤／重試分類與任何設定值均不在範圍。

## 實作

- 在 `SinopacPaymentProvider` 內集中以明確 `switch` 將三個 enum 成員對應為規格服務名稱。
- 先解析服務名稱，再開始 Nonce HTTP 呼叫，讓未定義 enum 值以 `ArgumentOutOfRangeException` fail fast，不經 provider retry/error normalization。
- envelope 的 `APIService` 只使用該對照結果。
- 修正 `SinopacModels.cs` 的 enum/XML 說明及 `SinopacPaymentProvider.cs` 的 wire-format 註解，使其符合 V2.6 §5.1.1 與 legacy `QPayToolkit`。

## 測試設計

- 在 `SinopacProviderTests` 以假 handler 執行公開 provider 路徑，保存第二次 Order POST 的 envelope body，只解析並斷言 `APIService`；測試不輸出完整 body。
- `Envelope_ApiService_UsesDocumentedServiceName` 對三種服務逐案斷言精確名稱。
- `Envelope_ApiService_NeverNumeric` 對所有已定義 enum 值驗證 wire mapping 非純數字，並確認未知值拋出指定程式錯誤且不送 HTTP。
- `SinopacAllotQueryTests` 的 request 與合成 response envelope 兩處 `"7"` 改為 `"AllotQuery"`。內層 JSON、Sign 與 Message 固定向量不變。

## 驗證與外部狀態

先執行測試觀察紅燈，再做最小產品修改；依序執行 provider tests、AllotQuery tests、固定 guard。離線檢查全通過後，只執行一次 Sandbox `Live_AllotQuery_SandboxRoundTrip`。禁止讀寫或記錄秘密、response body、headers、nonce、signature 或密文。依 PRD 的 A-E 分支處理後續，不因 HTTP 結果自行推測原因。
