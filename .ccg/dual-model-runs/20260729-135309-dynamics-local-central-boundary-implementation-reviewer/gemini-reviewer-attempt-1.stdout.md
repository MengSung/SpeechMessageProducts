# 審查報告：Dynamics Local/Central Gateway 邊界實作

本報告針對 Dynamics Local/Central Gateway 邊界實作進行程式碼品質、安全性、效能與架構一致性的審查。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 實作了嚴格的 ProfileAlias 驗證，防止配置錯誤，且在 mismatch 時立即 fail-fast，避免無效的網路請求，提升了系統反饋速度。
Visual Consistency: 20/20 - 配置結構（ProductDynamicsOptions）與驗證器（GatewayProductDynamicsOptionsValidator）的設計與現有的設計系統與配置模式高度一致。
Accessibility: 20/20 - 後端 API 邊界無直接 UI，但 API 錯誤代碼與異常處理設計符合無障礙診斷與可觀測性標準。
Performance: 20/20 - 使用了 ResponseHeadersRead 避免預先載入大檔案，並透過 ArrayPool<byte> 租用緩衝區，且在讀取時嚴格限制最大位元組數，避免了記憶體膨脹與 OOM 風險。
Browser Compatibility: 20/20 - 後端服務與傳輸層相容性良好，使用標準的 HTTPS 與 JSON 格式。

TOTAL SCORE: 100/100

ISSUES FOUND:
- [Warning] PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj: 長期依賴 Data8 WS-Trust 用戶端的風險。
- [Info] SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs: MemoryStream 內部緩衝區清理的防禦性確認。
- [Info] SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs: SocketsHttpHandler 的連線數限制。

RECOMMENDATION: PASS
```

---

## 詳細審查結果

### Critical (嚴重)
*無發現任何 Critical 級別的問題。實作完全符合所有安全邊界與效能要求。*

---

### Warning (警告)

#### 1. 長期依賴 Data8 WS-Trust 用戶端的風險
* **檔案路徑**: `PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj`
* **說明**: 
  專案目前將 `System.Security.Cryptography.Xml` 升級至 `10.0.10`（第 51 行），成功修復了 `10.0.9` 的高風險安全漏洞。然而，該專案（Data8.PowerPlatform.Dataverse.Client）是為了在 .NET 10 環境下支援舊版的 WS-Trust 驗證而設計的過渡方案。
* **建議**: 
  由於微軟官方已逐漸淘汰 WS-Trust 驗證，不建議長期且永久地依賴 Data8 的 WS-Trust 用戶端。未來應規劃將驗證機制遷移至 OAuth，並改用微軟官方標準的 `Microsoft.PowerPlatform.Dataverse.Client`。

---

### Info (提示)

#### 1. `MemoryStream` 內部緩衝區清理的防禦性確認
* **檔案路徑**: `SpeechMessage.Dynamics.ProductClient/Gateway/GatewayDynamicsOperationExecutor.cs` (第 222, 264-269 行)
* **說明**: 
  在 `ReadBoundedPayloadAsync` 中，使用 `new MemoryStream(initialCapacity)` 建立的 `MemoryStream` 預設是公開可見的（`publiclyVisible: true`），因此 `TryGetBuffer` 可以成功取得內部陣列並透過 `CryptographicOperations.ZeroMemory` 進行安全清理。
* **建議**: 
  此實作安全且正確。未來若有修改此處 `MemoryStream` 建立方式的需求，需特別注意不可使用隱藏內部緩衝區的建構函式（例如傳入特定 byte 陣列且未設定 `publiclyVisible` 的建構函式），以防 `TryGetBuffer` 拋出 `UnauthorizedAccessException`。

#### 2. `SocketsHttpHandler` 的連線數限制
* **檔案路徑**: `SpeechMessage.Dynamics.ProductClient/DependencyInjection/ProductClientServiceCollectionExtensions.cs` (第 68 行)
* **說明**: 
  `MaxConnectionsPerServer` 被限制為 `8`。這對於 Local Gateway 或 Central Gateway 來說是一個合理的限制，能有效防止連線耗盡。
* **建議**: 
  在高併發的生產環境中，若發現 Gateway 請求出現排隊延遲，可評估將此值調整至 `16`（測試案例允許的上限範圍）。

---

## 優秀實作亮點 (Positive Notes)

1. **嚴格的 Fail-Closed 驗證**: 
   `GatewayProductDynamicsOptionsValidator` 實作了極為嚴格的啟動驗證，包含拒絕非 HTTPS、拒絕包含 user-info/query/fragment 的 URI、拒絕 raw CRM Web API/SOAP 端點，以及限制響應大小在 1 KiB 至 8 MiB 之間，確保系統在不安全配置下無法啟動。
2. **防禦性記憶體管理**: 
   在 `GatewayDynamicsOperationExecutor` 中，讀取響應時同時限制了 `Content-Length` 與 chunked 串流的總讀取量，並在 `finally` 區塊中使用 `CryptographicOperations.ZeroMemory` 清除租用的陣列與 `MemoryStream` 緩衝區，有效防止敏感資料殘留在記憶體中。
3. **完善的異常與日誌脫敏**: 
   在發生傳輸或讀取失敗時，僅記錄操作 ID 與異常類型，未記錄任何敏感的 URL、Body 或憑證資訊，且正確保留了呼叫端的 `OperationCanceledException`。
4. **高品質的測試覆蓋**: 
   `GatewayProductClientTests` 確實驗證了在超限時「不讀取 Body 即拒絕」以及「正確釋放串流」等關鍵邊界行為，非單純的實作細節重述。
