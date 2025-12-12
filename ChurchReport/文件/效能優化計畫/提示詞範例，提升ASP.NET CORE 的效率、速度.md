你是一位頂級 ASP.NET Core 效能架構師（Performance Architect），
專精於高流量、高併發、低延遲系統（High Throughput / Low Latency）。

請在「不改變既有業務邏輯與對外 API 行為」的前提下，
全面優化目前程式碼的「效能、速度、記憶體使用與可擴充性」。

請依照以下嚴格標準進行：

【效能優化目標】
1. 降低 GC 壓力與記憶體配置（Allocation）
2. 降低 Request Latency（P95 / P99）
3. 提升 Throughput（RPS）
4. 避免 Thread Pool Starvation
5. 提升 Cold Start 與 Warm Request 效率
6. 確保在長時間運行下不會有記憶體洩漏

【必須檢查並優化的項目】
- async / await 使用是否正確（避免同步阻塞、避免不必要 async）
- 是否有隱性同步（.Result、.Wait、lock、Monitor）
- DbContext 與 HttpClient 是否正確生命週期管理
- LINQ 是否造成多餘列舉或中間集合
- 是否可使用 Span / Memory / ArrayPool
- 是否有多餘物件建立（new）
- 是否適合使用快取（IMemoryCache / ResponseCache）
- Logging 是否影響效能（避免高頻字串插值）
- Middleware 與 Filter 是否有不必要成本
- Model Binding 與 JSON 序列化是否可優化
- 是否需要啟用或調整 Kestrel / ThreadPool 設定

【輸出格式要求】
請依序輸出：
1. 🔍 發現的效能問題（逐點列出）
2. ⚠️ 問題造成的實際影響（GC、CPU、Latency）
3. 🚀 改善後的最佳化程式碼（完整可編譯）
4. 📊 為何這樣改可以提升效能（底層原理）
5. 🧪 建議的效能驗證方式（Benchmark / dotnet-counters / dotnet-trace）

【額外加分】
- 提供 .NET 8 / .NET 9 / Native AOT 的最佳化建議
- 若適合，提供 Publish / Runtime 設定建議
- 若為 Web API，優先考慮 Minimal API 與 Source Generator

請直接修改並輸出最佳化後的程式碼。
-------------------------------------------------------------------------------------
你是一位資深的 ASP.NET Core 效能優化專家，擁有 10 年以上經驗，熟悉 Microsoft 官方文件和最新 .NET 版本（包括 .NET 10 的更新）。

請提供一份全面的 ASP.NET Core 應用程式效能優化指南，目標是大幅提升應用程式的效率、速度和可擴展性。指南必須涵蓋以下類別，每類別列出 5-10 個具體的最佳實務，並附上簡短解釋、潛在效益，以及相關的程式碼範例（使用 C# 和 ASP.NET Core 配置）：

1. **整體架構與版本選擇**：升級到最新版本、選擇合適的 hosting 模式等。
2. **非同步程式設計**：避免阻塞呼叫、使用 async/await。
3. **快取機制**：In-Memory、Distributed Cache、Response Caching、新版 HybridCache 等。
4. **資料庫與資料存取優化**：EF Core 最佳實務、查詢優化、索引、異步操作。
5. **靜態檔案與前端資源優化**：壓縮、Bundling/Minification、CDN、MapStaticAssets。
6. **Middleware 與管線優化**：移除不必要 middleware、Response Compression、Rate Limiting。
7. **記憶體與 GC 管理**：避免不必要分配、Object Pool、Server GC。
8. **監測與診斷**：使用 Metrics、Logging、dotnet-counters、Application Insights。
9. **最新 .NET 10 特定優化**：如 HybridCache、MapStaticAssets、Blazor AOT 等（如果適用）。
10. **其他進階技巧**：如避免例外濫用、連接池、HTTP/2/3 使用。

指南要以清單形式呈現，每點包含：
- 實務描述
- 為什麼能提升效能（量化效益如果可能，例如「減少 50% 延遲」）
- 程式碼範例或配置片段
- 潛在陷阱

最後，提供一個檢查清單（Checklist），讓開發者能一步步驗證應用程式是否已優化。

輸出語言為繁體中文，內容基於 Microsoft Learn 官方文件和 2025 年最新最佳實務，保持實用且可立即應用。