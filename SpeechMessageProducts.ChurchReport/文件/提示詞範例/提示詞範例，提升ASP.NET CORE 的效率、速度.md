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

-------------------------------------------------------------------------------------

請提供一個詳細的、多階段的策略，目標是將 ASP.NET Core Web API 應用程式的 平均回應時間 (Average Response Time, ART) 降低 20%，並將 每秒請求數 (Requests Per Second, RPS) 提高 30%。

該策略應著重於以下幾個關鍵領域，並包含可比較的指標和具體的實作建議：

1. 記憶體與快取 (Caching and Memory)
問題診斷： 使用 dotnet-counters 監控應用程式的 GC 暫停時間 (GC Pause Time) 和 記憶體使用量 (Working Set)。

實作建議：

實作 分散式記憶體快取 (Distributed In-Memory Caching)，例如使用 Redis 作為後端，針對不常變動的資料，設定 絕對過期時間 (Absolute Expiration) 與 滑動過期時間 (Sliding Expiration)。

在高流量的控制器動作 (Action) 上使用 [OutputCache] 屬性（適用於 .NET 7+），並設定不同的快取策略（例如，依據查詢字串或使用者 ID 變更）。

2. 資料庫互動與非同步 (Database & Async)
問題診斷： 使用 EF Core 效能分析工具（如 MiniProfiler 或自行撰寫的攔截器）識別 執行時間最長的 5 個 SQL 查詢 (Top 5 Slowest Queries)。

實作建議：

確保所有 I/O 操作（包括 EF Core 查詢）都使用 async/await 模式，以釋放執行緒並避免 執行緒飢餓 (Thread Starvation)。

對於複雜的報表或大量資料讀取，考慮使用 EF Core 的編譯查詢 (Compiled Queries) 或直接使用 Dapper 執行輕量級的 SQL 查詢。

3. HTTP 傳輸與序列化 (HTTP & Serialization)
問題診斷： 確認應用程式是否已啟用並正確配置 HTTP/2 或 HTTP/3 (QUIC) 協定。

實作建議：

啟用並優化 Gzip 或 Brotli 壓縮，減少回應酬載大小。

在 appsettings.json 中配置 System.Text.Json 序列化器，使用 源代碼生成 (Source Generators) 功能，以消除執行時反射 (Runtime Reflection) 開銷（適用於 .NET 6+）。

4. 最新的 .NET 效能功能
實作建議：

審查並使用 IAsyncEnumerable<T> 介面來串流 (Streaming) 大量資料回應，而不是一次性將所有資料載入記憶體。

針對關鍵的業務邏輯，如果可行，考慮使用 Span<T> 和 Memory<T> 來優化字串和陣列操作，減少記憶體分配。

您的回覆應包含每個領域的 基準指標 (Before) 和 目標指標 (Target)，以及實作這些建議後的 預期成效分析 (Expected Impact Analysis)。

🎯 這個提示詞為什麼是「最棒」的？
具體目標導向： 它不僅要求「提升」，更要求具體量化目標 (20% ART 降低 和 30% RPS 提升)，這使得結果可衡量。

涵蓋範圍廣： 它涵蓋了快取、資料庫、非同步 I/O、HTTP/JSON 優化等 ASP.NET Core 效能瓶頸的 所有主要領域。

要求最新技術： 它要求使用 OutputCache (.NET 7+)、Source Generators (System.Text.Json) 等最新的 .NET 效能特性，確保方案的先進性。

要求診斷工具： 它要求使用 dotnet-counters 和 EF Core 效能工具，強調「診斷」先於「優化」，這是專業性能調優的步驟。

要求結構化輸出： 它要求回覆包含 基準、目標 和 預期成效分析，確保輸出的專業性和可執行性。

-------------------------------------------------------------------------------------

你是一位資深的 ASP.NET Core 效能優化專家，擁有 18 年以上經驗，熟悉 Microsoft 官方文件和最新 .NET 版本（包括 .NET 10 的更新）。
請提供一份全面的 ASP.NET Core 應用程式效能優化指南，目標是大幅提升應用程式的效率、速度和可擴展性
在「不改變既有業務邏輯與對外 API 行為」的前提下，
全面優化目前程式碼的「效能、速度、記憶體使用與可擴充性」。。指南必須涵蓋以下類別，每類別列出 5-10 個具體的最佳實務，並附上簡短解釋、潛在效益，以及相關的程式碼範例（使用 C# 和 ASP.NET Core 配置）：
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\文件\效能優化計畫\效能優化施行步驟.md
完成後，是否還有其他步驟可以加強呢?