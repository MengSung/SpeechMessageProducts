ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: annotate-richmenu-cs-files

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRichMenuAddComment

## Request
# Review Request: annotate-richmenu-cs-files

請審查目前 git diff 中所有 RichMenu 相關 C# 註解變更。

## 使用者需求
- 在 `.worktrees/Jesus_5.1.7.WorktreeRichMenuAddComment` 分支中，將所有關於 RichMenu 的 `.cs` 檔案加入詳細、完整、深入註解。
- 註解必須是繁體中文。
- 檔案編碼必須是 UTF-8 without BOM，並使用 CRLF。
- 不應改變程式行為，只允許註解與文件說明調整。

## 本地驗證摘要
- `git diff --check -- '*.cs'`: passed。
- RichMenu 關鍵字覆蓋掃描：所有命中的 `.cs` 檔案都已納入本分支 diff。
- 新增註解英文-only 掃描：passed。
- 修改後 `.cs` 檔案 UTF-8 without BOM + CRLF byte-level check: passed。
- `dotnet test Line.Messaging.Tests/Line.Messaging.Tests.csproj`: passed, 32 tests。
- `dotnet test LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj`: passed, 34 tests。
- `dotnet test LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj`: passed, 4 tests。
- `dotnet test LineMessagingProcessor.Tests/LineMessagingProcessor.Tests.csproj`: passed, 33 tests。
- `dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`: passed, 207 tests, one pre-existing xUnit1012 warning。
- `dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj`: blocked by existing target framework mismatch: ToolUtility.Tests targets net8.0 while ToolUtility targets net10.0。
- `dotnet build ToolUtility/ToolUtility.csproj`: passed。

## Review Focus
1. 是否有任何非註解行為變更。
2. 是否有 C# XML doc 放在 attribute 後面或其他語法/編譯風險。
3. 註解是否為繁體中文且有實質說明，不只是重述程式碼。
4. 是否有漏掉明顯 RichMenu 相關 `.cs` 檔案。
5. 是否有格式/編碼或 CRLF 風險。

## Diff
```diff
diff --git a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
index 4aa14d74..dba2af1a 100644
--- a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
+++ b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs
@@ -8,6 +8,12 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;
 
+/// <summary>
+/// 驗證 <see cref="LineUtilityClass"/> 與共用 LINE workflow 的整合邊界。
+///
+/// 這個測試類保留舊工具類的建構方式，同時讓通知、回覆與 RichMenu assignment
+/// 都可以被注入測試替身；如此可確認產品工具類只負責轉接，不直接碰 LINE RichMenu provider。
+/// </summary>
 public sealed class LineUtilityClassWorkflowTests
 {
     private const string LineUtilitySubjectPrefix = "\u004C\u0069\u006E\u0065\u63A8\u64AD\u7D71\u8A08:";
@@ -188,6 +194,12 @@ public sealed class LineUtilityClassWorkflowTests
             .WhoseValue.Should().Be("ChurchReport.ReplyUtility.ReplyMessage");
     }
 
+    /// <summary>
+    /// 建立可注入各種共用 workflow 的 LineUtilityClass 測試實例。
+    ///
+    /// <paramref name="lineRichMenuAssignmentWorkflow"/> 參數保留給 RichMenu 指派流程測試：
+    /// 產品工具類只應把使用者與 menu key 傳入共用 workflow，不應在測試中真的建立或刪除 LINE RichMenu。
+    /// </summary>
     private static LineUtilityClass CreateLineUtility(
         HttpClient httpClient,
         ILineNotificationWorkflow? lineNotificationWorkflow,
@@ -202,6 +214,12 @@ public sealed class LineUtilityClassWorkflowTests
         return new TestLineUtility(toolUtility, lineClient, lineNotificationWorkflow, lineReplyWorkflow, lineRichMenuAssignmentWorkflow, pushStatisticCalls);
     }
 
+    /// <summary>
+    /// 暴露受保護建構路徑的測試子類別。
+    ///
+    /// 這個子類別固定不注入舊版 create/upload/link RichMenu workflow，
+    /// 讓測試能專注於新的 assignment workflow 相依性是否正確傳遞到基底類別。
+    /// </summary>
     private sealed class TestLineUtility : LineUtilityClass
     {
         public TestLineUtility(
diff --git a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs
index b81cd076..d981eea0 100644
--- a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs
+++ b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs
@@ -7,6 +7,12 @@ using Xunit;
 
 namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;
 
+/// <summary>
+/// 驗證 <see cref="PushUtility"/> 在導入共用 LINE workflow 後仍維持舊產品語意。
+///
+/// RichMenu 相關測試特別鎖住 ChurchReport 舊版授權選單的 menu key，
+/// 確保產品端不再直接建立、上傳、刪除 RichMenu，而是委派給共用 assignment workflow。
+/// </summary>
 public sealed class PushUtilityWorkflowTests
 {
     [Fact]
@@ -223,6 +229,12 @@ public sealed class PushUtilityWorkflowTests
         workflow.Requests[0].RetryKey.Should().Be("retry-payment-001");
     }
 
+    /// <summary>
+    /// AddRichMenuMessage 應透過共用 assignment workflow 指派舊版授權選單。
+    ///
+    /// 這個測試保護遷移後的邊界：ChurchReport 仍保留原方法名稱與通知行為，
+    /// 但 RichMenu 綁定動作改由 menu key <c>legacy-auth</c> 交給共用層解析。
+    /// </summary>
     [Fact]
     public async Task AddRichMenuMessage_assigns_legacy_auth_menu_through_shared_assignment_workflow()
     {
@@ -244,6 +256,12 @@ public sealed class PushUtilityWorkflowTests
         notificationWorkflow.Requests[0].Metadata["source"].Should().Be("ChurchReport.PushUtility.AddRichMenuMessage");
     }
 
+    /// <summary>
+    /// DeleteRichMenuMessage 應透過共用 assignment workflow 解除使用者 RichMenu 綁定。
+    ///
+    /// 舊實作會取得使用者目前 richMenuId 後直接刪除 provider 資源；
+    /// 新流程只負責 unlink 使用者，避免產品端誤刪仍被其他使用者或環境共用的 RichMenu。
+    /// </summary>
     [Fact]
     public async Task DeleteRichMenuMessage_unassigns_through_shared_assignment_workflow()
     {
@@ -287,6 +305,13 @@ public sealed class PushUtilityWorkflowTests
         }
     }
 
+    /// <summary>
+    /// 捕捉舊版 create/upload/link workflow 請求的測試替身。
+    ///
+    /// 目前 PushUtility 的 RichMenu 指派已改走 assignment workflow；
+    /// 保留這個替身是為了覆蓋其他仍接受舊 workflow 相依性的建構路徑，
+    /// 並確保測試不需要真的呼叫 LINE 建立或刪除 RichMenu。
+    /// </summary>
     private sealed class CapturingRichMenuWorkflow : ILineRichMenuWorkflow
     {
         public List<LineRichMenuCreateUploadAndLinkRequest> CreateRequests { get; } = new();
@@ -318,6 +343,12 @@ public sealed class PushUtilityWorkflowTests
         }
     }
 
+    /// <summary>
+    /// 記錄 RichMenu 指派與解除指派請求的測試替身。
+    ///
+    /// 測試只關心 PushUtility 是否傳入正確的 lineUserId 與 menu key，
+    /// 因此這裡直接回傳成功結果，避免把共用 workflow 本身的行為混進產品整合測試。
+    /// </summary>
     private sealed class CapturingRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
     {
         public List<(string UserId, string MenuKey)> Assignments { get; } = new();
diff --git a/ChurchReport/Startup.cs b/ChurchReport/Startup.cs
index 4f084267..42fc52a0 100644
--- a/ChurchReport/Startup.cs
+++ b/ChurchReport/Startup.cs
@@ -494,6 +494,8 @@ namespace ChurchReport
                     Configuration["LINE_CHANNEL_ACCESS_TOKEN"] ??
                     string.Empty;
             });
+            // ChurchReport 的 RichMenu 圖片、alias 與 legacy-auth menu key 屬於產品層設定；
+            // 共用 LineMessagingProcessor.RichMenus 只負責依 catalog 佈建、快取 richMenuId 並執行指派。
             services.AddLineRichMenuProvisioning<ChurchReportLegacyRichMenuCatalog>();
             services.AddScoped<ChurchReport.Services.IChurchReportLineProfileProvider, ChurchReport.Services.ChurchReportLineProfileProvider>();
             services.AddScoped<ChurchReport.Services.IChurchReportLineBindingNotificationService, ChurchReport.Services.ChurchReportLineBindingNotificationService>();
diff --git a/ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs b/ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
index e2511b70..7dd3708d 100644
--- a/ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
+++ b/ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
@@ -14,12 +14,28 @@ namespace ChurchReport.Tools;
 /// </summary>
 public sealed class ChurchReportLegacyRichMenuCatalog : ILineRichMenuCatalog
 {
+    /// <summary>
+    /// 共用 RichMenu 工作流用來代表既有認證選單的產品層 menu key。
+    /// </summary>
     public const string LegacyAuthMenuKey = "legacy-auth";
 
+    /// <summary>
+    /// LINE RichMenu alias 的穩定識別碼，供切換動作與佈建流程共用。
+    /// </summary>
     private const string LegacyAuthAliasId = "churchreport-legacy-auth";
 
+    /// <summary>
+    /// ChurchReport 既有 RichMenu 佈署使用的 PNG 檔案路徑。
+    /// 將路徑集中在 catalog 內，未來改成內嵌資源或設定檔時，不必改動共用工作流。
+    /// </summary>
     private const string LegacyImagePath = @"D:\暫存區\richmenu.PNG";
 
+    /// <summary>
+    /// 回傳單一既有 RichMenu 定義，讓舊 ChurchReport 選單能接到共用工作流。
+    /// </summary>
+    /// <param name="cancellationToken">
+    /// 目前未使用；此 catalog 是靜態定義，只有 provisioning workflow 開啟 stream 時才讀取圖片。
+    /// </param>
     public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
     {
         IReadOnlyList<LineRichMenuDefinition> definitions = new[]
@@ -37,6 +53,10 @@ public sealed class ChurchReportLegacyRichMenuCatalog : ILineRichMenuCatalog
         return Task.FromResult(definitions);
     }
 
+    /// <summary>
+    /// 建立既有單一按鈕 RichMenu 版面。
+    /// action 覆蓋整張長版 RichMenu 圖片，使用者點任意位置都會送出舊版 postback payload。
+    /// </summary>
     private static RichMenu CreateLegacySingleButtonRichMenu()
     {
         return new RichMenu
diff --git a/ChurchReport/Tools/LineUtilityClass.cs b/ChurchReport/Tools/LineUtilityClass.cs
index 97f2ca9a..1f387df0 100644
--- a/ChurchReport/Tools/LineUtilityClass.cs
+++ b/ChurchReport/Tools/LineUtilityClass.cs
@@ -68,8 +68,16 @@ namespace ChurchReport.Tools
 
             private ILineReplyWorkflow m_LineReplyWorkflow;
 
+            /// <summary>
+            /// 舊版 RichMenu 建立/上傳/連結 workflow 的相容欄位。
+            /// 目前 legacy-auth 的一般指派改由 <see cref="m_LineRichMenuAssignmentWorkflow"/> 處理，
+            /// 此欄位仍保留給既有建構式與測試替換使用。
+            /// </summary>
             private ILineRichMenuWorkflow m_LineRichMenuWorkflow;
 
+            /// <summary>
+            /// 共用 RichMenu assignment workflow，負責解析 ChurchReport 的 menu key 並呼叫 LINE link/unlink。
+            /// </summary>
             private ILineRichMenuAssignmentWorkflow m_LineRichMenuAssignmentWorkflow;
 
             private readonly bool m_UsesDefaultLineNotificationWorkflow;
@@ -82,6 +90,10 @@ namespace ChurchReport.Tools
 
             private readonly Action<string, string, string> m_CreatePushLineMessage;
 
+            /// <summary>
+            /// ChurchReport 既有認證 RichMenu 的應用程式 menu key。
+            /// 實際 LINE richMenuId 由 <see cref="ChurchReportLegacyRichMenuCatalog"/> 與 provisioning/cache 決定。
+            /// </summary>
             private const string LegacyAuthRichMenuKey = "legacy-auth";
 
             private const String WEB_LINK = @"http://www.speechmessage.com.tw";
@@ -110,13 +122,13 @@ namespace ChurchReport.Tools
 
             if (disposing)
             {
-                // ??? ToolUtilityClass
+                // 釋放 ToolUtilityClass。
                 m_ToolUtilityClass?.Dispose();
                 
-                // ??? LineMessagingClient
+                // 釋放 LineMessagingClient。
                 m_LineMessagingClient?.Dispose();
                 
-                // ??? ReplyUtility
+                // 釋放 ReplyUtility。
                 (m_ReplyUtility as IDisposable)?.Dispose();
             }
 
@@ -131,9 +143,8 @@ namespace ChurchReport.Tools
 
         ~LineUtilityClass()
         {
-            // Do not re-create Dispose clean-up code here.
-            // Calling Dispose(false) is optimal in terms of
-            // readability and maintainability.
+            // 解構函式只呼叫 Dispose(false)，避免重複撰寫清理邏輯。
+            // 實際資源釋放集中在 Dispose(bool)，可讀性與維護性較高。
             Dispose(false);
         }
             #endregion
@@ -265,11 +276,14 @@ namespace ChurchReport.Tools
 
             private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)
             {
+                // 保留 create/upload/link workflow 供舊呼叫端相容；目前一般切換 legacy-auth 選單走 assignment workflow。
                 return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));
             }
 
             private static ILineRichMenuAssignmentWorkflow CreateDefaultRichMenuAssignmentWorkflow(LineMessagingClient lineMessagingClient)
             {
+                // 使用產品 catalog 與共用 cache/state store 建立 assignment workflow，
+                // 讓 LineUtilityClass 不需要知道 LINE provider richMenuId 或 alias lifecycle。
                 var processor = new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient));
                 return new LineRichMenuAssignmentWorkflow(
                     processor,
@@ -293,16 +307,14 @@ namespace ChurchReport.Tools
                     }
                     else
                     {
-                        // 雿輻?身蝯?
+                        // 未指定組織時使用預設組織。
                         string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";
                         m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);
                     }
 
-                    // ?????LineMessagingClient
-                    // ?ㄐ?芣??遣?祇??交??? LineMessagingClient??
-                    // 憒??芯???ILineNotificationWorkflow 瘜典?唬?靘陷憭?蝜????澆蝡荔?
-                    // 撖阡???粥 workflow ????processor/client嚗??舫ㄐ?遣??client??
-                    // ?迨甇???亦???workflow 撅支?敹??瑕??詨???蝜?token 頝舐?賢???
+                    // 依目前 channel access token 建立 LineMessagingClient。
+                    // 若沒有外部注入 ILineNotificationWorkflow，後續會用這個 client 建立預設共用 workflow。
+                    // 重新建立預設 workflow 可避免切換 token 後，仍沿用舊 client 或舊 token。
                     m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);
                     RebuildDefaultWorkflowsForCurrentClient();
                     m_ReplyUtility = new ReplyUtility(m_LineMessagingClient, m_LineReplyWorkflow);
diff --git a/ChurchReport/Tools/PushUtility.cs b/ChurchReport/Tools/PushUtility.cs
index b24d43e0..74d35caf 100644
--- a/ChurchReport/Tools/PushUtility.cs
+++ b/ChurchReport/Tools/PushUtility.cs
@@ -14,8 +14,19 @@ namespace ChurchReport.Tools
         #region ???身摰?
         private LineMessagingClient m_LineMessagingClient { get; }
         private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
+        /// <summary>
+        /// 舊版 create/upload/link 流程的相容入口；保留欄位是為了不破壞既有建構式注入形狀。
+        /// 目前新增/刪除 legacy-auth RichMenu 主要改走 assignment workflow，避免產品層重複佈建圖片。
+        /// </summary>
         private readonly ILineRichMenuWorkflow _lineRichMenuWorkflow;
+        /// <summary>
+        /// 共用 RichMenu 指派流程，負責把 ChurchReport 使用者切到 catalog 中的 legacy-auth menu key，
+        /// 並將 provider 例外轉成一致的 RichMenu exception/result 語意。
+        /// </summary>
         private readonly ILineRichMenuAssignmentWorkflow _lineRichMenuAssignmentWorkflow;
+        /// <summary>
+        /// ChurchReport 既有認證選單的產品層 menu key；實際 richMenuId 由 catalog/provisioning/cache 解析。
+        /// </summary>
         private const string LegacyAuthRichMenuKey = "legacy-auth";
 
         public PushUtility(LineMessagingClient LineMessagingClient)
@@ -71,11 +82,14 @@ namespace ChurchReport.Tools
 
         private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)
         {
+            // 保留舊 workflow factory，讓仍注入 ILineRichMenuWorkflow 的測試或呼叫端可解析；
+            // 新的 legacy-auth 指派行為則由 assignment workflow 處理。
             return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));
         }
 
         private static ILineRichMenuAssignmentWorkflow CreateDefaultRichMenuAssignmentWorkflow(LineMessagingClient lineMessagingClient)
         {
+            // 預設 assignment workflow 使用產品 catalog 解析 legacy-auth，讓 ChurchReport 工具類不再直接操作 provider richMenuId。
             var processor = new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient));
             return new LineRichMenuAssignmentWorkflow(
                 processor,
@@ -181,22 +195,21 @@ namespace ChurchReport.Tools
         }
 
         /// <summary>
-        /// Sends a required text notification with LINE retry-key semantics.
-        /// This method is intentionally different from <see cref="SendMessage(string, string)"/>:
-        /// SendMessage is the legacy best-effort path and still swallows failures; this method is
-        /// for payment or required notifications where failure must remain visible to the caller.
+        /// 送出需要保留 LINE retry-key 語意的必要文字通知。
+        /// 這個方法刻意不同於 <see cref="SendMessage(string, string)"/>：
+        /// SendMessage 是舊版 best-effort 路徑，仍會吞掉失敗；此方法則用於付款或必要通知，
+        /// 讓傳送失敗必須對呼叫端保持可見。
         ///
-        /// When an ILineNotificationWorkflow is injected, the request is routed through the shared
-        /// product-agnostic LINE workflow with the retry key preserved. ChurchReport-specific CRM,
-        /// payment, donation, and MVC decisions stay in ChurchReport.
+        /// 注入 ILineNotificationWorkflow 時，請求會走共用且不綁定產品的 LINE workflow，
+        /// 並保留 retry key。ChurchReport 專屬的 CRM、付款、奉獻與 MVC 決策仍留在 ChurchReport。
         ///
-        /// The legacy <c>new PushUtility(client)</c> constructor now creates this shared workflow
-        /// automatically, so older call sites also use the same processor-backed path.
+        /// 舊版 <c>new PushUtility(client)</c> 建構式現在會自動建立這個共用 workflow，
+        /// 因此舊呼叫端也會使用同一條 processor-backed 路徑。
         /// </summary>
-        /// <param name="UserId">LINE user id. Required notifications must have an explicit recipient.</param>
-        /// <param name="Message">Text content to send.</param>
+        /// <param name="UserId">LINE 使用者 ID。必要通知必須有明確收件者。</param>
+        /// <param name="Message">要送出的文字內容。</param>
         /// <param name="retryKey">
-        /// LINE retry key used to identify retried sends and reduce duplicate payment notifications.
+        /// LINE retry key，用來識別重試送出並降低付款通知重複送達。
         /// </param>
         public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)
         {
diff --git a/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs b/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
index 7c95ba5f..65e0315f 100644
--- a/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
+++ b/Line.Messaging.Tests/LineMessagingClientP0EndpointTests.cs
@@ -7,6 +7,14 @@ using Xunit;
 
 namespace Line.Messaging.Tests;
 
+/// <summary>
+/// 鎖定 LINE Messaging Client 對官方 P0 端點的路徑組裝規則。
+///
+/// RichMenu 圖片、批次進度與批次驗證端點在 LINE 官方 API 中分散於
+/// <c>api.line.me</c> 與 <c>api-data.line.me</c> 兩個 host。這些測試刻意只檢查
+/// HTTP method 與 URL，避免 SDK 重構時把 RichMenu 專用端點誤接到一般訊息端點，
+/// 造成上傳圖片或查詢批次狀態時被 LINE 拒絕。
+/// </summary>
 public sealed class LineMessagingClientP0EndpointTests
 {
     [Fact]
@@ -85,6 +93,13 @@ public sealed class LineMessagingClientP0EndpointTests
             .Should().Be("https://api-data.line.me/v2/bot/message/message-123/content/preview");
     }
 
+    /// <summary>
+    /// 驗證 RichMenu 圖片下載與 JPEG/PNG 上傳都走 LINE 的 API data host。
+    ///
+    /// RichMenu 圖片內容不是一般 JSON API；官方要求使用
+    /// <c>https://api-data.line.me/v2/bot/richmenu/{richMenuId}/content</c>。
+    /// 這裡同時覆蓋 GET 與兩種 POST 上傳格式，確保共用 URL 建構邏輯不會只修到其中一條路徑。
+    /// </summary>
     [Fact]
     public async Task Rich_menu_image_download_and_upload_use_api_data_host()
     {
@@ -135,6 +150,12 @@ public sealed class LineMessagingClientP0EndpointTests
         handler.Requests.Should().BeEmpty();
     }
 
+    /// <summary>
+    /// 驗證 RichMenu 批次操作進度查詢使用官方 progress query endpoint。
+    ///
+    /// LINE 的批次進度端點把 requestId 放在 query string，而不是 REST path。
+    /// 這個測試保護批次同步流程，避免 provisioning workflow 查不到 LINE 回報的批次狀態。
+    /// </summary>
     [Fact]
     public async Task Get_rich_menu_batch_progress_uses_progress_query_endpoint()
     {
@@ -148,6 +169,13 @@ public sealed class LineMessagingClientP0EndpointTests
             .Should().Be("https://api.line.me/v2/bot/richmenu/progress/batch?requestId=request-123");
     }
 
+    /// <summary>
+    /// 驗證 RichMenu 批次請求驗證使用官方 validate/batch endpoint。
+    ///
+    /// provisioning 在送出大量 create/link/delete 前可先呼叫此端點做格式驗證；
+    /// 若 URL 多了一層或少了一層 richmenu segment，LINE 會直接拒絕請求，
+    /// 因此這裡把 POST method 與完整路徑一起鎖住。
+    /// </summary>
     [Fact]
     public async Task Validate_rich_menu_batch_uses_official_validate_batch_endpoint()
     {
diff --git a/Line.Messaging/ILineMessagingClient.cs b/Line.Messaging/ILineMessagingClient.cs
index 9f457048..2d33000b 100644
--- a/Line.Messaging/ILineMessagingClient.cs
+++ b/Line.Messaging/ILineMessagingClient.cs
@@ -376,153 +376,156 @@ namespace Line.Messaging
         #region Rich menu
 
         /// <summary>
-        /// Gets a rich menu via a rich menu ID.
+        /// 透過 LINE provider 端的 richMenuId 取得單一 RichMenu 定義。
+        /// 此方法只查詢已建立的 RichMenu metadata / area 設定，不會下載圖片內容。
         /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
         /// </summary>
-        /// <param name="richMenuId">ID of an uploaded rich menu</param>
-        /// <returns>RichMenu</returns>
+        /// <param name="richMenuId">LINE 已上傳 RichMenu 的 provider ID。</param>
+        /// <returns>RichMenu 版面與 action 設定。</returns>
         Task<RichMenu> GetRichMenuAsync(string richMenuId);
 
         /// <summary>
-        /// Creates a rich menu. 
-        /// Note: You must upload a rich menu image and link the rich menu to a user for the rich menu to be displayed.You can create up to 1000 rich menus for one bot.
-        /// The rich menu represented as a rich menu object.
+        /// 建立 RichMenu metadata 與可點擊區域設定，並回傳 LINE 產生的 richMenuId。
+        /// 建立後還必須另外上傳圖片，並將 RichMenu 綁定到使用者或設為預設選單，使用者端才會看到。
+        /// LINE 官方帳號最多可建立 1000 個 RichMenu，因此 provisioning 流程需避免重複建立。
         /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
         /// </summary>
-        /// <param name="richMenu">RichMenu</param>
-        /// <returns>RichMenu Id</returns>
+        /// <param name="richMenu">要送到 LINE 建立的 RichMenu 版面物件。</param>
+        /// <returns>LINE provider 產生的 richMenuId。</returns>
         Task<string> CreateRichMenuAsync(RichMenu richMenu);
 
         /// <summary>
-        /// Validate a rich menu object.
+        /// 驗證 RichMenu 物件是否符合 LINE 建立規則，但不實際建立。
         /// https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
         /// </summary>
-        /// <param name="richMenu">RichMenu to validate</param>
+        /// <param name="richMenu">要驗證的 RichMenu 版面物件。</param>
         Task ValidateRichMenuAsync(RichMenu richMenu);
 
         /// <summary>
-        /// Deletes a rich menu.
+        /// 刪除指定 provider richMenuId 的 RichMenu。
+        /// 此操作會移除 LINE 端資源；若同一 RichMenu 被多位使用者或 alias 共用，呼叫端必須先確認生命週期。
         /// https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
         /// </summary>
-        /// <param name="richMenuId">RichMenu Id</param>
+        /// <param name="richMenuId">要刪除的 LINE provider richMenuId。</param>
         Task DeleteRichMenuAsync(string richMenuId);
 
         /// <summary>
-        /// Gets the ID of the rich menu linked to a user.
+        /// 查詢單一使用者目前被直接綁定的 RichMenu ID。
+        /// 回傳值是 provider richMenuId，不是應用程式 menu key 或 alias id。
         /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-id-of-user
         /// </summary>
-        /// <param name="userId">ID of the user</param>
-        /// <returns>RichMenu Id</returns>
+        /// <param name="userId">LINE webhook event 中的 userId。</param>
+        /// <returns>使用者目前綁定的 provider richMenuId。</returns>
         Task<string> GetRichMenuIdOfUserAsync(string userId);
 
         /// <summary>
-        /// Sets a default rich menu.
+        /// 設定官方帳號層級的預設 RichMenu。
+        /// 預設 RichMenu 會影響沒有個人綁定選單的使用者，屬於較大範圍的 channel 設定。
         /// https://developers.line.biz/en/reference/messaging-api/#set-default-rich-menu
         /// </summary>
-        /// <param name="richMenuId">ID of an uploaded rich menu</param>
+        /// <param name="richMenuId">要設為預設選單的 provider richMenuId。</param>
         Task SetDefaultRichMenuAsync(string richMenuId);
 
         /// <summary>
-        /// Gets the default rich menu ID.
+        /// 取得官方帳號目前設定的預設 RichMenu ID。
         /// https://developers.line.biz/en/reference/messaging-api/#get-default-rich-menu-id
         /// </summary>
-        /// <returns>Default rich menu ID</returns>
+        /// <returns>目前預設選單的 provider richMenuId。</returns>
         Task<string> GetDefaultRichMenuIdAsync();
 
         /// <summary>
-        /// Cancels the default rich menu set with the Messaging API.
+        /// 取消官方帳號目前設定的預設 RichMenu。
         /// https://developers.line.biz/en/reference/messaging-api/#cancel-default-rich-menu
         /// </summary>
         Task CancelDefaultRichMenuAsync();
 
         /// <summary>
-        /// Links a rich menu to a user.
-        /// Note: Only one rich menu can be linked to a user at one time.
+        /// 將 RichMenu 直接綁定到單一使用者。
+        /// LINE 同一時間只允許一位使用者有一個直接綁定的 RichMenu；再次綁定會覆蓋原本選單。
         /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-user
         /// </summary>
-        /// <param name="userId">ID of the user</param>
-        /// <param name="richMenuId">ID of an uploaded rich menu</param>
-        /// <returns></returns>
+        /// <param name="userId">LINE webhook event 中的 userId。</param>
+        /// <param name="richMenuId">要綁定的 provider richMenuId。</param>
         Task LinkRichMenuToUserAsync(string userId, string richMenuId);
 
         /// <summary>
-        /// Links a rich menu to multiple users.
+        /// 將同一個 RichMenu 批次綁定到多位使用者。
         /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-users
         /// </summary>
-        /// <param name="richMenuId">Rich menu ID</param>
-        /// <param name="userIds">Array of user IDs. Max: 500 users</param>
+        /// <param name="richMenuId">要綁定的 provider richMenuId。</param>
+        /// <param name="userIds">LINE userId 清單，最多 500 位使用者。</param>
         Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds);
 
         /// <summary>
-        /// Unlinks a rich menu from a user.
+        /// 解除單一使用者目前直接綁定的 RichMenu。
         /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-user
         /// </summary>
-        /// <param name="userId">ID of the user</param>
-        /// <returns></returns>
+        /// <param name="userId">LINE webhook event 中的 userId。</param>
         Task UnLinkRichMenuFromUserAsync(string userId);
 
         /// <summary>
-        /// Unlinks rich menus from multiple users.
+        /// 批次解除多位使用者目前直接綁定的 RichMenu。
         /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-users
         /// </summary>
-        /// <param name="userIds">Array of user IDs. Max: 500 users</param>
+        /// <param name="userIds">LINE userId 清單，最多 500 位使用者。</param>
         Task UnLinkRichMenuFromUsersAsync(IList<string> userIds);
 
         /// <summary>
-        /// Replace or unlink the linked rich menus in batches.
+        /// 以 batch-control API 批次替換或解除 RichMenu 綁定。
+        /// 此 API 由 LINE 非同步處理，呼叫端需另外查詢 requestId 的進度。
         /// https://developers.line.biz/en/reference/messaging-api/#batch-control-rich-menus
         /// </summary>
-        /// <param name="operations">Array of operation objects. Max: 30 operations</param>
+        /// <param name="operations">RichMenu batch operation 集合，最多 30 筆操作。</param>
         Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations);
 
         /// <summary>
-        /// Get the status of rich menu batch control.
+        /// 查詢 RichMenu batch-control request 的非同步處理狀態。
         /// https://developers.line.biz/en/reference/messaging-api/#get-batch-control-rich-menus-progress-status
         /// </summary>
-        /// <param name="requestId">Request ID returned by batch control operation</param>
-        /// <returns>Batch progress</returns>
+        /// <param name="requestId">batch-control operation 回傳的 requestId。</param>
+        /// <returns>LINE 回報的 batch 進度。</returns>
         Task<RichMenuBatchProgress> GetRichMenuBatchProgressAsync(string requestId);
 
         /// <summary>
-        /// Validate a request of rich menu batch control.
+        /// 驗證 RichMenu batch-control request 是否符合 LINE 格式要求，但不實際送出。
         /// https://developers.line.biz/en/reference/messaging-api/#validate-batch-control-rich-menus-request
         /// </summary>
-        /// <param name="operations">Array of operation objects to validate</param>
+        /// <param name="operations">要驗證的 batch operation 集合。</param>
         Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations);
 
         /// <summary>
-        /// Downloads an image associated with a rich menu.
+        /// 下載指定 RichMenu 目前關聯的圖片內容。
         /// https://developers.line.biz/en/reference/messaging-api/#download-rich-menu-image
         /// </summary>
-        /// <param name="richMenuId">RichMenu Id</param>
-        /// <returns>Image as ContentStream</returns>
+        /// <param name="richMenuId">要下載圖片的 provider richMenuId。</param>
+        /// <returns>RichMenu 圖片內容 stream。</returns>
         Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId);
 
         /// <summary>
-        /// Uploads and attaches a jpeg image to a rich menu.
-        /// Images must have one of the following resolutions: 2500x1686, 2500x843. 
-        /// You cannot replace an image attached to a rich menu.To update your rich menu image, create a new rich menu object and upload another image.
+        /// 上傳 JPEG 圖片並關聯到指定 RichMenu。
+        /// 圖片尺寸必須是 2500x1686 或 2500x843。
+        /// LINE 不允許替換已關聯圖片；若要更新圖片，需建立新的 RichMenu 並重新上傳。
         /// https://developers.line.biz/en/reference/messaging-api/#upload-rich-menu-image
         /// </summary>
-        /// <param name="stream">Jpeg image for the rich menu</param>
-        /// <param name="richMenuId">The ID of the rich menu to attach the image to.</param>
+        /// <param name="stream">要上傳的 JPEG 圖片 stream。</param>
+        /// <param name="richMenuId">要關聯圖片的 provider richMenuId。</param>
         Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId);
 
         /// <summary>
-        /// Uploads and attaches a png image to a rich menu.
-        /// Images must have one of the following resolutions: 2500x1686, 2500x843. 
-        /// You cannot replace an image attached to a rich menu.To update your rich menu image, create a new rich menu object and upload another image.
+        /// 上傳 PNG 圖片並關聯到指定 RichMenu。
+        /// 圖片尺寸必須是 2500x1686 或 2500x843。
+        /// LINE 不允許替換已關聯圖片；若要更新圖片，需建立新的 RichMenu 並重新上傳。
         /// https://developers.line.biz/en/reference/messaging-api/#upload-rich-menu-image
         /// </summary>
-        /// <param name="stream">Png image for the rich menu</param>
-        /// <param name="richMenuId">The ID of the rich menu to attach the image to.</param>
+        /// <param name="stream">要上傳的 PNG 圖片 stream。</param>
+        /// <param name="richMenuId">要關聯圖片的 provider richMenuId。</param>
         Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId);
 
         /// <summary>
-        /// Gets a list of all uploaded rich menus.
+        /// 取得官方帳號底下所有已上傳 RichMenu 的清單。
         /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-list
         /// </summary>
-        /// <returns>List of ResponseRichMenu</returns>
+        /// <returns>LINE 回傳的 ResponseRichMenu 清單。</returns>
         Task<IList<ResponseRichMenu>> GetRichMenuListAsync();
 
         #endregion
@@ -530,41 +533,41 @@ namespace Line.Messaging
         #region Rich menu alias
 
         /// <summary>
-        /// Create a rich menu alias.
+        /// 建立 RichMenu alias，讓應用程式用穩定 alias 指向 provider richMenuId。
         /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
         /// </summary>
-        /// <param name="richMenuId">Rich menu ID to be associated with the rich menu alias</param>
-        /// <param name="richMenuAliasId">Rich menu alias ID (Max: 100 characters)</param>
+        /// <param name="richMenuId">alias 要指向的 provider richMenuId。</param>
+        /// <param name="richMenuAliasId">RichMenu 別名 ID，最多 100 字元。</param>
         Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId);
 
         /// <summary>
-        /// Delete a rich menu alias.
+        /// 刪除 RichMenu alias。
         /// https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
         /// </summary>
-        /// <param name="richMenuAliasId">Rich menu alias ID to delete</param>
+        /// <param name="richMenuAliasId">要刪除的 RichMenu 別名 ID。</param>
         Task DeleteRichMenuAliasAsync(string richMenuAliasId);
 
         /// <summary>
-        /// Update a rich menu alias.
+        /// 更新 RichMenu alias 指向的 provider richMenuId。
         /// https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
         /// </summary>
-        /// <param name="richMenuAliasId">Rich menu alias ID to update</param>
-        /// <param name="richMenuId">New rich menu ID to be associated</param>
+        /// <param name="richMenuAliasId">要更新的 RichMenu 別名 ID。</param>
+        /// <param name="richMenuId">alias 新的 provider richMenuId。</param>
         Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId);
 
         /// <summary>
-        /// Get rich menu alias information.
+        /// 取得單一 RichMenu alias 的目前指向資訊。
         /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
         /// </summary>
-        /// <param name="richMenuAliasId">Rich menu alias ID</param>
-        /// <returns>Rich menu alias</returns>
+        /// <param name="richMenuAliasId">RichMenu 別名 ID。</param>
+        /// <returns>RichMenu alias 與其指向的 provider richMenuId。</returns>
         Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId);
 
         /// <summary>
-        /// Get list of rich menu aliases.
+        /// 取得官方帳號底下所有 RichMenu alias。
         /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-list
         /// </summary>
-        /// <returns>List of rich menu aliases</returns>
+        /// <returns>RichMenu alias 清單。</returns>
         Task<RichMenuAliasList> GetRichMenuAliasListAsync();
 
         #endregion
diff --git a/Line.Messaging/LineMessagingClient.cs b/Line.Messaging/LineMessagingClient.cs
index e97859ba..870a42b6 100644
--- a/Line.Messaging/LineMessagingClient.cs
+++ b/Line.Messaging/LineMessagingClient.cs
@@ -8,13 +8,12 @@ using System.Net.Http;
 using System.Net.Http.Headers;
 using System.Text;
 using System.Threading.Tasks;
-using Newtonsoft.Json.Linq; // ...added for rich menu list parsing
+using Newtonsoft.Json.Linq; // RichMenu list endpoint 回傳包在 richmenus 陣列中，需用 JObject/JArray 解析。
 
 namespace Line.Messaging
 {
     /// <summary>
-    /// LINE Messaging API 客戶端，處理與 LINE 伺服器的請求和回應
-    /// LINE Messaging API client, which handles request/response to LINE server.
+    /// LINE Messaging API 客戶端，集中處理與 LINE 伺服器的請求和回應。
     /// </summary>
     /// <remarks>
     /// 此類別提供完整的 LINE Messaging API 功能，包括：
@@ -25,17 +24,7 @@ namespace Line.Messaging
     /// - Webhook 設定
     /// - 訊息配額查詢
     /// <para>
-    /// This class provides complete LINE Messaging API functionality including:
-    /// - Message sending (reply, push, multicast, broadcast)
-    /// - User profile management
-    /// - Group and room management
-    /// - Rich Menu management
-    /// - Webhook configuration
-    /// - Message quota inquiry
-    /// </para>
-    /// <para>
     /// 官方文件：https://developers.line.biz/en/reference/messaging-api/
-    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/
     /// </para>
     /// </remarks>
     /// <example>
@@ -58,8 +47,7 @@ namespace Line.Messaging
     public class LineMessagingClient : ILineMessagingClient, IDisposable
     {
         /// <summary>
-        /// LINE API 預設 URI
-        /// Default LINE API URI
+        /// LINE JSON API 預設 URI。
         /// </summary>
         private const string DEFAULT_URI = "https://api.line.me/v2";
 
@@ -70,14 +58,12 @@ namespace Line.Messaging
         private const string DEFAULT_DATA_URI = "https://api-data.line.me/v2";
 
         /// <summary>
-        /// HTTP 客戶端，用於發送 API 請求
-        /// HTTP client for sending API requests
+        /// HTTP 客戶端，用於發送 LINE API 請求。
         /// </summary>
         private readonly HttpClient _client;
 
         /// <summary>
-        /// 是否由此類別負責釋放 HttpClient
-        /// Whether this class is responsible for disposing HttpClient
+        /// 是否由此類別負責釋放 HttpClient。
         /// </summary>
         private readonly bool _disposeClient;
 
@@ -1756,20 +1742,17 @@ namespace Line.Messaging
 
         #region Rich Menu & Alias & Batch
         /// <summary>
-        /// 取得 Rich Menu 資訊
-        /// Gets a rich menu via a rich menu ID
+        /// 透過 LINE provider 端的 richMenuId 取得 RichMenu 資訊。
+        /// 這個端點回傳版面、chat bar 文字與 action area，不包含圖片 binary。
         /// </summary>
         /// <param name="richMenuId">
-        /// 已上傳的 Rich Menu ID
-        /// ID of an uploaded rich menu
+        /// 已上傳到 LINE 的 provider richMenuId。
         /// </param>
         /// <returns>
-        /// RichMenu 物件，包含 Rich Menu 的完整設定
-        /// RichMenu object containing complete rich menu configuration
+        /// RichMenu 物件，包含 LINE 回傳的選單設定。
         /// </returns>
         /// <remarks>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
         /// </remarks>
         /// <example>
         /// <code>
@@ -1785,16 +1768,14 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 建立 Rich Menu
-        /// Creates a rich menu
+        /// 建立 RichMenu metadata 與可點擊區域設定。
+        /// 建立完成只會得到 provider richMenuId；使用者要看得到選單，還必須另外上傳圖片並綁定使用者或設定預設選單。
         /// </summary>
         /// <param name="richMenu">
-        /// Rich Menu 物件，定義選單的結構和行為
-        /// Rich menu object defining the menu structure and behavior
+        /// RichMenu 物件，定義選單尺寸、chat bar 文字與 action area。
         /// </param>
         /// <returns>
-        /// 建立的 Rich Menu ID
-        /// Created Rich Menu ID
+        /// LINE provider 產生的 richMenuId。
         /// </returns>
         /// <remarks>
         /// 注意事項：
@@ -1802,14 +1783,7 @@ namespace Line.Messaging
         /// - 一個機器人最多可建立 1000 個 Rich Menu
         /// - Rich Menu 以物件形式表示
         /// <para>
-        /// Important notes:
-        /// - Must upload rich menu image and link to user for display
-        /// - Maximum 1000 rich menus per bot
-        /// - Rich menu is represented as an object
-        /// </para>
-        /// <para>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
         /// </para>
         /// </remarks>
         /// <example>
@@ -1843,23 +1817,17 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 驗證 Rich Menu 物件
-        /// Validates a rich menu object
+        /// 驗證 RichMenu 物件是否符合 LINE 建立規則。
+        /// 此方法不會建立遠端選單，適合 provisioning 前先檢查 catalog 定義。
         /// </summary>
         /// <param name="richMenu">
-        /// 要驗證的 Rich Menu 物件
-        /// Rich menu object to validate
+        /// 要驗證的 RichMenu 版面物件。
         /// </param>
         /// <remarks>
         /// 在建立 Rich Menu 前，可先使用此方法驗證設定是否正確。
         /// 驗證不通過會拋出例外。
         /// <para>
-        /// Before creating a rich menu, use this method to validate if the configuration is correct.
-        /// Validation failure will throw an exception.
-        /// </para>
-        /// <para>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
         /// </para>
         /// </remarks>
         /// <example>
@@ -1884,21 +1852,16 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 刪除 Rich Menu
-        /// Deletes a rich menu
+        /// 刪除指定 provider richMenuId 的 RichMenu。
+        /// 呼叫前應確認此選單不是多人共用選單，也沒有仍被 alias 或使用者綁定。
         /// </summary>
         /// <param name="richMenuId">
-        /// 要刪除的 Rich Menu ID
-        /// Rich Menu ID to delete
+        /// 要刪除的 provider richMenuId。
         /// </param>
         /// <remarks>
         /// 刪除後，該 Rich Menu 將無法再使用，且已連結的使用者將不再看到此選單。
         /// <para>
-        /// After deletion, the rich menu cannot be used, and users linked to it will no longer see this menu.
-        /// </para>
-        /// <para>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
         /// </para>
         /// </remarks>
         /// <example>
@@ -1914,25 +1877,19 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 建立 Rich Menu 別名
-        /// Creates a rich menu alias
+        /// 建立 RichMenu alias。
+        /// alias 讓應用程式用穩定 ID 指向 LINE 產生的 provider richMenuId，方便未來輪替底層選單。
         /// </summary>
         /// <param name="richMenuId">
-        /// 要關聯的 Rich Menu ID
-        /// Rich menu ID to be associated with the alias
+        /// alias 要關聯的 provider richMenuId。
         /// </param>
         /// <param name="richMenuAliasId">
-        /// Rich Menu 別名 ID（最多 100 字元）
-        /// Rich menu alias ID (maximum 100 characters)
+        /// RichMenu 別名 ID，最多 100 字元。
         /// </param>
         /// <remarks>
         /// Rich Menu 別名可讓您使用自訂 ID 來管理 Rich Menu，而不需要記住系統產生的 Rich Menu ID。
         /// <para>
-        /// Rich menu alias allows you to manage rich menus using custom IDs instead of system-generated rich menu IDs.
-        /// </para>
-        /// <para>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
         /// </para>
         /// </remarks>
         /// <example>
@@ -1950,16 +1907,14 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 刪除 Rich Menu 別名
-        /// Deletes a rich menu alias
+        /// 刪除 RichMenu alias。
+        /// 此操作只移除 alias 對照，不會刪除 alias 原本指向的 provider RichMenu。
         /// </summary>
         /// <param name="richMenuAliasId">
-        /// 要刪除的 Rich Menu 別名 ID
-        /// Rich menu alias ID to delete
+        /// 要刪除的 RichMenu 別名 ID。
         /// </param>
         /// <remarks>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
         /// </para>
         /// </remarks>
         /// <example>
@@ -1975,20 +1930,17 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 更新 Rich Menu 別名
-        /// Updates a rich menu alias
+        /// 更新 RichMenu alias 指向的 provider richMenuId。
+        /// provisioning workflow 可用此方法維持 alias 穩定，同時把使用者導向新版圖稿或新版 action。
         /// </summary>
         /// <param name="richMenuAliasId">
-        /// 要更新的 Rich Menu 別名 ID
-        /// Rich menu alias ID to update
+        /// 要更新的 RichMenu 別名 ID。
         /// </param>
         /// <param name="richMenuId">
-        /// 新的 Rich Menu ID
-        /// New rich menu ID to be associated
+        /// alias 新的 provider richMenuId。
         /// </param>
         /// <remarks>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
         /// </para>
         /// </remarks>
         /// <example>
@@ -2006,20 +1958,16 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得 Rich Menu 別名資訊
-        /// Gets rich menu alias information
+        /// 取得 RichMenu alias 目前指向資訊。
         /// </summary>
         /// <param name="richMenuAliasId">
-        /// Rich Menu 別名 ID
-        /// Rich menu alias ID
+        /// RichMenu 別名 ID。
         /// </param>
         /// <returns>
-        /// RichMenuAlias 物件，包含別名和關聯的 Rich Menu ID
-        /// RichMenuAlias object containing alias and associated rich menu ID
+        /// RichMenuAlias 物件，包含 alias 與其關聯的 provider richMenuId。
         /// </returns>
         /// <remarks>
         /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
-        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
         /// </para>
         /// </remarks>
         /// <example>
@@ -2036,8 +1984,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得 Rich Menu 別名清單
-        /// Gets list of rich menu aliases
+        /// 取得官方帳號底下所有 RichMenu alias 清單。
         /// </summary>
         public virtual async Task<RichMenuAliasList> GetRichMenuAliasListAsync()
         {
@@ -2046,8 +1993,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得使用者目前連結的 Rich Menu ID
-        /// Gets the ID of the rich menu linked to a user
+        /// 取得使用者目前直接綁定的 RichMenu ID。
+        /// 回傳值是 provider richMenuId，不是應用程式 menu key 或 alias。
         /// </summary>
         public virtual async Task<string> GetRichMenuIdOfUserAsync(string userId)
         {
@@ -2056,8 +2003,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 設定預設 Rich Menu
-        /// Sets a default rich menu
+        /// 設定官方帳號層級的預設 RichMenu。
+        /// 這會影響沒有個人 RichMenu 綁定的使用者。
         /// </summary>
         public virtual async Task SetDefaultRichMenuAsync(string richMenuId)
         {
@@ -2066,8 +2013,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得預設 Rich Menu ID
-        /// Gets default rich menu ID
+        /// 取得官方帳號目前設定的預設 RichMenu ID。
         /// </summary>
         public virtual async Task<string> GetDefaultRichMenuIdAsync()
         {
@@ -2076,8 +2022,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取消預設 Rich Menu
-        /// Cancels default rich menu
+        /// 取消官方帳號目前設定的預設 RichMenu。
         /// </summary>
         public virtual async Task CancelDefaultRichMenuAsync()
         {
@@ -2086,8 +2031,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 將 Rich Menu 連結到使用者
-        /// Links a rich menu to a user
+        /// 將 RichMenu 直接連結到單一使用者。
+        /// LINE 同一時間只允許一位使用者有一個直接綁定的 RichMenu。
         /// </summary>
         public virtual async Task LinkRichMenuToUserAsync(string userId, string richMenuId)
         {
@@ -2096,8 +2041,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 將 Rich Menu 連結到多位使用者
-        /// Links a rich menu to multiple users
+        /// 將同一個 RichMenu 批次連結到多位使用者。
         /// </summary>
         public virtual async Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds)
         {
@@ -2108,8 +2052,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 將 Rich Menu 自使用者解除連結
-        /// Unlinks a rich menu from a user
+        /// 解除單一使用者目前直接連結的 RichMenu。
         /// </summary>
         public virtual async Task UnLinkRichMenuFromUserAsync(string userId)
         {
@@ -2118,8 +2061,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 將 Rich Menu 自多位使用者解除連結
-        /// Unlinks rich menus from multiple users
+        /// 批次解除多位使用者目前直接連結的 RichMenu。
         /// </summary>
         public virtual async Task UnLinkRichMenuFromUsersAsync(IList<string> userIds)
         {
@@ -2130,8 +2072,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 批次控制 Rich Menu (link/unlink/unlinkAll)
-        /// Batch control rich menus
+        /// 批次控制 RichMenu，支援 link、unlink 與 unlinkAll。
+        /// LINE 會非同步處理此請求，呼叫端應使用 progress endpoint 追蹤結果。
         /// </summary>
         public virtual async Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations)
         {
@@ -2142,8 +2084,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得批次控制進度
-        /// Gets batch control progress
+        /// 取得 RichMenu batch-control request 的處理進度。
         /// </summary>
         public virtual async Task<RichMenuBatchProgress> GetRichMenuBatchProgressAsync(string requestId)
         {
@@ -2152,8 +2093,7 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 驗證批次控制請求
-        /// Validates batch control request
+        /// 驗證 RichMenu batch-control request 是否符合 LINE 官方格式。
         /// </summary>
         public virtual async Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations)
         {
@@ -2164,8 +2104,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 下載 Rich Menu 圖片
-        /// Downloads rich menu image
+        /// 下載指定 RichMenu 的圖片內容。
+        /// 圖片端點必須走 api-data.line.me，因此這裡使用 DataUrl。
         /// </summary>
         public virtual async Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId)
         {
@@ -2175,8 +2115,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 上傳 JPEG Rich Menu 圖片
-        /// Uploads JPEG rich menu image
+        /// 上傳 JPEG RichMenu 圖片。
+        /// LINE 不允許替換既有圖片；更新圖片時需建立新的 RichMenu。
         /// </summary>
         public virtual async Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId)
         {
@@ -2187,8 +2127,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 上傳 PNG Rich Menu 圖片
-        /// Uploads PNG rich menu image
+        /// 上傳 PNG RichMenu 圖片。
+        /// 共用 provisioning workflow 目前使用 PNG 圖稿時會走此端點。
         /// </summary>
         public virtual async Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId)
         {
@@ -2199,8 +2139,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// 取得所有 Rich Menu 清單
-        /// Gets list of all uploaded rich menus
+        /// 取得官方帳號底下所有已上傳 RichMenu 清單。
+        /// LINE 回傳 JSON 外層是 richmenus 陣列，因此這裡手動解析每個項目。
         /// </summary>
         public virtual async Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
         {
diff --git a/Line.Messaging/LineObjects/ImagemapSize.cs b/Line.Messaging/LineObjects/ImagemapSize.cs
index c5472165..9dd1009f 100644
--- a/Line.Messaging/LineObjects/ImagemapSize.cs
+++ b/Line.Messaging/LineObjects/ImagemapSize.cs
@@ -1,4 +1,4 @@
-﻿namespace Line.Messaging
+namespace Line.Messaging
 {
     /// <summary>
     /// Image size. 
@@ -6,12 +6,14 @@
     public class ImagemapSize
     {
         /// <summary>
-        /// Default rich menu size
+        /// LINE RichMenu 長版預設尺寸，對應 2500x1686。
+        /// 此尺寸必須與上傳圖片和 ActionArea 座標系一致。
         /// </summary>
         public static ImagemapSize RichMenuLong { get; } = new ImagemapSize(2500, 1686);
         
         /// <summary>
-        /// Half rich menu size.
+        /// LINE RichMenu 短版尺寸，對應 2500x843。
+        /// 適合較精簡的選單；仍需使用同一套 RichMenu 座標與圖片尺寸規則。
         /// </summary>
         public static ImagemapSize RichMenuShort { get; } = new ImagemapSize(2500, 843);
 
diff --git a/Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs b/Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs
index 7926c0e2..1f65de84 100644
--- a/Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs
+++ b/Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs
@@ -1,4 +1,4 @@
-﻿using System;
+using System;
 
 namespace Line.Messaging
 {
@@ -14,7 +14,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus.Spoken when the accessibility feature is enabled on the client device.Max: 20 characters.Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </summary>
         public string Label { get; protected set; }
 
@@ -56,7 +57,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus.Spoken when the accessibility feature is enabled on the client device.Max: 20 characters.Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </param>
         /// <param name="data">
         /// String returned via webhook in the postback.data property of the postback event
@@ -91,7 +93,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus.Spoken when the accessibility feature is enabled on the client device.Max: 20 characters.Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </param>
         /// <param name="data">
         /// String returned via webhook in the postback.data property of the postback event
diff --git a/Line.Messaging/Messages/Action/MessageTemplateAction.cs b/Line.Messaging/Messages/Action/MessageTemplateAction.cs
index 45ed1e29..61daaa68 100644
--- a/Line.Messaging/Messages/Action/MessageTemplateAction.cs
+++ b/Line.Messaging/Messages/Action/MessageTemplateAction.cs
@@ -1,4 +1,4 @@
-﻿using System;
+using System;
 
 namespace Line.Messaging
 {
@@ -14,7 +14,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </summary>
         public string Label { get; }
 
@@ -31,7 +32,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </param>
         /// <param name="text">
         /// Text sent when the action is performed
diff --git a/Line.Messaging/Messages/Action/PostbackTemplateAction.cs b/Line.Messaging/Messages/Action/PostbackTemplateAction.cs
index b0ea2829..522e1b87 100644
--- a/Line.Messaging/Messages/Action/PostbackTemplateAction.cs
+++ b/Line.Messaging/Messages/Action/PostbackTemplateAction.cs
@@ -1,4 +1,4 @@
-﻿using System;
+using System;
 
 namespace Line.Messaging
 {
@@ -15,7 +15,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </summary>
         public string Label { get; }
 
@@ -46,7 +47,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </param>
         /// <param name="data">
         /// String returned via webhook in the postback.data property of the postback event
diff --git a/Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs b/Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
index 7ddd1a4e..a021979b 100644
--- a/Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
+++ b/Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
@@ -3,41 +3,46 @@ using Newtonsoft.Json;
 namespace Line.Messaging
 {
     /// <summary>
-    /// Rich menu switch action
-    /// When a control associated with this action is tapped, the rich menu switches to the rich menu specified in richMenuAliasId.
+    /// RichMenu 切換動作。
+    /// 使用者點擊綁定此 action 的區域時，LINE 會切換到 <c>richMenuAliasId</c> 指向的 RichMenu。
     /// https://developers.line.biz/en/reference/messaging-api/#richmenu-switch-action
+    /// 此 action 依賴 RichMenu alias，因此佈建流程必須先建立或更新 alias，使用者點擊時才會成功切換。
     /// </summary>
     public class RichMenuSwitchTemplateAction : ITemplateAction
     {
+        /// <summary>
+        /// 取得序列化到 LINE JSON 時使用的 RichMenu switch action 類型識別值。
+        /// </summary>
         public TemplateActionType Type { get; } = TemplateActionType.RichMenuSwitch;
 
         /// <summary>
-        /// Rich menu alias ID to switch to
+        /// 要切換到的 RichMenu alias ID。
+        /// LINE 會在使用者點擊當下解析 alias，因此佈署可輪替底層 richMenuId，而 action payload 仍維持穩定。
         /// </summary>
         [JsonProperty("richMenuAliasId")]
         public string RichMenuAliasId { get; set; }
 
         /// <summary>
-        /// Action label.
-        /// Max: 20 characters
-        /// Not displayed for rich menus. (Required for template messages, but not for rich menus)
-        /// Supported on LINE 8.11.0 and later for iOS and Android.
+        /// action 標籤。
+        /// LINE 限制最長 20 個字元；在 RichMenu 上不顯示，但 template message 仍可能需要。
+        /// iOS 與 Android 的 LINE 8.11.0 以後支援此欄位；即使未來重用在 RichMenu 以外的介面，也應保持簡短。
         /// </summary>
         [JsonProperty("label")]
         public string Label { get; set; }
 
         /// <summary>
-        /// String returned via webhook in the postback.data property of the postback event. Max: 300 characters.
+        /// 使用者點擊後，LINE webhook 會放在 postback event 的 <c>postback.data</c> 內回傳的字串。
+        /// 最長 300 個字元，可用於應用程式路由、稽核或後續流程判斷。
         /// </summary>
         [JsonProperty("data")]
         public string Data { get; set; }
 
         /// <summary>
-        /// Constructor
+        /// 建立 RichMenu switch action。
         /// </summary>
-        /// <param name="richMenuAliasId">Rich menu alias ID</param>
-        /// <param name="data">Postback data</param>
-        /// <param name="label">Action label (optional)</param>
+        /// <param name="richMenuAliasId">RichMenu alias ID。</param>
+        /// <param name="data">postback data。</param>
+        /// <param name="label">選填 action 標籤。</param>
         public RichMenuSwitchTemplateAction(string richMenuAliasId, string data, string label = null)
         {
             RichMenuAliasId = richMenuAliasId;
@@ -47,6 +52,8 @@ namespace Line.Messaging
 
         internal static RichMenuSwitchTemplateAction CreateFrom(dynamic dynamicObject)
         {
+            // LINE 回傳 malformed 或不完整 response 時，action payload 可能是 null；
+            // 這裡保留既有 nullable 行為，避免與其他 template-action parser 的相容性分歧。
             if (dynamicObject == null) return null;
             return new RichMenuSwitchTemplateAction(
                 (string)dynamicObject?.richMenuAliasId,
diff --git a/Line.Messaging/Messages/Action/TemplateActionType.cs b/Line.Messaging/Messages/Action/TemplateActionType.cs
index 7aeebd50..a7102bab 100644
--- a/Line.Messaging/Messages/Action/TemplateActionType.cs
+++ b/Line.Messaging/Messages/Action/TemplateActionType.cs
@@ -1,7 +1,12 @@
-﻿using System.Runtime.Serialization;
+using System.Runtime.Serialization;
 
 namespace Line.Messaging
 {
+    /// <summary>
+    /// LINE template action 的序列化型別。
+    /// RichMenu 使用此 enum 解析 action area 的 action type；新增 LINE action 時必須同步更新
+    /// <see cref="ActionArea.ParseTemplateAction(dynamic)"/>，否則 provider 回傳的 RichMenu area 會無法還原成正確 action 物件。
+    /// </summary>
     public enum TemplateActionType
     {
         [EnumMember(Value = "postback")]
@@ -18,6 +23,10 @@ namespace Line.Messaging
         CameraRoll,
         [EnumMember(Value = "location")]
         Location,
+        /// <summary>
+        /// LINE RichMenu switch action，對應官方 JSON 字串 <c>richmenuswitch</c>。
+        /// 此值需要搭配 RichMenu alias 使用，讓切換 action 不直接綁死 provider richMenuId。
+        /// </summary>
         [EnumMember(Value = "richmenuswitch")]
         RichMenuSwitch,
         [EnumMember(Value = "clipboard")]
diff --git a/Line.Messaging/Messages/Action/UriTemplateAction.cs b/Line.Messaging/Messages/Action/UriTemplateAction.cs
index 1f7ba8ad..fe20afa3 100644
--- a/Line.Messaging/Messages/Action/UriTemplateAction.cs
+++ b/Line.Messaging/Messages/Action/UriTemplateAction.cs
@@ -1,4 +1,4 @@
-﻿using System;
+using System;
 
 namespace Line.Messaging
 {
@@ -14,7 +14,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </summary>
         public string Label { get; }
 
@@ -41,7 +42,8 @@ namespace Line.Messaging
         /// Label for the action
         /// Required for templates other than image carousel.Max: 20 characters
         /// Optional for image carousel templates.Max: 12 characters.
-        /// Optional for rich menus. Spoken when the accessibility feature is enabled on the client device. Max: 20 characters. Supported on LINE iOS version 8.2.0 and later.
+        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
+        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
         /// </param>
         /// <param name="uri">
         /// URI opened when the action is performed (Max: 1000 characters)
diff --git a/Line.Messaging/Messages/RichMenu/ActionArea.cs b/Line.Messaging/Messages/RichMenu/ActionArea.cs
index 43e88d3f..e7c6ba93 100644
--- a/Line.Messaging/Messages/RichMenu/ActionArea.cs
+++ b/Line.Messaging/Messages/RichMenu/ActionArea.cs
@@ -1,25 +1,30 @@
-﻿using System.Collections.Generic;
+using System.Collections.Generic;
 
 namespace Line.Messaging
 {
     /// <summary>
-    /// Rich menu  Area
+    /// RichMenu 可點擊區域。
     /// https://developers.line.me/en/docs/messaging-api/reference/#area-object
+    /// ActionArea 將 RichMenu 圖片上的矩形範圍綁定到一個 LINE template action。
     /// </summary>
     public class ActionArea
     {
         /// <summary>
-        /// Object describing the boundaries of the area in pixels. See bounds object.
+        /// 以像素描述可點擊範圍邊界的物件。
+        /// Bounds 必須落在 RichMenu 圖片尺寸內；除非設計上刻意依賴 LINE 的區域排序判定，否則不應互相重疊。
         /// </summary>
         public ImagemapArea Bounds { get; set; }
 
         /// <summary>
-        /// Action performed when the area is tapped. See action objects. Note: The label field is not supported for actions in rich menus.
+        /// 使用者點擊此區域時執行的 action。
+        /// RichMenu action 不支援顯示 label；目前支援 message、URI、postback、datetime picker、RichMenu switch 與 clipboard。
         /// </summary>
         public ITemplateAction Action { get; set; }
 
         internal static ActionArea CreateFrom(dynamic dynamicObject)
         {
+            // LINE provider response 的結構與建立 payload 相近，但這裡是 dynamic JSON。
+            // 防禦式解析座標，缺少數字欄位時預設為 0，避免 parser 直接丟例外。
             return new ActionArea()
             {
                 Bounds = new ImagemapArea(
@@ -33,6 +38,8 @@ namespace Line.Messaging
 
         public static ITemplateAction ParseTemplateAction(dynamic dynamicObject)
         {
+            // LINE action type 字串決定要建立哪個具體 action 物件。
+            // 未來 SDK 新增 action type 時，這個 switch 必須與 TemplateActionType 同步更新。
             var type = (TemplateActionType)System.Enum.Parse(typeof(TemplateActionType), (string)dynamicObject?.type, true);
             switch (type)
             {
diff --git a/Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs b/Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
index e6846468..fd77393d 100644
--- a/Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
+++ b/Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
@@ -1,27 +1,29 @@
-﻿using System.Collections.Generic;
+using System.Collections.Generic;
 using System.Linq;
 
 namespace Line.Messaging
 {
     /// <summary>
-    /// Rich menu response object.
+    /// LINE 回傳的 RichMenu response 物件。
     /// https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-response-object
+    /// 在 <see cref="RichMenu"/> 的版面資料外，額外保存 LINE 建立或查詢後回傳的 provider id。
     /// </summary>
     public class ResponseRichMenu : RichMenu
     {
         /// <summary>
-        /// Rich menu ID
+        /// LINE provider 端的 RichMenu ID。
+        /// link、unlink、alias、default 與 delete 操作都必須使用這個 provider identifier。
         /// </summary>
         public string RichMenuId { get; set; }
 
         /// <summary>
-        /// Constructor
+        /// 從 provider richMenuId 與本機 RichMenu 定義建立 response 物件。
         /// </summary>
         /// <param name="richMenuId">
-        /// Rich menu ID
+        /// LINE provider 端的 RichMenu ID。
         /// </param>
         /// <param name="source">
-        /// Rich menu object
+        /// 本機 RichMenu 版面物件。
         /// </param>
         public ResponseRichMenu(string richMenuId, RichMenu source)
         {
@@ -36,6 +38,8 @@ namespace Line.Messaging
         internal static ResponseRichMenu CreateFrom(dynamic dynamicObject)
         {
 
+            // LINE 會以巢狀 JSON 回傳 action areas。
+            // 將解析集中在這裡，避免呼叫端重複 dynamic access，或不小心與 provider 欄位名稱脫節。
             var areas = new List<ActionArea>();
             foreach (var area in dynamicObject?.areas ?? Enumerable.Empty<dynamic>())
             {
diff --git a/Line.Messaging/Messages/RichMenu/RichMenu.cs b/Line.Messaging/Messages/RichMenu/RichMenu.cs
index 86d29782..e8c1f6f0 100644
--- a/Line.Messaging/Messages/RichMenu/RichMenu.cs
+++ b/Line.Messaging/Messages/RichMenu/RichMenu.cs
@@ -1,29 +1,40 @@
-﻿using System;
+using System;
 using System.Collections.Generic;
 
 namespace Line.Messaging
 {
     /// <summary>
-    /// Rich menu object
+    /// RichMenu 建立用物件。
     /// https://developers.line.me/en/docs/messaging-api/reference/#rich-menu-object
+    /// 此 model 用於建立 RichMenu 定義；LINE 建立成功後才會額外指派 provider richMenuId。
     /// </summary>
     public class RichMenu
     {
+        /// <summary>
+        /// <see cref="Name"/> 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
+        /// </summary>
         private string _name;
+
+        /// <summary>
+        /// <see cref="ChatBarText"/> 的 backing field，讓 setter 能集中套用 LINE 的長度限制。
+        /// </summary>
         private string _chatBarText;
 
         /// <summary>
-        /// size object which contains the width and height of the rich menu displayed in the chat. Rich menu images must be one of the following sizes: 2500x1686, 2500x843.
+        /// RichMenu 在聊天室顯示時的寬高尺寸。
+        /// LINE 只接受 2500x1686 或 2500x843；此尺寸必須與實際上傳的 PNG 圖片一致。
         /// </summary>
         public ImagemapSize Size { get; set; }
 
         /// <summary>
-        /// true to display the rich menu by default. Otherwise, false.
+        /// 是否預設展開 RichMenu。
+        /// true 代表 RichMenu 顯示時 chat bar 預設展開；false 則維持收合。
         /// </summary>
         public bool Selected { set; get; }
 
         /// <summary>
-        /// Name of the rich menu. This value can be used to help manage your rich menus and is not displayed to users. Maximum of 300 characters.
+        /// RichMenu 名稱，不會顯示給使用者，主要供管理與佈建比對使用；LINE 最長允許 300 個字元。
+        /// provisioning 程式可在此欄位嵌入 fingerprint，用來偵測可重用的 provider menu。
         /// </summary>
         public string Name
         {
@@ -35,7 +46,8 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// Text displayed in the chat bar. Maximum of 14 characters.
+        /// 顯示在 chat bar 的文字，LINE 最長允許 14 個字元。
+        /// RichMenu 收合時，LINE client 會顯示這段文字。
         /// </summary>
         public string ChatBarText
         {
@@ -47,17 +59,19 @@ namespace Line.Messaging
         }
 
         /// <summary>
-        /// Array of area objects which define the coordinates and size of tappable areas. Maximum of 20 area objects.
+        /// 定義可點擊區域座標與大小的 area 集合，LINE 最多允許 20 個 area。
+        /// 座標必須落在 <see cref="Size"/> 內，並與上傳的 PNG 圖稿位置對齊。
         /// </summary>
         public IList<ActionArea> Areas { set; get; }
 
         /// <summary>
-        /// Converts from RichMenu to ResponseRichMenu
+        /// 將本機 RichMenu 定義轉成 <see cref="ResponseRichMenu"/>。
+        /// 主要供測試與 adapter 使用，讓本機定義可以模擬 LINE provider-style response。
         /// </summary>
         /// <param name="richMenuId">
-        /// Rich menu ID
+        /// LINE provider 端的 RichMenu ID。
         /// </param>
-        /// <returns>ResponseRichMenu object</returns>
+        /// <returns>包含 provider richMenuId 的 response 物件。</returns>
         public ResponseRichMenu ToResponseRichMenu(string richMenuId = "")
         {
             return new ResponseRichMenu(richMenuId, this);
diff --git a/Line.Messaging/Messages/RichMenu/RichMenuAlias.cs b/Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
index 3178ee70..a8c05066 100644
--- a/Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
+++ b/Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
@@ -3,32 +3,36 @@ using Newtonsoft.Json;
 namespace Line.Messaging
 {
     /// <summary>
-    /// Rich menu alias
+    /// RichMenu 別名。
     /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
+    /// alias 提供穩定識別碼，讓 action 在 provisioning 輪替底層 provider richMenuId 後仍能引用同一個邏輯選單。
     /// </summary>
     public class RichMenuAlias
     {
         /// <summary>
-        /// Rich menu alias ID
+        /// RichMenu 別名 ID。
+        /// 此值由應用程式 catalog 控制，跨佈署應維持穩定。
         /// </summary>
         [JsonProperty("richMenuAliasId")]
         public string RichMenuAliasId { get; set; }
 
         /// <summary>
-        /// Rich menu ID
+        /// alias 目前指向的 LINE provider richMenuId。
         /// </summary>
         [JsonProperty("richMenuId")]
         public string RichMenuId { get; set; }
     }
 
     /// <summary>
-    /// Rich menu alias list
+    /// RichMenu alias 清單。
     /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-list
+    /// provisioning workflow 會讀取此清單，判斷 alias 應建立、更新或保持不變。
     /// </summary>
     public class RichMenuAliasList
     {
         /// <summary>
-        /// Array of rich menu alias objects
+        /// RichMenu alias 物件集合。
+        /// LINE 會在此集合中回傳 channel 目前的 alias 對照表。
         /// </summary>
         [JsonProperty("aliases")]
         public System.Collections.Generic.List<RichMenuAlias> Aliases { get; set; }
diff --git a/Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs b/Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs
index 866afd89..52b317aa 100644
--- a/Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs
+++ b/Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs
@@ -4,19 +4,22 @@ using System.Collections.Generic;
 namespace Line.Messaging
 {
     /// <summary>
-    /// Request body for replacing or unlinking the linked rich menus in batches
+    /// 批次替換或解除使用者 RichMenu 連結的 request body。
     /// https://developers.line.biz/en/reference/messaging-api/#batch-control-rich-menus
+    /// LINE 會非同步處理此請求；呼叫端應使用 progress endpoint 追蹤已送出的操作最後成功或失敗。
     /// </summary>
     public class RichMenuBatchRequest
     {
         /// <summary>
-        /// Array of operation objects. Max: 1000 objects
+        /// operation 物件集合，LINE 最多接受 1000 筆。
+        /// 每筆 operation 表示一個 link、unlink 或 unlink-all 指令；順序應保留為呼叫端希望 LINE 處理的順序。
         /// </summary>
         [JsonProperty("operations")]
         public List<RichMenuBatchOperation> Operations { get; set; }
 
         /// <summary>
-        /// A key that is used to resume a batch control request.
+        /// 用於恢復 batch control request 的 key。
+        /// 呼叫端重試或恢復先前已被接受的批次操作時會提供此值。
         /// </summary>
         [JsonProperty("resumeRequestKey")]
         public string ResumeRequestKey { get; set; }
@@ -24,66 +27,73 @@ namespace Line.Messaging
 
 
     /// <summary>
-    /// Rich menu batch operation
+    /// RichMenu 批次操作項目。
+    /// 表示 <see cref="RichMenuBatchRequest"/> 中的一個指令；必要欄位會依 <see cref="Type"/> 改變。
+    /// 呼叫端只能組出 LINE API 接受的欄位組合。
     /// </summary>
     public class RichMenuBatchOperation
     {
         /// <summary>
-        /// Operation type. One of:
-        /// - link: Link a rich menu to users
-        /// - unlink: Unlink a rich menu from users
-        /// - unlinkAll: Unlink rich menus from all users
+        /// 操作類型。
+        /// - link：將 RichMenu 綁定到使用者。
+        /// - unlink：解除使用者的 RichMenu 綁定。
+        /// - unlinkAll：解除所有使用者的 RichMenu 綁定。
+        /// 此字串會直接送進 JSON，必須保持 LINE API 要求的小寫格式。
         /// </summary>
         [JsonProperty("type")]
         public string Type { get; set; }
 
         /// <summary>
-        /// Rich menu ID. Required when type is link.
+        /// RichMenu ID；<see cref="Type"/> 為 link 時必填。
+        /// 這是 provider richMenuId，不是 alias id；若要用 alias-based request，請使用 <see cref="RichMenuAliasId"/>。
         /// </summary>
         [JsonProperty("richMenuId")]
         public string RichMenuId { get; set; }
 
         /// <summary>
-        /// Rich menu alias ID.
+        /// RichMenu 別名 ID。
+        /// alias id 讓 client 端切換 action 維持穩定，同時允許 provisioning 輪替底層 richMenuId。
         /// </summary>
         [JsonProperty("richMenuAliasId")]
         public string RichMenuAliasId { get; set; }
 
         /// <summary>
-        /// Array of user IDs. Required when type is link or unlink.
-        /// Use the userId values returned in webhook event objects.
-        /// Max: 500 user IDs
+        /// 使用者 ID 集合；<see cref="Type"/> 為 link 或 unlink 時必填。
+        /// 必須使用 webhook event object 內的 userId，LINE 最多接受 500 筆。
+        /// unlinkAll 是 channel-wide 操作，不應提供此欄位。
         /// </summary>
         [JsonProperty("userIds")]
         public List<string> UserIds { get; set; }
     }
 
     /// <summary>
-    /// Rich menu batch progress response
+    /// RichMenu 批次操作進度 response。
     /// https://developers.line.biz/en/reference/messaging-api/#get-batch-control-rich-menus-progress-status
+    /// LINE 接受 batch-control request 並開始非同步處理後，會透過此物件回傳進度狀態。
     /// </summary>
     public class RichMenuBatchProgress
     {
         /// <summary>
-        /// The current status of the rich menu batch control operation. One of:
-        /// - processing: Processing is in progress
-        /// - succeeded: Processing has succeeded
-        /// - failed: Processing has failed
+        /// RichMenu batch control operation 目前狀態。
+        /// - processing：處理中。
+        /// - succeeded：處理成功。
+        /// - failed：處理失敗。
+        /// LINE 未來可能擴充狀態集合，消費端遇到未知值時應採防禦式處理。
         /// </summary>
         [JsonProperty("phase")]
         public string Phase { get; set; }
 
         /// <summary>
-        /// The accepted time in milliseconds of the request of batch control the rich menu.
-        /// Format: Epoch time (milliseconds)
+        /// batch control request 被 LINE 接受的時間，單位為毫秒。
+        /// 格式為 Epoch time milliseconds；這是 provider 時間，可用於診斷與 polling log，不應作為本機業務排序依據。
         /// </summary>
         [JsonProperty("acceptedTime")]
         public long AcceptedTime { get; set; }
 
         /// <summary>
-        /// The completed time in milliseconds of batch control the rich menu. 
-        /// Returned only when phase is succeeded or failed.
-        /// Format: Epoch time (milliseconds)
+        /// RichMenu batch control 完成時間，單位為毫秒。
+        /// 僅在 phase 為 succeeded 或 failed 時回傳；格式為 Epoch time milliseconds。
+        /// null 代表 LINE 尚未完成非同步操作。
         /// </summary>
         [JsonProperty("completedTime")]
         public long? CompletedTime { get; set; }
diff --git a/Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs b/Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
index 89948335..50dedc6c 100644
--- a/Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
+++ b/Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
@@ -4,36 +4,40 @@ using System.Collections.Generic;
 namespace Line.Messaging
 {
     /// <summary>
-    /// Request body for linking rich menu to multiple users
+    /// 將 RichMenu 批次連結到多位使用者的 request body。
     /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-users
+    /// 此 DTO 會直接序列化到 LINE bulk-link endpoint，因此屬性名稱必須對齊官方 JSON contract，
+    /// 不能依本機 C# 命名偏好任意調整。
     /// </summary>
     public class RichMenuBulkLinkRequest
     {
         /// <summary>
-        /// Rich menu ID
+        /// LINE 回傳的 provider richMenuId。
+        /// 這裡不能填應用程式 menu key 或 alias id。
         /// </summary>
         [JsonProperty("richMenuId")]
         public string RichMenuId { get; set; }
 
         /// <summary>
-        /// Array of user IDs. Use the userId values returned in webhook event objects. 
-        /// Do not use the LINE ID found on LINE.
-        /// Max: 500 user IDs
+        /// 使用者 ID 集合，必須使用 webhook event object 內回傳的 userId。
+        /// 不可使用使用者自己看到的 LINE ID；LINE 最多接受 500 筆。
+        /// 呼叫端應先將大量受眾切成小批次，避免超過 API 限制而被拒絕。
         /// </summary>
         [JsonProperty("userIds")]
         public List<string> UserIds { get; set; }
     }
 
     /// <summary>
-    /// Request body for unlinking rich menu from multiple users
+    /// 批次解除多位使用者 RichMenu 連結的 request body。
     /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-users
+    /// 此 DTO 用於移除使用者與 RichMenu 的直接連結；受影響使用者會回到 channel 的 LINE 預設 RichMenu 行為。
     /// </summary>
     public class RichMenuBulkUnlinkRequest
     {
         /// <summary>
-        /// Array of user IDs. Use the userId values returned in webhook event objects. 
-        /// Do not use the LINE ID found on LINE.
-        /// Max: 500 user IDs
+        /// 使用者 ID 集合，必須使用 webhook event object 內回傳的 userId。
+        /// 不可使用使用者自己看到的 LINE ID；LINE 最多接受 500 筆。
+        /// 此清單只能包含 LINE webhook userId，顯示名稱與 LINE ID 都不是有效值。
         /// </summary>
         [JsonProperty("userIds")]
         public List<string> UserIds { get; set; }
diff --git a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs
index b1973f27..e6a6df46 100644
--- a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs
+++ b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs
@@ -8,8 +8,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.AspNetCore.Tests;
 
+/// <summary>
+/// 驗證 ASP.NET Core DI extension 對 LINE 與 RichMenu 共用工作流的註冊邊界。
+/// 這些測試鎖住「基礎 RichMenu services 可自動註冊，但產品 catalog 必須由產品明確提供」的規則。
+/// </summary>
 public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
 {
+    /// <summary>
+    /// 確認一般 LINE processor 註冊會同時提供 RichMenu workflow、assignment workflow 與文字 trigger policy。
+    /// </summary>
     [Fact]
     public void AddLineMessagingProcessor_registers_client_processor_and_workflow()
     {
@@ -32,6 +39,9 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
         provider.GetServices<IRichMenuPolicy>().Should().ContainSingle(policy => policy is LineRichMenuTextTriggerPolicy);
     }
 
+    /// <summary>
+    /// 確認產品在預設註冊後仍可覆蓋 RichMenu 文字 trigger 設定，避免 DI 註冊順序讓空設定永久生效。
+    /// </summary>
     [Fact]
     public async Task AddLineRichMenus_updates_text_trigger_options_when_called_after_default_registration()
     {
@@ -46,6 +56,8 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
         {
             options.ExactTextToMenuKey["member center"] = "member-main";
         });
+        // 測試只關心 orchestrator 能否使用覆蓋後的文字 trigger 設定，
+        // 因此以 fake processor 與可預期 cache 取代真實 LINE API 呼叫。
         services.RemoveAll<ILineRichMenuProcessor>();
         services.RemoveAll<ILineRichMenuIdCache>();
         services.AddSingleton<ILineRichMenuProcessor, FakeRichMenuProcessor>();
@@ -70,6 +82,9 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
         result.AssignedMenuKey.Should().Be("member-main");
     }
 
+    /// <summary>
+    /// 確認預設註冊在 ASP.NET Core ValidateOnBuild/ValidateScopes 下能被完整解析。
+    /// </summary>
     [Fact]
     public void AddLineMessagingProcessor_passes_aspnetcore_validate_on_build()
     {
@@ -91,6 +106,9 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
         provider.GetRequiredService<ILineRichMenuWorkflow>().Should().NotBeNull();
     }
 
+    /// <summary>
+    /// 確認 provisioning 註冊必須由產品提供 catalog，並會掛上對應的同步 workflow。
+    /// </summary>
     [Fact]
     public void AddLineRichMenuProvisioning_registers_product_catalog_and_provisioning_workflow()
     {
@@ -113,6 +131,10 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
         provider.GetRequiredService<ILineRichMenuProvisioningWorkflow>().Should().BeOfType<LineRichMenuProvisioningWorkflow>();
     }
 
+    /// <summary>
+    /// 供 DI 測試使用的 RichMenu processor 假物件。
+    /// 它不呼叫 LINE，只提供可被 assignment/orchestration services 解析的最小行為。
+    /// </summary>
     private sealed class FakeRichMenuProcessor : ILineRichMenuProcessor
     {
         public Task<string> CreateRichMenuAsync(RichMenu richMenu) => Task.FromResult("created-rich-menu");
@@ -152,6 +174,10 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests
             });
     }
 
+    /// <summary>
+    /// 供 provisioning DI 測試使用的產品 catalog 假物件。
+    /// 空清單即可證明 DI 可以解析產品 catalog 型別，不需要真的佈建 LINE RichMenu。
+    /// </summary>
     private sealed class FakeRichMenuCatalog : ILineRichMenuCatalog
     {
         public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
diff --git a/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs b/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs
index 666c2b75..2f8a3ba0 100644
--- a/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs
+++ b/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs
@@ -9,14 +9,20 @@ using Microsoft.Extensions.Options;
 namespace LineMessagingProcessor.AspNetCore;
 
 /// <summary>
-/// ASP.NET Core DI registration for the shared LINE processor family.
-/// The registration is intentionally split so future products can choose the shared RichMenu core
-/// without being forced to provide product-specific catalog/policy services at the same time.
+/// ASP.NET Core DI 註冊入口，集中註冊共用 LINE processor 家族。
+/// 註冊流程刻意拆開：未來產品可以只使用共用 RichMenu 核心，
+/// 不必同時被迫提供產品專屬的 catalog、policy 或持久化 state store。
 /// </summary>
 public static class LineMessagingProcessorServiceCollectionExtensions
 {
     private const string HttpClientName = "LineMessagingProcessor";
 
+    /// <summary>
+    /// 註冊共用 LINE Messaging client、低階 processor 與預設工作流。
+    /// RichMenu 基礎服務會在這裡一併掛上，但產品專屬 catalog 仍由 <see cref="AddLineRichMenuProvisioning{TCatalog}"/> 明確提供。
+    /// </summary>
+    /// <param name="services">ASP.NET Core service collection。</param>
+    /// <param name="configure">設定 LINE channel token 與 API base URI 的委派。</param>
     public static IServiceCollection AddLineMessagingProcessor(
         this IServiceCollection services,
         Action<LineMessagingProcessorOptions> configure)
@@ -44,16 +50,18 @@ public static class LineMessagingProcessorServiceCollectionExtensions
             new LineMessagingProcessorClass(sp.GetRequiredService<LineMessagingClient>()));
         services.AddTransient<ILineNotificationWorkflow, LineNotificationWorkflow>();
         services.AddTransient<ILineReplyWorkflow, LineReplyWorkflow>();
+        // 基礎 RichMenu 指派、文字觸發與舊版 create/upload/link workflow 可與一般 LINE workflow 一起註冊；
+        // 產品 catalog 另外註冊，避免共用套件知道產品選單內容。
         services.AddLineRichMenus();
 
         return services;
     }
 
     /// <summary>
-    /// Registers product-neutral RichMenu services.
-    /// This method does not register <see cref="ILineRichMenuCatalog"/> because catalog content is product-specific.
-    /// A future ASP.NET Core product can call this after registering LineMessagingProcessorClass, then add its own
-    /// catalog, policies, and persistent state store.
+    /// 註冊產品中立的 RichMenu services。
+    /// 此方法不註冊 <see cref="ILineRichMenuCatalog"/>，因為 catalog 內容屬於各產品。
+    /// 未來 ASP.NET Core 產品可先註冊 <see cref="LineMessagingProcessorClass"/>，
+    /// 再加上自己的 catalog、policies 與持久化 state store。
     /// </summary>
     public static IServiceCollection AddLineRichMenus(
         this IServiceCollection services,
@@ -69,9 +77,13 @@ public static class LineMessagingProcessorServiceCollectionExtensions
             var textTriggerOptions = new LineRichMenuTextTriggerOptions();
             configureTextTriggers?.Invoke(textTriggerOptions);
 
+            // 允許產品在 AddLineMessagingProcessor 之後再次呼叫 AddLineRichMenus 覆蓋文字觸發設定；
+            // 這樣 DI 註冊順序不會讓預設空設定卡住後續產品設定。
             services.RemoveAll<LineRichMenuTextTriggerOptions>();
             services.AddSingleton(textTriggerOptions);
         }
+        // TryAdd* 讓產品測試或正式環境可以替換 cache/state store/processor，
+        // 例如改用 Redis 或資料庫保存 RichMenu 狀態，而不需要 fork 共用註冊方法。
         services.TryAddSingleton<ILineRichMenuIdCache, InMemoryLineRichMenuIdCache>();
         services.TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>();
         services.TryAddTransient<ILineRichMenuProcessor, LineMessagingProcessorRichMenuAdapter>();
@@ -89,9 +101,9 @@ public static class LineMessagingProcessorServiceCollectionExtensions
     }
 
     /// <summary>
-    /// Registers RichMenu provisioning with a product-owned catalog.
-    /// Keeping this separate from AddLineRichMenus prevents the shared core from forcing every application
-    /// to define menus before it can use assignment, text trigger, or workflow services.
+    /// 使用產品擁有的 catalog 註冊 RichMenu provisioning。
+    /// 將 provisioning 與 <see cref="AddLineRichMenus"/> 分開，可避免共用核心要求每個應用程式
+    /// 必須先定義選單，才能使用 assignment、文字 trigger 或舊版 workflow services。
     /// </summary>
     public static IServiceCollection AddLineRichMenuProvisioning<TCatalog>(this IServiceCollection services)
         where TCatalog : class, ILineRichMenuCatalog
diff --git a/LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs b/LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs
index 64c55f38..616520d4 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs
@@ -5,8 +5,14 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Actions;
 
+/// <summary>
+/// 驗證 RichMenu action factory 建出的 SDK action 符合 LINE richmenuswitch JSON contract。
+/// </summary>
 public sealed class RichMenuActionFactoryTests
 {
+    /// <summary>
+    /// 確認 helper 會建立 RichMenuSwitchTemplateAction，並序列化出 LINE 需要的 richMenuAliasId 欄位。
+    /// </summary>
     [Fact]
     public void SwitchToAlias_creates_official_richmenu_switch_action()
     {
diff --git a/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
index f82e5571..9fb61503 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
@@ -18,6 +18,13 @@ namespace LineMessagingProcessor.RichMenus.Tests.Assignment;
 /// </summary>
 public sealed class LineRichMenuAssignmentWorkflowTests
 {
+    /// <summary>
+    /// 快取已有 richMenuId 時，指派流程應直接用快取值綁定使用者。
+    ///
+    /// 這是最常見的產品路徑：provisioning 已經把 menu key 與 provider richMenuId
+    /// 建立好對照，assignment workflow 只負責把使用者連到既有 RichMenu，
+    /// 不應重新建立或掃描線上選單。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_links_user_to_cached_rich_menu_id()
     {
@@ -33,6 +40,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         processor.Calls.Should().Contain("link:U123:rich-menu-001");
     }
 
+    /// <summary>
+    /// menu key 不存在時應回傳標準驗證失敗，而不是呼叫 LINE provider。
+    ///
+    /// 產品端只知道穩定的 menu key；如果這個 key 沒有被 catalog 或 cache 解析，
+    /// 代表本機設定錯誤，不能把錯誤包裝成 LINE 平台失敗。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_validation_failure_when_menu_key_is_unknown()
     {
@@ -47,6 +60,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorCode.Should().Be("line-richmenu-menu-key-not-found");
     }
 
+    /// <summary>
+    /// 快取未命中時，workflow 應能用 catalog fingerprint 從線上 RichMenu 清單復原 richMenuId。
+    ///
+    /// 這保護重啟後的冷快取情境：LINE 上已存在相同版本選單時，不需要重新建立，
+    /// 只要找回 provider id、寫回快取並繼續完成使用者綁定。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_resolves_online_rich_menu_when_cache_is_empty()
     {
@@ -75,6 +94,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         cachedRichMenuId.Should().Be("rich-menu-online");
     }
 
+    /// <summary>
+    /// AssignOrThrowAsync 在指派失敗時應丟出共用例外，並保留原始 assignment result。
+    ///
+    /// 呼叫端若採用 throw-based API，仍需要從例外取回狀態碼與錯誤碼，
+    /// 才能在產品層做一致的錯誤記錄或使用者提示。
+    /// </summary>
     [Fact]
     public async Task AssignOrThrowAsync_throws_standard_exception_when_assignment_fails()
     {
@@ -89,6 +114,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ValidationFailed);
     }
 
+    /// <summary>
+    /// 即使本機 state store 沒有紀錄，解除綁定仍必須呼叫 LINE unlink。
+    ///
+    /// state store 只是輔助追蹤上一個 menu key；LINE 端才是使用者目前實際綁定狀態。
+    /// 若本機狀態遺失就跳過 unlink，使用者會繼續留在舊 RichMenu。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_calls_line_unlink_even_when_state_store_is_empty()
     {
@@ -106,6 +137,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         processor.Calls.Should().Contain("unlink:U123");
     }
 
+    /// <summary>
+    /// state store 有紀錄時，解除綁定結果應帶回前一個 menu key 並清除本機狀態。
+    ///
+    /// 這讓呼叫端可得知解除前的產品選單來源，同時確保暫時性 RichMenu 狀態不會殘留，
+    /// 影響後續 sweep 或重新指派判斷。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_returns_previous_menu_key_and_removes_state_when_record_exists()
     {
@@ -132,6 +169,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         storedState.Should().BeNull();
     }
 
+    /// <summary>
+    /// LINE 回覆明確拒絕 link 時，workflow 應轉成 ProviderRejected。
+    ///
+    /// 這類錯誤通常代表 richMenuId 無效、使用者不可綁定或 LINE 端驗證失敗；
+    /// 與網路斷線不同，呼叫端應能從標準狀態看出 provider 已處理但拒絕請求。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_provider_rejected_when_line_rejects_link_request()
     {
@@ -154,6 +197,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("invalid rich menu link");
     }
 
+    /// <summary>
+    /// LINE link 發生一般 HTTP/network 失敗時，workflow 應轉成 ProviderUnavailable。
+    ///
+    /// 這保護產品端不必直接理解 HttpRequestException，
+    /// 只要依照共用 RichMenu 狀態碼決定重試、補償或告警。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_provider_unavailable_when_line_link_network_fails()
     {
@@ -176,6 +225,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("network unavailable");
     }
 
+    /// <summary>
+    /// 線上 RichMenu 查詢失敗時，不應繼續嘗試 link 使用者。
+    ///
+    /// 快取未命中代表 workflow 尚未知道 provider richMenuId；若 list 呼叫失敗，
+    /// 後續 link 沒有可靠 id 可用，因此必須停在 ProviderUnavailable。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_provider_unavailable_when_online_rich_menu_lookup_network_fails()
     {
@@ -206,6 +261,11 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         processor.Calls.Should().NotContain(call => call.StartsWith("link:", StringComparison.Ordinal));
     }
 
+    /// <summary>
+    /// TaskCanceledException 型態的 provider 逾時應回報為標準 timeout 錯誤碼。
+    ///
+    /// 不同 HTTP client 可能以取消例外表示逾時；產品端不應因此看到不一致的錯誤分類。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_provider_timeout_when_line_link_times_out()
     {
@@ -228,6 +288,11 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("provider timeout");
     }
 
+    /// <summary>
+    /// TimeoutException 型態的 provider 逾時也應回報為標準 timeout 錯誤碼。
+    ///
+    /// 這與 TaskCanceledException 測試互補，確保 workflow 的 provider 邊界能涵蓋常見逾時型態。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_returns_provider_timeout_when_line_link_throws_timeout_exception()
     {
@@ -253,6 +318,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("provider hard timeout");
     }
 
+    /// <summary>
+    /// processor 內部程式錯誤不應被 workflow 吞掉或誤包裝成 provider 失敗。
+    ///
+    /// InvalidOperationException 代表測試假物件模擬的程式缺陷；
+    /// 若被轉成標準 ProviderUnavailable，會掩蓋真正需要修程式的問題。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_does_not_swallow_unexpected_processor_exception()
     {
@@ -273,6 +344,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
             .WithMessage("processor bug");
     }
 
+    /// <summary>
+    /// 本機 state store 寫入失敗不應被誤判為 LINE provider failure。
+    ///
+    /// 測試刻意使用 HttpRequestException，確認 workflow 的 try/catch 邊界只包住 provider 呼叫，
+    /// 避免本機一致性問題被回報成外部平台暫時不可用。
+    /// </summary>
     [Fact]
     public async Task AssignAsync_does_not_report_provider_failure_when_state_store_set_fails()
     {
@@ -295,6 +372,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         processor.Calls.Should().Contain("link:U123:rich-menu-001");
     }
 
+    /// <summary>
+    /// throw-based 指派 API 在 provider link 失敗時，應把標準 assignment result 放進例外。
+    ///
+    /// 產品端若選擇用例外控制流程，仍能讀取 ProviderUnavailable 與錯誤碼，
+    /// 不需要重新解析底層 HttpRequestException。
+    /// </summary>
     [Fact]
     public async Task AssignOrThrowAsync_throws_standard_exception_when_provider_link_fails()
     {
@@ -317,6 +400,11 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
     }
 
+    /// <summary>
+    /// LINE 明確拒絕 unlink 時，解除綁定流程應轉成 ProviderRejected。
+    ///
+    /// 這讓呼叫端能分辨「LINE 已拒絕」與「LINE 無法連線」兩種補償策略。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_returns_provider_rejected_when_line_rejects_unlink_request()
     {
@@ -337,6 +425,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("invalid rich menu unlink");
     }
 
+    /// <summary>
+    /// LINE unlink 逾時時，解除綁定流程應回報 provider timeout。
+    ///
+    /// 使用者實際是否解除成功在逾時時不可確定，因此 workflow 保留 provider unavailable 分類，
+    /// 交由呼叫端決定是否重試或稍後查詢狀態。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_returns_provider_unavailable_when_line_unlink_times_out()
     {
@@ -357,6 +451,11 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         result.ErrorMessage.Should().Be("provider timeout");
     }
 
+    /// <summary>
+    /// unlink processor 的非預期程式例外不應被 workflow 包裝成 provider failure。
+    ///
+    /// 這與 assign 的保護對稱，避免共用流程把內部 bug 偽裝成 LINE 平台問題。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_does_not_swallow_unexpected_processor_exception()
     {
@@ -375,6 +474,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
             .WithMessage("processor bug");
     }
 
+    /// <summary>
+    /// 本機 state store 移除失敗不應被誤判為 LINE unlink 失敗。
+    ///
+    /// LINE unlink 已送出後，若本機狀態清除失敗，呼叫端需要看到真正的儲存層錯誤，
+    /// 才能安排補償或資料修復。
+    /// </summary>
     [Fact]
     public async Task UnassignAsync_does_not_report_provider_failure_when_state_store_remove_fails()
     {
@@ -406,6 +511,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         processor.Calls.Should().Contain("unlink:U123");
     }
 
+    /// <summary>
+    /// throw-based 解除綁定 API 在 provider unlink 失敗時，應保留標準 assignment result。
+    ///
+    /// 這確保非同步背景流程或產品控制器即使用 OrThrow 版本，
+    /// 仍能用同一組 RichMenu 狀態碼做診斷。
+    /// </summary>
     [Fact]
     public async Task UnassignOrThrowAsync_throws_standard_exception_when_provider_unlink_fails()
     {
@@ -426,6 +537,12 @@ public sealed class LineRichMenuAssignmentWorkflowTests
         exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable");
     }
 
+    /// <summary>
+    /// 可注入寫入或移除例外的 state store 假物件。
+    ///
+    /// 它用來精準測試 workflow 的錯誤邊界：provider 例外應被標準化，
+    /// 但本機狀態儲存失敗必須原樣往外拋，避免兩種不同責任域混在一起。
+    /// </summary>
     private sealed class ThrowingRichMenuStateStore : IRichMenuStateStore
     {
         private readonly Exception? _setException;
diff --git a/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs b/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
index 580900aa..6b94d821 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
@@ -3,8 +3,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Boundary;
 
+/// <summary>
+/// 驗證共用 RichMenu 專案不依賴任何產品層、資料庫層或 ASP.NET MVC 層型別。
+/// 這是架構邊界測試，確保 RichMenu 共用能力可以被未來多個產品重用。
+/// </summary>
 public sealed class RichMenuProjectBoundaryTests
 {
+    /// <summary>
+    /// 掃描 RichMenu 共用專案原始碼，若出現產品名稱或上層框架關鍵字就視為邊界破壞。
+    /// </summary>
     [Fact]
     public void RichMenu_project_does_not_reference_product_specific_dependencies()
     {
@@ -32,6 +39,9 @@ public sealed class RichMenuProjectBoundaryTests
         hits.Should().BeEmpty();
     }
 
+    /// <summary>
+    /// 從測試輸出目錄往上找 solution root，讓測試可在 IDE、CLI 與 CI 中穩定定位專案檔。
+    /// </summary>
     private static string FindProjectRoot()
     {
         var directory = new DirectoryInfo(AppContext.BaseDirectory);
diff --git a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
index 287ce8b0..f21dbb80 100644
--- a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
@@ -8,8 +8,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests;
 
+/// <summary>
+/// 驗證低階 RichMenu workflow 與 LINE Messaging API endpoint 的串接順序。
+/// 這組測試使用自訂 HttpMessageHandler 捕捉 HTTP request，避免打真實 LINE API。
+/// </summary>
 public sealed class LineRichMenuWorkflowTests
 {
+    /// <summary>
+    /// 建立、上傳圖片、連結使用者應依 LINE API 要求順序送出三個 HTTP request。
+    /// </summary>
     [Fact]
     public async Task CreateUploadAndLinkAsync_creates_uploads_and_links_rich_menu_in_order()
     {
@@ -41,6 +48,9 @@ public sealed class LineRichMenuWorkflowTests
         handler.Bodies[0].Should().Contain("\"name\":\"test richmenu\"");
     }
 
+    /// <summary>
+    /// 刪除已連結 RichMenu 時，workflow 應先查使用者目前 richMenuId，再 unlink，最後 delete provider menu。
+    /// </summary>
     [Fact]
     public async Task DeleteLinkedRichMenuAsync_gets_current_menu_then_unlinks_and_deletes()
     {
@@ -63,6 +73,9 @@ public sealed class LineRichMenuWorkflowTests
             "DELETE https://api.line.me/v2/bot/richmenu/rich-menu-001");
     }
 
+    /// <summary>
+    /// 本機驗證失敗時不應送出任何 HTTP request，避免把明顯錯誤送到 LINE provider。
+    /// </summary>
     [Fact]
     public async Task CreateUploadAndLinkAsync_returns_validation_failure_without_http_call_when_user_is_blank()
     {
@@ -82,6 +95,9 @@ public sealed class LineRichMenuWorkflowTests
         handler.Requests.Should().BeEmpty();
     }
 
+    /// <summary>
+    /// LINE provider 明確拒絕 request 時，OrThrow 變體應丟出標準 RichMenu 例外並保留 provider-rejected 狀態。
+    /// </summary>
     [Fact]
     public async Task CreateUploadAndLinkOrThrowAsync_throws_standard_exception_when_provider_rejects_request()
     {
@@ -102,12 +118,18 @@ public sealed class LineRichMenuWorkflowTests
         exception.Which.Result.ErrorMessage.Should().Be("invalid rich menu");
     }
 
+    /// <summary>
+    /// 建立 workflow 測試用的 SDK client，讓測試可以控制 HTTP response sequence。
+    /// </summary>
     private static LineRichMenuWorkflow CreateWorkflow(SequencedHttpMessageHandler handler)
     {
         var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2");
         return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(sdkClient)));
     }
 
+    /// <summary>
+    /// 建立符合 LINE RichMenu 基本要求的測試版面。
+    /// </summary>
     private static RichMenu CreateRichMenu()
     {
         return new RichMenu
@@ -127,19 +149,37 @@ public sealed class LineRichMenuWorkflowTests
         };
     }
 
+    /// <summary>
+    /// 依序回傳預先排好的 HTTP response，並捕捉 workflow 送出的 request 與 body。
+    /// </summary>
     private sealed class SequencedHttpMessageHandler : HttpMessageHandler
     {
+        /// <summary>
+        /// 測試預先排好的 provider responses。
+        /// </summary>
         private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new();
 
+        /// <summary>
+        /// workflow 送出的 HTTP requests，供測試檢查 endpoint 與 method。
+        /// </summary>
         public List<HttpRequestMessage> Requests { get; } = new();
 
+        /// <summary>
+        /// workflow 送出的 request bodies，供測試檢查 RichMenu create payload。
+        /// </summary>
         public List<string> Bodies { get; } = new();
 
+        /// <summary>
+        /// 加入下一個 provider response。
+        /// </summary>
         public void Enqueue(HttpStatusCode statusCode, string body)
         {
             _responses.Enqueue((statusCode, body));
         }
 
+        /// <summary>
+        /// 捕捉 request，取出下一個 response，模擬 LINE API 回應。
+        /// </summary>
         protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
         {
             Requests.Add(request);
diff --git a/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs
index 9649ff5a..c8091a8e 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs
@@ -3,8 +3,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;
 
+/// <summary>
+/// 驗證到期掃描 workflow 如何還原暫時性 RichMenu 狀態。
+/// 測試涵蓋「回復上一個選單」、「沒有上一個選單時解除綁定」與「未到期不處理」三種核心路徑。
+/// </summary>
 public sealed class RichMenuExpirationSweepWorkflowTests
 {
+    /// <summary>
+    /// 使用者狀態已到期且有 PreviousMenuKey 時，sweep 應呼叫 assignment workflow 指派回上一個選單。
+    /// </summary>
     [Fact]
     public async Task SweepAsync_restores_previous_menu_for_expired_state()
     {
@@ -26,6 +33,9 @@ public sealed class RichMenuExpirationSweepWorkflowTests
         assignment.Calls.Should().Equal("assign:U-expired:member-main");
     }
 
+    /// <summary>
+    /// 使用者狀態已到期但沒有 PreviousMenuKey 時，sweep 應解除使用者個人 RichMenu 綁定。
+    /// </summary>
     [Fact]
     public async Task SweepAsync_unassigns_expired_state_without_previous_menu()
     {
@@ -47,6 +57,9 @@ public sealed class RichMenuExpirationSweepWorkflowTests
         assignment.Calls.Should().Equal("unassign:U-expired");
     }
 
+    /// <summary>
+    /// 使用者狀態尚未到期時，sweep 不應呼叫 assignment workflow，也不應把它計入 scanned/restored。
+    /// </summary>
     [Fact]
     public async Task SweepAsync_ignores_states_that_have_not_expired()
     {
@@ -68,10 +81,20 @@ public sealed class RichMenuExpirationSweepWorkflowTests
         assignment.Calls.Should().BeEmpty();
     }
 
+    /// <summary>
+    /// 捕捉 sweep workflow 對 assignment workflow 的呼叫順序與參數。
+    /// 這個 fake 不模擬 LINE provider，只用來驗證到期判斷後的 orchestration 決策。
+    /// </summary>
     private sealed class CapturingAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
     {
+        /// <summary>
+        /// 依序記錄 assign / unassign 呼叫，讓測試能直接 assert sweep 的輸出行為。
+        /// </summary>
         public List<string> Calls { get; } = new();
 
+        /// <summary>
+        /// 記錄還原到上一個 menuKey 的呼叫。
+        /// </summary>
         public Task<LineRichMenuAssignmentResult> AssignAsync(
             string lineUserId,
             string menuKey,
@@ -81,6 +104,9 @@ public sealed class RichMenuExpirationSweepWorkflowTests
             return Task.FromResult(LineRichMenuAssignmentResult.Linked(null, menuKey, "rich-menu-restored", changed: true));
         }
 
+        /// <summary>
+        /// 記錄 OrThrow assign 呼叫；目前 sweep 不使用此方法，保留以完整實作介面。
+        /// </summary>
         public Task AssignOrThrowAsync(
             string lineUserId,
             string menuKey,
@@ -90,6 +116,9 @@ public sealed class RichMenuExpirationSweepWorkflowTests
             return Task.CompletedTask;
         }
 
+        /// <summary>
+        /// 記錄解除個人 RichMenu 綁定的呼叫。
+        /// </summary>
         public Task<LineRichMenuAssignmentResult> UnassignAsync(
             string lineUserId,
             CancellationToken cancellationToken = default)
@@ -98,6 +127,9 @@ public sealed class RichMenuExpirationSweepWorkflowTests
             return Task.FromResult(LineRichMenuAssignmentResult.Unlinked(null, changed: true));
         }
 
+        /// <summary>
+        /// 記錄 OrThrow unassign 呼叫；目前 sweep 不使用此方法，保留以完整實作介面。
+        /// </summary>
         public Task UnassignOrThrowAsync(
             string lineUserId,
             CancellationToken cancellationToken = default)
diff --git a/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs b/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
index 2d4d4df7..229fe24f 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
@@ -4,8 +4,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Orchestration;
 
+/// <summary>
+/// 驗證 RichMenu orchestrator 如何把 policy 決策轉交給 assignment workflow。
+/// 這些測試避免 orchestrator 被加入產品邏輯；它只應挑選決策並套用結果。
+/// </summary>
 public sealed class RichMenuOrchestratorTests
 {
+    /// <summary>
+    /// 收到文字命中 trigger policy 時，orchestrator 應指派對應 menu key 並透過 cache link 到 provider richMenuId。
+    /// </summary>
     [Fact]
     public async Task ApplyAsync_assigns_menu_when_text_matches_trigger_policy()
     {
@@ -34,6 +41,9 @@ public sealed class RichMenuOrchestratorTests
         processor.LinkedUsers["U123"].Should().Be("rich-menu-001");
     }
 
+    /// <summary>
+    /// 沒有任何 policy 命中時，orchestrator 應回傳 no-change，不應誤呼叫 LINE link/unlink。
+    /// </summary>
     [Fact]
     public async Task ApplyAsync_returns_no_change_when_text_has_no_mapping()
     {
diff --git a/LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
index 8f17305d..71735a3c 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
@@ -4,8 +4,15 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Provisioning;
 
+/// <summary>
+/// 驗證 RichMenu catalog 佈建 workflow 與 LINE provider 狀態同步的核心行為。
+/// 測試重點是 create/upload/alias/default/cache 的順序，以及失敗選單不會中斷後續選單同步。
+/// </summary>
 public sealed class LineRichMenuProvisioningWorkflowTests
 {
+    /// <summary>
+    /// 新選單不存在於 LINE 時，workflow 應建立 RichMenu、上傳圖片、建立 alias、設定 default 並寫入 cache。
+    /// </summary>
     [Fact]
     public async Task SyncAsync_creates_uploads_aliases_defaults_and_caches_new_menu()
     {
@@ -36,6 +43,9 @@ public sealed class LineRichMenuProvisioningWorkflowTests
         processor.UploadedImageCount.Should().Be(1);
     }
 
+    /// <summary>
+    /// 已存在相同 fingerprinted name 時，workflow 應重用 provider richMenuId，仍補齊 alias 與 cache。
+    /// </summary>
     [Fact]
     public async Task SyncAsync_reuses_existing_fingerprinted_menu_and_updates_alias_when_needed()
     {
@@ -70,6 +80,9 @@ public sealed class LineRichMenuProvisioningWorkflowTests
         processor.UploadedImageCount.Should().Be(0);
     }
 
+    /// <summary>
+    /// 單一 definition 失敗時應產生 Failed item 並繼續處理下一個 definition，讓管理端看到完整同步結果。
+    /// </summary>
     [Fact]
     public async Task SyncAsync_records_failed_item_and_continues_with_next_definition()
     {
diff --git a/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs b/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
index ce8f6b3b..c7af9d1b 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
@@ -20,6 +20,7 @@ namespace LineMessagingProcessor.RichMenus.Tests.Support;
 /// </summary>
 internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor
 {
+    // 此假物件刻意同時保存 provider 狀態與呼叫順序，讓 RichMenu tests 能驗證 workflow 是否真的走到 LINE 邊界。
     /// <summary>
     /// 建立 RichMenu 時要回傳的 richMenuId 佇列。
     ///
diff --git a/LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs b/LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
index d06e019d..4d798e11 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
@@ -2,8 +2,16 @@ using Line.Messaging;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Support;
 
+/// <summary>
+/// 建立 RichMenu 測試資料的集中 factory。
+/// 測試透過這個 helper 取得一致的版面、action area 與 PNG bytes，避免每個測試各自手寫不一致的 RichMenu payload。
+/// </summary>
 internal static class RichMenuTestFactory
 {
+    /// <summary>
+    /// 建立一個可供 provisioning、assignment 與 workflow 測試共用的基本 RichMenu。
+    /// </summary>
+    /// <param name="name">RichMenu 名稱；測試會用它模擬一般名稱或 fingerprinted provider 名稱。</param>
     public static RichMenu CreateMenu(string name = "member-main")
     {
         return new RichMenu
@@ -23,11 +31,19 @@ internal static class RichMenuTestFactory
         };
     }
 
+    /// <summary>
+    /// 建立 PNG stream factory，模擬 catalog definition 在 provisioning 時可重新開啟圖片來源。
+    /// </summary>
+    /// <param name="seed">用來產生穩定但可區分的測試 bytes。</param>
     public static Func<CancellationToken, Task<Stream>> CreatePngFactory(byte seed = 1)
     {
         return _ => Task.FromResult<Stream>(new MemoryStream(CreatePngBytes(seed)));
     }
 
+    /// <summary>
+    /// 建立穩定 PNG bytes，讓 fingerprint 測試可預期且不依賴真實圖片檔案。
+    /// </summary>
+    /// <param name="seed">第一個 byte，方便測試不同圖片內容會產生不同 fingerprint。</param>
     public static byte[] CreatePngBytes(byte seed = 1)
         => new[] { seed, (byte)(seed + 1), (byte)(seed + 2) };
 }
diff --git a/LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs b/LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
index b71c744f..044b9008 100644
--- a/LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
+++ b/LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
@@ -3,8 +3,16 @@ using Xunit;
 
 namespace LineMessagingProcessor.RichMenus.Tests.Triggers;
 
+/// <summary>
+/// 驗證文字觸發 resolver 的輸入正規化與 exact-match 行為。
+/// 這些測試鎖住「使用者輸入前後空白不影響判斷」與「產品設定文字可直接對應 menu key」的契約。
+/// </summary>
 public sealed class LineRichMenuTextTriggerResolverTests
 {
+    /// <summary>
+    /// 確認 resolver 會先 trim 使用者收到的文字，再用 options 內的對照表解析 menu key。
+    /// 這能避免 LINE webhook payload 因使用者輸入空白而無法切換 RichMenu。
+    /// </summary>
     [Fact]
     public void TryResolve_uses_trimmed_ordinal_trigger_mapping()
     {
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
index f657ca1e..a02d8d52 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
@@ -6,11 +6,33 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public interface ILineRichMenuAssignmentWorkflow
 {
+    /// <summary>
+    /// 將應用程式 menu key 指定的選單指派給 LINE 使用者。
+    /// </summary>
+    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
+    /// <param name="menuKey">應用程式層級的 menu key，會透過 RichMenu id cache 或 catalog 解析。</param>
+    /// <param name="cancellationToken">供 cache、catalog、state store 與 provider 操作使用的取消權杖。</param>
     Task<LineRichMenuAssignmentResult> AssignAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 指派選單；若標準化結果不成功，則丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="lineUserId">要接收選單的 LINE userId。</param>
+    /// <param name="menuKey">要指派的應用程式層級 menu key。</param>
+    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
     Task AssignOrThrowAsync(string lineUserId, string menuKey, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 移除 LINE 使用者的顯式 RichMenu 連結，並清除本機保存的應用程式指派狀態。
+    /// </summary>
+    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
+    /// <param name="cancellationToken">供 provider 與 state store 操作使用的取消權杖。</param>
     Task<LineRichMenuAssignmentResult> UnassignAsync(string lineUserId, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 移除使用者 RichMenu 連結；若結果不成功，則丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="lineUserId">要移除顯式 RichMenu 連結的 LINE userId。</param>
+    /// <param name="cancellationToken">供下游操作使用的取消權杖。</param>
     Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
index 5f0caf2c..0b6374ec 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
@@ -6,5 +6,12 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public interface ILineRichMenuCatalog
 {
+    /// <summary>
+    /// 載入所有應同步到 LINE 的 RichMenu 定義。
+    /// </summary>
+    /// <param name="cancellationToken">供需要 I/O 的 catalog 實作用的取消權杖。</param>
+    /// <returns>
+    /// 穩定的應用程式 RichMenu 定義清單，包含 menu key、alias、版面與圖片 stream factory。
+    /// </returns>
     Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
index 4400be9c..99f95075 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
@@ -6,13 +6,34 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public interface ILineRichMenuIdCache
 {
+    /// <summary>
+    /// 嘗試取得應用程式 menu key 已解析出的 LINE richMenuId。
+    /// </summary>
+    /// <param name="menuKey">應用程式層級的 menu key。</param>
+    /// <param name="richMenuId">方法回傳 true 時，代表已快取的 LINE richMenuId。</param>
     bool TryGet(string menuKey, out string richMenuId);
 
+    /// <summary>
+    /// 儲存或取代某個應用程式 menu key 對應的 LINE richMenuId。
+    /// </summary>
+    /// <param name="menuKey">應用程式層級的 menu key。</param>
+    /// <param name="richMenuId">provisioning 過程中建立或發現的 LINE provider id。</param>
     void Set(string menuKey, string richMenuId);
 
+    /// <summary>
+    /// 移除已快取的應用程式 menu key 對照。
+    /// </summary>
+    /// <param name="menuKey">要移除的應用程式層級 menu key。</param>
     void Remove(string menuKey);
 
+    /// <summary>
+    /// 回傳目前所有應用程式 menu key 到 LINE richMenuId 對照的時間點快照。
+    /// </summary>
     IReadOnlyDictionary<string, string> Snapshot();
 
+    /// <summary>
+    /// 以新的對照集合取代整份 cache。
+    /// </summary>
+    /// <param name="values">要保留的 menu key 到 richMenuId 對照。</param>
     void SetSnapshot(IReadOnlyDictionary<string, string> values);
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
index dcf17246..12fc4417 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
@@ -8,19 +8,78 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public interface ILineRichMenuProcessor
 {
+    /// <summary>
+    /// 建立 LINE RichMenu metadata record，並回傳 provider id。
+    /// </summary>
     Task<string> CreateRichMenuAsync(RichMenu richMenu);
+
+    /// <summary>
+    /// 上傳 LINE 顯示 RichMenu 前必須具備的 PNG 圖片。
+    /// </summary>
     Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream);
+
+    /// <summary>
+    /// 列出目前 LINE channel 內已存在的 RichMenus。
+    /// </summary>
     Task<IList<ResponseRichMenu>> GetRichMenuListAsync();
+
+    /// <summary>
+    /// 設定 channel 預設 RichMenu。
+    /// </summary>
     Task SetDefaultRichMenuAsync(string richMenuId);
+
+    /// <summary>
+    /// 取得目前 channel 預設 richMenuId。
+    /// </summary>
     Task<string> GetDefaultRichMenuIdAsync();
+
+    /// <summary>
+    /// 清除 channel 預設 RichMenu。
+    /// </summary>
     Task CancelDefaultRichMenuAsync();
+
+    /// <summary>
+    /// 取得目前直接連結到指定 LINE 使用者的 richMenuId。
+    /// </summary>
     Task<string> GetRichMenuIdOfUserAsync(string userId);
+
+    /// <summary>
+    /// 將 LINE 使用者連結到指定 provider richMenuId。
+    /// </summary>
     Task LinkRichMenuToUserAsync(string userId, string richMenuId);
+
+    /// <summary>
+    /// 移除 LINE 使用者的顯式 RichMenu 連結。
+    /// </summary>
     Task UnlinkRichMenuFromUserAsync(string userId);
+
+    /// <summary>
+    /// 依 id 刪除 provider RichMenu。
+    /// </summary>
     Task DeleteRichMenuAsync(string richMenuId);
+
+    /// <summary>
+    /// 建立指向 provider richMenuId 的 LINE RichMenu alias。
+    /// </summary>
     Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId);
+
+    /// <summary>
+    /// 更新既有 RichMenu alias，讓它指向不同的 provider richMenuId。
+    /// </summary>
     Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId);
+
+    /// <summary>
+    /// 刪除 LINE RichMenu alias。
+    /// </summary>
     Task DeleteRichMenuAliasAsync(string richMenuAliasId);
+
+    /// <summary>
+    /// 依 alias id 取得單一 LINE RichMenu alias。
+    /// </summary>
     Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId);
+
+    /// <summary>
+    /// 取得 channel 內所有 LINE RichMenu aliases。
+    /// </summary>
     Task<RichMenuAliasList> GetRichMenuAliasListAsync();
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
index 220955f1..2fd51a63 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
@@ -1,6 +1,13 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 將應用程式 RichMenu catalog 與 LINE provider 狀態同步。
+/// </summary>
 public interface ILineRichMenuProvisioningWorkflow
 {
+    /// <summary>
+    /// 依目前 catalog 建立、重用、設定 alias、設定預設值，並快取 RichMenu 對照。
+    /// </summary>
+    /// <param name="cancellationToken">供 LINE API 與 catalog 操作使用的取消權杖。</param>
     Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
index f498083d..5560a61f 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
@@ -1,8 +1,20 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 將 LINE 傳入文字解析成應用程式 RichMenu key。
+/// </summary>
 public interface ILineRichMenuTextTriggerResolver
 {
+    /// <summary>
+    /// 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
+    /// </summary>
+    /// <param name="receivedText">LINE 收到的原始文字。</param>
     string? ResolveMenuKey(string? receivedText);
 
+    /// <summary>
+    /// 嘗試將 received text 解析成 menu key。
+    /// </summary>
+    /// <param name="receivedText">LINE 收到的原始文字。</param>
+    /// <param name="menuKey">方法回傳 true 時為解析出的 menu key；否則為空字串。</param>
     bool TryResolve(string? receivedText, out string menuKey);
 }
diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
index e0c9326b..5c408317 100644
--- a/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
@@ -8,12 +8,28 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public interface ILineRichMenuWorkflow
 {
+    /// <summary>
+    /// 建立 LINE RichMenu、上傳圖片，並直接連結到一位使用者。
+    /// </summary>
+    /// <param name="request">此操作需要的 user id、選單版面、圖片 stream factory 與 metadata。</param>
     Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request);
 
+    /// <summary>
+    /// 執行 <see cref="CreateUploadAndLinkAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="request">建立、上傳與連結的 request。</param>
     Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request);
 
+    /// <summary>
+    /// 解除使用者目前 RichMenu 連結，並刪除該連結指向的 provider RichMenu。
+    /// </summary>
+    /// <param name="request">刪除與解除連結操作需要的 user id 與 metadata。</param>
     Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request);
 
+    /// <summary>
+    /// 執行 <see cref="DeleteLinkedRichMenuAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="request">刪除與解除連結 request。</param>
     Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);
 }
 
diff --git a/LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs b/LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
index c84ef048..c34f303b 100644
--- a/LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
@@ -1,6 +1,14 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 還原或解除已到期的暫時性 RichMenu 指派。
+/// </summary>
 public interface IRichMenuExpirationSweepWorkflow
 {
+    /// <summary>
+    /// 處理已到期的 RichMenu 使用者狀態紀錄。
+    /// </summary>
+    /// <param name="now">用來判斷哪些指派已到期的目前時間。</param>
+    /// <param name="cancellationToken">傳入 state store 與 assignment workflow 的取消權杖。</param>
     Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs b/LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
index 0390d4f8..cd35ef59 100644
--- a/LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
+++ b/LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
@@ -1,6 +1,14 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 協調單次使用者互動中的 policy 評估與 RichMenu 指派。
+/// </summary>
 public interface IRichMenuOrchestrator
 {
+    /// <summary>
+    /// 依傳入 context 套用最佳 RichMenu decision。
+    /// </summary>
+    /// <param name="context">policy 評估所需的 LINE 使用者 context 與訊息事實。</param>
+    /// <param name="cancellationToken">傳入 policies 與 assignment workflows 的取消權杖。</param>
     Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs b/LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
index b1935cbd..f9da4c30 100644
--- a/LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
+++ b/LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
@@ -1,6 +1,15 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 定義一條可判斷使用者 RichMenu 是否應改變的規則。
+/// policy 刻意保持小而可組合；orchestrator 會評估所有 policy，並只套用強度最高的 decision。
+/// </summary>
 public interface IRichMenuPolicy
 {
+    /// <summary>
+    /// 評估傳入 context，並回傳 RichMenu decision。
+    /// </summary>
+    /// <param name="context">policy 可使用的使用者與訊息 context。</param>
+    /// <param name="cancellationToken">供需要非同步資料的 policy 使用的取消權杖。</param>
     Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs b/LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
index 6a31e523..a29c636d 100644
--- a/LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
+++ b/LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
@@ -1,12 +1,36 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 保存 LINE 使用者的應用程式層級 RichMenu 狀態。
+/// 實作可以使用記憶體、資料庫或分散式快取，但必須保留足夠狀態，讓 assignment workflow 與到期 sweep 能可預期地還原前一個選單。
+/// </summary>
 public interface IRichMenuStateStore
 {
+    /// <summary>
+    /// 取得單一 LINE 使用者已保存的狀態。
+    /// </summary>
+    /// <param name="lineUserId">要查詢的 LINE userId。</param>
+    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
     Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 儲存或取代單一 LINE 使用者的 RichMenu 狀態。
+    /// </summary>
+    /// <param name="state">要保存的完整狀態紀錄。</param>
+    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
     Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 移除單一 LINE 使用者已保存的 RichMenu 狀態。
+    /// </summary>
+    /// <param name="lineUserId">要移除狀態的 LINE userId。</param>
+    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
     Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default);
 
+    /// <summary>
+    /// 回傳所有已達到期時間的狀態紀錄。
+    /// </summary>
+    /// <param name="now">用於到期比較的目前時間。</param>
+    /// <param name="cancellationToken">供 backing store 操作使用的取消權杖。</param>
     Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
 }
diff --git a/LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs b/LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
index a5ffa105..c4d75239 100644
--- a/LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
+++ b/LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
@@ -6,9 +6,18 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
 {
+    /// <summary>
+    /// 同步 snapshot 取代流程，避免讀取端看到只更新到一半的 dictionary。
+    /// </summary>
     private readonly object _gate = new();
+
+    /// <summary>
+    /// 目前 cache snapshot，以應用程式 menu key 作為索引。
+    /// 透過整份 dictionary 取代而不是原地修改，讓 snapshot 讀取保持簡單且可預期。
+    /// </summary>
     private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
 
+    /// <inheritdoc />
     public bool TryGet(string menuKey, out string richMenuId)
     {
         richMenuId = string.Empty;
@@ -30,6 +39,7 @@ public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
         }
     }
 
+    /// <inheritdoc />
     public void Set(string menuKey, string richMenuId)
     {
         var normalizedKey = Normalize(menuKey);
@@ -54,6 +64,7 @@ public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
         }
     }
 
+    /// <inheritdoc />
     public void Remove(string menuKey)
     {
         var normalizedKey = Normalize(menuKey);
@@ -75,6 +86,7 @@ public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
         }
     }
 
+    /// <inheritdoc />
     public IReadOnlyDictionary<string, string> Snapshot()
     {
         lock (_gate)
@@ -83,6 +95,7 @@ public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
         }
     }
 
+    /// <inheritdoc />
     public void SetSnapshot(IReadOnlyDictionary<string, string> values)
     {
         var replacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
@@ -105,5 +118,8 @@ public sealed class InMemoryLineRichMenuIdCache : ILineRichMenuIdCache
         }
     }
 
+    /// <summary>
+    /// 修剪 cache key 與 value，確保 cache 保存的是 workflow 實際使用的邏輯識別碼。
+    /// </summary>
     private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
 }
diff --git a/LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs b/LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
index 9558d66c..1646293b 100644
--- a/LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
+++ b/LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
@@ -9,14 +9,20 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
 {
+    /// <summary>
+    /// 以 LINE userId 為 key 的 thread-safe state table。
+    /// 使用不分大小寫 comparer，讓呼叫端 userId 大小寫不同時仍能穩定查詢狀態。
+    /// </summary>
     private readonly ConcurrentDictionary<string, RichMenuUserState> _states = new(StringComparer.OrdinalIgnoreCase);
 
+    /// <inheritdoc />
     public Task<RichMenuUserState?> GetAsync(string lineUserId, CancellationToken cancellationToken = default)
     {
         _states.TryGetValue(Normalize(lineUserId), out var state);
         return Task.FromResult(state);
     }
 
+    /// <inheritdoc />
     public Task SetAsync(RichMenuUserState state, CancellationToken cancellationToken = default)
     {
         if (state == null)
@@ -28,6 +34,7 @@ public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
         return Task.CompletedTask;
     }
 
+    /// <inheritdoc />
     public Task RemoveAsync(string lineUserId, CancellationToken cancellationToken = default)
     {
         var key = Normalize(lineUserId);
@@ -39,6 +46,7 @@ public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
         return Task.CompletedTask;
     }
 
+    /// <inheritdoc />
     public Task<IReadOnlyList<RichMenuUserState>> GetExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
     {
         IReadOnlyList<RichMenuUserState> expired = _states.Values
@@ -47,8 +55,14 @@ public sealed class InMemoryRichMenuStateStore : IRichMenuStateStore
         return Task.FromResult(expired);
     }
 
+    /// <summary>
+    /// 正規化選填 key，讓查詢與移除路徑可容忍空值並安全 no-op。
+    /// </summary>
     private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
 
+    /// <summary>
+    /// 正規化寫入路徑的 key；空值會破壞 store，因此必須拒絕。
+    /// </summary>
     private static string NormalizeRequired(string value, string parameterName)
     {
         var normalized = Normalize(value);
diff --git a/LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs b/LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
index e4e779a6..65cffb30 100644
--- a/LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
+++ b/LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
@@ -8,26 +8,60 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineMessagingProcessorRichMenuAdapter : ILineRichMenuProcessor
 {
+    /// <summary>
+    /// 既有 processor，已負責 token 設定與 LINE SDK 存取。
+    /// </summary>
     private readonly LineMessagingProcessorClass _processor;
 
+    /// <summary>
+    /// 將 legacy processor 包裝在 RichMenu 專用抽象後方。
+    /// </summary>
+    /// <param name="processor">應用程式既有的 LINE messaging processor。</param>
     public LineMessagingProcessorRichMenuAdapter(LineMessagingProcessorClass processor)
     {
         _processor = processor ?? throw new ArgumentNullException(nameof(processor));
     }
 
+    /// <inheritdoc />
     public Task<string> CreateRichMenuAsync(RichMenu richMenu) => _processor.CreateRichMenuAsync(richMenu);
+
+    /// <inheritdoc />
     public Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream) => _processor.UploadRichMenuPngImageAsync(richMenuId, imageStream);
+
+    /// <inheritdoc />
     public Task<IList<ResponseRichMenu>> GetRichMenuListAsync() => _processor.GetRichMenuListAsync();
+
+    /// <inheritdoc />
     public Task SetDefaultRichMenuAsync(string richMenuId) => _processor.SetDefaultRichMenuAsync(richMenuId);
+
+    /// <inheritdoc />
     public Task<string> GetDefaultRichMenuIdAsync() => _processor.GetDefaultRichMenuIdAsync();
+
+    /// <inheritdoc />
     public Task CancelDefaultRichMenuAsync() => _processor.CancelDefaultRichMenuAsync();
+
+    /// <inheritdoc />
     public Task<string> GetRichMenuIdOfUserAsync(string userId) => _processor.GetRichMenuIdOfUserAsync(userId);
+
+    /// <inheritdoc />
     public Task LinkRichMenuToUserAsync(string userId, string richMenuId) => _processor.LinkRichMenuToUserAsync(userId, richMenuId);
+
+    /// <inheritdoc />
     public Task UnlinkRichMenuFromUserAsync(string userId) => _processor.UnlinkRichMenuFromUserAsync(userId);
+
+    /// <inheritdoc />
     public Task DeleteRichMenuAsync(string richMenuId) => _processor.DeleteRichMenuAsync(richMenuId);
+
+    /// <inheritdoc />
     public Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId) => _processor.CreateRichMenuAliasAsync(richMenuId, richMenuAliasId);
+
+    /// <inheritdoc />
     public Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId) => _processor.UpdateRichMenuAliasAsync(richMenuAliasId, richMenuId);
+
+    /// <inheritdoc />
     public Task DeleteRichMenuAliasAsync(string richMenuAliasId) => _processor.DeleteRichMenuAliasAsync(richMenuAliasId);
+
+    /// <inheritdoc />
     public async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
     {
         try
@@ -39,5 +73,7 @@ public sealed class LineMessagingProcessorRichMenuAdapter : ILineRichMenuProcess
             throw new LineRichMenuAliasNotFoundException(richMenuAliasId);
         }
     }
+
+    /// <inheritdoc />
     public Task<RichMenuAliasList> GetRichMenuAliasListAsync() => _processor.GetRichMenuAliasListAsync();
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs b/LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
index b3588d15..be7e5b75 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
@@ -1,12 +1,23 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 表示 LINE 沒有回傳指定 alias id 的 RichMenu alias。
+/// 專用例外讓 provisioning 程式可以分辨「可建立的 missing alias」與「應回報為同步錯誤的其他 provider failure」。
+/// </summary>
 public sealed class LineRichMenuAliasNotFoundException : Exception
 {
+    /// <summary>
+    /// 建立指定 LINE RichMenu alias id 不存在的例外。
+    /// </summary>
+    /// <param name="richMenuAliasId">向 LINE 查詢的 alias id。</param>
     public LineRichMenuAliasNotFoundException(string richMenuAliasId)
         : base($"RichMenu alias '{richMenuAliasId}' was not found.")
     {
         RichMenuAliasId = richMenuAliasId;
     }
 
+    /// <summary>
+    /// 取得不存在的 LINE RichMenu alias id。
+    /// </summary>
     public string RichMenuAliasId { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
index d53979cd..55be2b2d 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
@@ -1,7 +1,15 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 回報使用者 RichMenu 被指派、保留或移除後的結果。
+/// 此結果同時攜帶業務層狀態（例如已指派的 menu key）與 provider 層狀態（例如 richMenuId），
+/// 讓呼叫端不必查看 workflow 內部細節也能做後續判斷。
+/// </summary>
 public sealed class LineRichMenuAssignmentResult
 {
+    /// <summary>
+    /// 建立成功的 RichMenu 指派結果。
+    /// </summary>
     private LineRichMenuAssignmentResult(bool changed, string? previousMenuKey, string? assignedMenuKey, string? richMenuId)
     {
         Succeeded = true;
@@ -12,6 +20,9 @@ public sealed class LineRichMenuAssignmentResult
         RichMenuId = richMenuId;
     }
 
+    /// <summary>
+    /// 建立失敗的 RichMenu 指派結果，並保留標準化錯誤資訊。
+    /// </summary>
     private LineRichMenuAssignmentResult(LineRichMenuStatus status, string errorCode, string errorMessage)
     {
         Succeeded = false;
@@ -20,31 +31,78 @@ public sealed class LineRichMenuAssignmentResult
         ErrorMessage = errorMessage;
     }
 
+    /// <summary>
+    /// 取得 assignment workflow 是否成功完成。
+    /// </summary>
     public bool Succeeded { get; }
 
+    /// <summary>
+    /// 取得標準化 workflow 狀態。
+    /// </summary>
     public LineRichMenuStatus Status { get; }
 
+    /// <summary>
+    /// 當 <see cref="Succeeded"/> 為 false 時，取得穩定的應用程式錯誤代碼。
+    /// </summary>
     public string? ErrorCode { get; }
 
+    /// <summary>
+    /// workflow 失敗時，取得可讀的錯誤訊息。
+    /// </summary>
     public string? ErrorMessage { get; }
 
+    /// <summary>
+    /// 取得實際 LINE 指派是否有變更。
+    /// 沒有變更仍代表 workflow 成功完成，而不是失敗。
+    /// </summary>
     public bool Changed { get; }
 
+    /// <summary>
+    /// 取得操作前已知的作用中應用程式 menu key。
+    /// </summary>
     public string? PreviousMenuKey { get; }
 
+    /// <summary>
+    /// 取得操作後指派的應用程式 menu key；若操作為 unlink 則為 null。
+    /// </summary>
     public string? AssignedMenuKey { get; }
 
+    /// <summary>
+    /// 操作指派選單時，取得連結到使用者的 LINE richMenuId。
+    /// </summary>
     public string? RichMenuId { get; }
 
+    /// <summary>
+    /// 建立成功的 link 結果。
+    /// </summary>
+    /// <param name="previousMenuKey">先前的應用程式 menu key；若無資料則為 null。</param>
+    /// <param name="assignedMenuKey">workflow 指派的應用程式 menu key。</param>
+    /// <param name="richMenuId">連結到使用者的 LINE richMenuId。</param>
+    /// <param name="changed">此次 link 是否改變使用者實際生效的選單。</param>
     public static LineRichMenuAssignmentResult Linked(string? previousMenuKey, string assignedMenuKey, string richMenuId, bool changed)
         => new(changed, previousMenuKey, assignedMenuKey, richMenuId);
 
+    /// <summary>
+    /// 建立成功但刻意不變更目前選單的結果。
+    /// </summary>
+    /// <param name="currentMenuKey">目前已知的應用程式 menu key。</param>
     public static LineRichMenuAssignmentResult NoChange(string? currentMenuKey)
         => new(false, currentMenuKey, currentMenuKey, null);
 
+    /// <summary>
+    /// 建立成功的 unlink 結果。
+    /// </summary>
+    /// <param name="previousMenuKey">從使用者身上移除的應用程式 menu key；若無資料則為 null。</param>
+    /// <param name="changed">是否真的移除了既有指派。</param>
     public static LineRichMenuAssignmentResult Unlinked(string? previousMenuKey, bool changed)
         => new(changed, previousMenuKey, null, null);
 
+    /// <summary>
+    /// 建立失敗的 RichMenu 指派結果。
+    /// </summary>
+    /// <param name="status">標準化失敗狀態。</param>
+    /// <param name="errorCode">穩定的應用程式錯誤代碼。</param>
+    /// <param name="errorMessage">可讀的失敗細節。</param>
     public static LineRichMenuAssignmentResult Failure(LineRichMenuStatus status, string errorCode, string errorMessage)
         => new(status, errorCode, errorMessage);
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
index 38898bb1..f957a4b2 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
@@ -22,11 +22,34 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWorkflow
 {
+    /// <summary>
+    /// 對 LINE RichMenu API 的抽象；所有 provider link / unlink / list 呼叫都從這裡出去。
+    /// </summary>
     private readonly ILineRichMenuProcessor _processor;
+
+    /// <summary>
+    /// menuKey 到 LINE richMenuId 的快取，避免每次指派都查詢 LINE 遠端清單。
+    /// </summary>
     private readonly ILineRichMenuIdCache _cache;
+
+    /// <summary>
+    /// 本機輔助狀態，用來記錄使用者目前與前一個 menuKey，支援未來還原與到期掃描。
+    /// </summary>
     private readonly IRichMenuStateStore _stateStore;
+
+    /// <summary>
+    /// 選用 catalog。當快取找不到 menuKey 時，workflow 會用 catalog definition 推算 fingerprint，
+    /// 再到 LINE 線上 RichMenu 清單尋找已佈建的同版選單。
+    /// </summary>
     private readonly ILineRichMenuCatalog? _catalog;
 
+    /// <summary>
+    /// 建立完整指派工作流。
+    /// </summary>
+    /// <param name="processor">LINE RichMenu API 抽象。</param>
+    /// <param name="cache">menuKey 到 richMenuId 的快取。</param>
+    /// <param name="stateStore">使用者 RichMenu 狀態儲存。</param>
+    /// <param name="catalog">可選 catalog，用於 cache miss 時嘗試線上解析 richMenuId。</param>
     public LineRichMenuAssignmentWorkflow(
         ILineRichMenuProcessor processor,
         ILineRichMenuIdCache cache,
@@ -39,6 +62,11 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         _catalog = catalog;
     }
 
+    /// <summary>
+    /// 建立使用 in-memory state store 的簡化指派工作流。
+    /// </summary>
+    /// <param name="processor">LINE RichMenu API 抽象。</param>
+    /// <param name="cache">menuKey 到 richMenuId 的快取。</param>
     public LineRichMenuAssignmentWorkflow(
         ILineRichMenuProcessor processor,
         ILineRichMenuIdCache cache)
@@ -46,6 +74,13 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
     {
     }
 
+    /// <summary>
+    /// 將指定 menuKey 對應的 RichMenu 指派給 LINE 使用者。
+    /// </summary>
+    /// <param name="lineUserId">LINE 使用者 id。</param>
+    /// <param name="menuKey">產品端邏輯選單代號。</param>
+    /// <param name="cancellationToken">取消權杖，用於 catalog 與 state store 操作。</param>
+    /// <returns>標準化指派結果，包含 richMenuId、前一個選單與是否真的變更。</returns>
     public async Task<LineRichMenuAssignmentResult> AssignAsync(
         string lineUserId,
         string menuKey,
@@ -104,6 +139,12 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         return LineRichMenuAssignmentResult.Linked(previous?.CurrentMenuKey, key, richMenuId, changed: true);
     }
 
+    /// <summary>
+    /// 執行 <see cref="AssignAsync"/>，並在失敗時丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="lineUserId">LINE 使用者 id。</param>
+    /// <param name="menuKey">產品端邏輯選單代號。</param>
+    /// <param name="cancellationToken">取消權杖。</param>
     public async Task AssignOrThrowAsync(
         string lineUserId,
         string menuKey,
@@ -116,6 +157,12 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         }
     }
 
+    /// <summary>
+    /// 解除使用者的 LINE RichMenu 個人綁定，並移除本機輔助狀態。
+    /// </summary>
+    /// <param name="lineUserId">LINE 使用者 id。</param>
+    /// <param name="cancellationToken">取消權杖，用於 state store 操作。</param>
+    /// <returns>標準化解除結果，包含前一個 menuKey 與 provider 錯誤分類。</returns>
     public async Task<LineRichMenuAssignmentResult> UnassignAsync(
         string lineUserId,
         CancellationToken cancellationToken = default)
@@ -143,6 +190,11 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         return LineRichMenuAssignmentResult.Unlinked(previous?.CurrentMenuKey, changed: true);
     }
 
+    /// <summary>
+    /// 執行 <see cref="UnassignAsync"/>，並在失敗時丟出 <see cref="LineRichMenuException"/>。
+    /// </summary>
+    /// <param name="lineUserId">LINE 使用者 id。</param>
+    /// <param name="cancellationToken">取消權杖。</param>
     public async Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default)
     {
         var result = await UnassignAsync(lineUserId, cancellationToken).ConfigureAwait(false);
@@ -152,6 +204,12 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         }
     }
 
+    /// <summary>
+    /// 解析 menuKey 對應的 LINE richMenuId。
+    /// 解析順序是快取優先，其次才用 catalog fingerprint 到 LINE 線上清單尋找已佈建選單。
+    /// </summary>
+    /// <param name="menuKey">產品端邏輯選單代號。</param>
+    /// <param name="cancellationToken">取消權杖，用於 catalog 與圖片 stream 讀取。</param>
     private async Task<(string? RichMenuId, LineRichMenuAssignmentResult? ProviderFailure)> ResolveRichMenuIdAsync(
         string menuKey,
         CancellationToken cancellationToken)
@@ -207,6 +265,9 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         return (matched.RichMenuId, null);
     }
 
+    /// <summary>
+    /// 驗證並修剪必要字串，避免空白 userId 或 menuKey 進入 provider 呼叫。
+    /// </summary>
     private static string NormalizeRequired(string value, string parameterName)
     {
         if (string.IsNullOrWhiteSpace(value))
@@ -217,6 +278,9 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         return value.Trim();
     }
 
+    /// <summary>
+    /// 將任意可讀取 stream 複製為 byte array，以便用於 fingerprint 計算。
+    /// </summary>
     private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
     {
         if (stream is MemoryStream memoryStream)
@@ -229,6 +293,9 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         return copy.ToArray();
     }
 
+    /// <summary>
+    /// 包住單一 LINE provider action，並只把已知 provider 邊界錯誤轉成標準結果。
+    /// </summary>
     private static async Task<LineRichMenuAssignmentResult?> TryExecuteProviderActionAsync(Func<Task> providerAction)
     {
         try
@@ -242,6 +309,9 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         }
     }
 
+    /// <summary>
+    /// 包住單一 LINE provider query，並在 provider 失敗時回傳標準失敗結果。
+    /// </summary>
     private static async Task<(T? Value, LineRichMenuAssignmentResult? Failure)> TryExecuteProviderQueryAsync<T>(
         Func<Task<T>> providerQuery)
     {
@@ -255,6 +325,10 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork
         }
     }
 
+    /// <summary>
+    /// 將 LINE / HTTP / timeout 例外轉成產品層可理解的標準指派結果。
+    /// 非 provider 類型的未知例外會回傳 false，讓原始例外直接往外拋。
+    /// </summary>
     private static bool TryMapProviderException(Exception exception, out LineRichMenuAssignmentResult result)
     {
         switch (exception)
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs b/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
index 4ae7f9ef..02769bcd 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
@@ -8,12 +8,25 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuCreateUploadAndLinkRequest
 {
+    /// <summary>
+    /// 要連結新 RichMenu 的 LINE 使用者 id。
+    /// </summary>
     public required string UserId { get; init; }
 
+    /// <summary>
+    /// 要建立到 LINE 的 RichMenu 版面、尺寸、chat bar 文字與 action area 設定。
+    /// </summary>
     public required RichMenu RichMenu { get; init; }
 
+    /// <summary>
+    /// 開啟 PNG 圖片 stream 的 factory。
+    /// 每次呼叫 workflow 時都應回傳可讀取的新 stream，讓上傳流程能完整讀取圖片內容。
+    /// </summary>
     public required Func<Stream> PngImageStreamFactory { get; init; }
 
+    /// <summary>
+    /// 呼叫端提供的追蹤資料；結果成功或失敗時都會保留。
+    /// </summary>
     public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
 }
 
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs b/LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
index d7f029a3..97827978 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
@@ -9,10 +9,24 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuDefinition
 {
+    /// <summary>
+    /// catalog 內部使用的穩定 menu key。
+    /// </summary>
     private string _menuKey;
+
+    /// <summary>
+    /// LINE RichMenu alias id，供 RichMenu switch action 與 provisioning workflow 使用。
+    /// </summary>
     private string _aliasId;
+
+    /// <summary>
+    /// LINE RichMenu 版面設定；包含尺寸、chat bar text 與所有可點擊 action areas。
+    /// </summary>
     private RichMenu _richMenu;
 
+    /// <summary>
+    /// 建立可供 object initializer 使用的空白 definition。
+    /// </summary>
     public LineRichMenuDefinition()
     {
         _menuKey = string.Empty;
@@ -21,6 +35,13 @@ public sealed class LineRichMenuDefinition
         PngImageStreamFactory = _ => Task.FromResult<Stream>(Stream.Null);
     }
 
+    /// <summary>
+    /// 以完整必要欄位建立 RichMenu definition。
+    /// </summary>
+    /// <param name="menuKey">產品端穩定識別這份選單的 menu key。</param>
+    /// <param name="aliasId">LINE RichMenu alias id。</param>
+    /// <param name="richMenu">要建立到 LINE 的 RichMenu 版面。</param>
+    /// <param name="pngImageStreamFactory">可依 cancellation token 開啟 PNG stream 的 factory。</param>
     public LineRichMenuDefinition(
         string menuKey,
         string aliasId,
@@ -33,36 +54,67 @@ public sealed class LineRichMenuDefinition
         PngImageStreamFactory = pngImageStreamFactory ?? throw new ArgumentNullException(nameof(pngImageStreamFactory));
     }
 
+    /// <summary>
+    /// 取得產品端用來指派 RichMenu 的穩定 menu key。
+    /// </summary>
     public string MenuKey => _menuKey;
 
+    /// <summary>
+    /// 取得 LINE RichMenu alias id。
+    /// </summary>
     public string AliasId => _aliasId;
 
+    /// <summary>
+    /// 取得 LINE RichMenu 版面設定。
+    /// </summary>
     public RichMenu RichMenu => _richMenu;
 
+    /// <summary>
+    /// 取得 PNG 圖片 stream factory。
+    /// provisioning 會用它讀取圖片內容、計算 fingerprint，並上傳到 LINE。
+    /// </summary>
     public Func<CancellationToken, Task<Stream>> PngImageStreamFactory { get; init; }
 
+    /// <summary>
+    /// 取得這份選單是否應設定為 LINE channel default RichMenu。
+    /// </summary>
     public bool IsDefault { get; init; }
 
+    /// <summary>
+    /// 取得產品端提供的描述文字，供管理畫面或日誌顯示。
+    /// </summary>
     public string? Description { get; init; }
 
+    /// <summary>
+    /// object initializer 友善別名，對應 <see cref="MenuKey"/>。
+    /// </summary>
     public string Key
     {
         get => _menuKey;
         init => _menuKey = NormalizeRequired(value, nameof(Key));
     }
 
+    /// <summary>
+    /// object initializer 友善別名，對應 <see cref="AliasId"/>。
+    /// </summary>
     public string Alias
     {
         get => _aliasId;
         init => _aliasId = NormalizeRequired(value, nameof(Alias));
     }
 
+    /// <summary>
+    /// object initializer 友善別名，對應 <see cref="RichMenu"/>。
+    /// </summary>
     public RichMenu Layout
     {
         get => _richMenu;
         init => _richMenu = value ?? throw new ArgumentNullException(nameof(Layout));
     }
 
+    /// <summary>
+    /// 正規化必要字串欄位，避免 catalog 以空白 key 或 alias 進入 provisioning。
+    /// </summary>
     private static string NormalizeRequired(string value, string parameterName)
     {
         if (string.IsNullOrWhiteSpace(value))
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs b/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
index d9d3b860..beb92cdf 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
@@ -6,8 +6,16 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuDeleteLinkedRequest
 {
+    /// <summary>
+    /// 目標 LINE 使用者 id。
+    /// workflow 會先用它查詢目前連結的 richMenuId，再解除連結並刪除該遠端 RichMenu。
+    /// </summary>
     public required string UserId { get; init; }
 
+    /// <summary>
+    /// 呼叫端提供的追蹤資料。
+    /// 這些資料不會送到 LINE，只會原樣回填到結果，方便管理端或日誌對照來源流程。
+    /// </summary>
     public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
 }
 
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuException.cs b/LineMessagingProcessor.RichMenus/LineRichMenuException.cs
index c1a391a2..37bd4a92 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuException.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuException.cs
@@ -6,12 +6,20 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuException : Exception
 {
+    /// <summary>
+    /// 以低階 RichMenu workflow 的失敗結果建立例外。
+    /// </summary>
+    /// <param name="result">包含狀態、錯誤碼與錯誤訊息的標準化結果。</param>
     public LineRichMenuException(LineRichMenuResult result)
         : base(result?.ErrorMessage ?? "LINE RichMenu workflow failed.")
     {
         Result = result ?? throw new ArgumentNullException(nameof(result));
     }
 
+    /// <summary>
+    /// 以使用者指派 workflow 的失敗結果建立例外。
+    /// </summary>
+    /// <param name="result">包含指派狀態、錯誤碼、richMenuId 與錯誤訊息的標準化結果。</param>
     public LineRichMenuException(LineRichMenuAssignmentResult result)
         : base(result?.ErrorMessage ?? "LINE RichMenu assignment failed.")
     {
@@ -26,7 +34,14 @@ public sealed class LineRichMenuException : Exception
             new Dictionary<string, string>());
     }
 
+    /// <summary>
+    /// 取得低階 RichMenu workflow 的標準化結果。
+    /// 即使例外源自 assignment workflow，也會轉成這個通用結果以維持舊呼叫端相容。
+    /// </summary>
     public LineRichMenuResult Result { get; }
 
+    /// <summary>
+    /// 取得原始 assignment workflow 結果；只有指派/解除綁定流程失敗時會有值。
+    /// </summary>
     public LineRichMenuAssignmentResult? AssignmentResult { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs b/LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
index 0fdf438e..31619d92 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
@@ -11,6 +11,11 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public static class LineRichMenuFingerprint
 {
+    /// <summary>
+    /// 依 catalog definition 與 PNG 內容產生 LINE RichMenu 的可版本化名稱。
+    /// </summary>
+    /// <param name="definition">包含 menu key 與 RichMenu 版面的 catalog definition。</param>
+    /// <param name="pngBytes">即將上傳到 LINE 的 PNG bytes。</param>
     public static string BuildName(LineRichMenuDefinition definition, byte[] pngBytes)
     {
         if (definition == null)
@@ -27,6 +32,11 @@ public static class LineRichMenuFingerprint
         return BuildName(definition, fingerprint);
     }
 
+    /// <summary>
+    /// 將預先計算好的 fingerprint 轉成可放入 LINE RichMenu name 的版本化名稱。
+    /// </summary>
+    /// <param name="definition">提供穩定 menu key 的 catalog definition。</param>
+    /// <param name="fingerprint">完整 SHA-256 fingerprint 字串。</param>
     public static string BuildName(LineRichMenuDefinition definition, string fingerprint)
     {
         if (definition == null)
@@ -47,6 +57,11 @@ public static class LineRichMenuFingerprint
         return baseName + suffix;
     }
 
+    /// <summary>
+    /// 以 RichMenu 版面 JSON 與 PNG 圖片內容建立 SHA-256 fingerprint。
+    /// </summary>
+    /// <param name="richMenu">要佈建的 LINE RichMenu 版面。</param>
+    /// <param name="pngBytes">與該版面配套的 PNG bytes。</param>
     public static string Create(RichMenu richMenu, byte[] pngBytes)
     {
         if (richMenu == null)
@@ -76,6 +91,10 @@ public static class LineRichMenuFingerprint
         return Convert.ToHexString(sha256.ComputeHash(merged)).ToLowerInvariant();
     }
 
+    /// <summary>
+    /// 取得 fingerprint 前段短碼，供 LINE RichMenu 名稱維持可讀且不超過長度限制。
+    /// </summary>
+    /// <param name="fingerprint">完整 fingerprint；必須至少 12 個字元。</param>
     public static string ShortVersion(string fingerprint)
     {
         if (string.IsNullOrWhiteSpace(fingerprint))
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs b/LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
index f5e880d5..5122ae9c 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
@@ -37,6 +37,12 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
     // 注意這不是使用者狀態快取，只保存「某個邏輯選單目前對應 LINE 哪個 richMenuId」。
     private readonly ILineRichMenuIdCache _cache;
 
+    /// <summary>
+    /// 建立 RichMenu catalog synchronization workflow。
+    /// </summary>
+    /// <param name="catalog">產品端宣告的 RichMenu catalog。</param>
+    /// <param name="processor">LINE RichMenu API 抽象。</param>
+    /// <param name="cache">同步完成後寫入的 menuKey 到 richMenuId 快取。</param>
     public LineRichMenuProvisioningWorkflow(
         ILineRichMenuCatalog catalog,
         ILineRichMenuProcessor processor,
@@ -47,6 +53,11 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
         _cache = cache ?? throw new ArgumentNullException(nameof(cache));
     }
 
+    /// <summary>
+    /// 將 catalog 內所有 RichMenu definition 同步到 LINE provider。
+    /// </summary>
+    /// <param name="cancellationToken">整批同步的取消權杖。</param>
+    /// <returns>包含 created、reused、failed item 與 menu id 對照表的同步報告。</returns>
     public async Task<LineRichMenuSyncReport> SyncAsync(CancellationToken cancellationToken = default)
     {
         // 第一步：從產品 catalog 取得所有要佈建的 RichMenu 定義。
@@ -114,6 +125,10 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
         return new LineRichMenuSyncReport(menuIds, created, reused, Array.Empty<string>(), items);
     }
 
+    /// <summary>
+    /// 同步單一 RichMenu definition。
+    /// 這個方法封裝 fingerprint 比對、create/upload、alias upsert、default 設定與 cache 更新。
+    /// </summary>
     private async Task SyncDefinitionAsync(
         LineRichMenuDefinition definition,
         IReadOnlyDictionary<string, ResponseRichMenu> existingByName,
@@ -187,6 +202,11 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
         items.Add(new LineRichMenuSyncItem(definition.MenuKey, richMenuId, LineRichMenuSyncOutcome.Created));
     }
 
+    /// <summary>
+    /// 建立或更新 LINE RichMenu alias，使穩定 alias id 指向目前版本的 richMenuId。
+    /// </summary>
+    /// <param name="aliasId">穩定 alias id。</param>
+    /// <param name="richMenuId">目前版本的 provider richMenuId。</param>
     private async Task UpsertAliasAsync(string aliasId, string richMenuId)
     {
         try
@@ -215,6 +235,8 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
     /// catalog 內的 RichMenu 是產品宣告資料，workflow 不應直接修改它。
     /// 這裡建立新物件可以避免「同步一次後輸入物件被改名」這類隱性副作用。
     /// </remarks>
+    /// <param name="source">catalog 宣告的原始 RichMenu 版面。</param>
+    /// <param name="name">要寫入 LINE provider 的 versioned name。</param>
     private static RichMenu CloneForProvisioning(RichMenu source, string name)
         => new()
         {
@@ -225,6 +247,11 @@ public sealed class LineRichMenuProvisioningWorkflow : ILineRichMenuProvisioning
             Areas = source.Areas
         };
 
+    /// <summary>
+    /// 將 PNG stream 完整讀成 bytes，以供 fingerprint 計算與後續重新上傳。
+    /// </summary>
+    /// <param name="stream">catalog definition 提供的圖片 stream。</param>
+    /// <param name="cancellationToken">讀取 stream 時的取消權杖。</param>
     private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
     {
         if (stream is MemoryStream memoryStream)
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs b/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
index 3d404201..fe3d3fb7 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
@@ -6,6 +6,10 @@ namespace LineMessagingProcessor.RichMenus;
 /// </summary>
 public sealed class LineRichMenuResult
 {
+    /// <summary>
+    /// 建立標準化 workflow 結果。
+    /// 透過 static factory 建立成功與失敗結果，讓呼叫端程式碼保持清楚可讀。
+    /// </summary>
     private LineRichMenuResult(
         bool succeeded,
         LineRichMenuStatus status,
@@ -26,25 +30,65 @@ public sealed class LineRichMenuResult
         Metadata = metadata;
     }
 
+    /// <summary>
+    /// 取得 workflow 是否成功完成。
+    /// </summary>
     public bool Succeeded { get; }
 
+    /// <summary>
+    /// 取得標準化 workflow 狀態。
+    /// </summary>
     public LineRichMenuStatus Status { get; }
 
+    /// <summary>
+    /// 取得 workflow 涉及的 LINE userId；若無資料則為 null。
+    /// </summary>
     public string? UserId { get; }
 
+    /// <summary>
+    /// 取得 workflow 建立、連結或刪除的 LINE richMenuId；若無資料則為 null。
+    /// </summary>
     public string? RichMenuId { get; }
 
+    /// <summary>
+    /// workflow 失敗時，取得穩定的應用程式錯誤代碼。
+    /// </summary>
     public string? ErrorCode { get; }
 
+    /// <summary>
+    /// workflow 失敗時，取得可讀的失敗細節。
+    /// </summary>
     public string? ErrorMessage { get; }
 
+    /// <summary>
+    /// 當失敗由 provider 或非預期錯誤造成時，取得原始例外。
+    /// </summary>
     public Exception? Exception { get; }
 
+    /// <summary>
+    /// 取得呼叫端提供且應隨成功或失敗結果一起流動的 metadata。
+    /// </summary>
     public IReadOnlyDictionary<string, string> Metadata { get; }
 
+    /// <summary>
+    /// 建立成功的 RichMenu workflow 結果。
+    /// </summary>
+    /// <param name="userId">此操作涉及的 LINE userId。</param>
+    /// <param name="richMenuId">此操作涉及的 provider richMenuId；若無資料則為 null。</param>
+    /// <param name="metadata">要保留在結果中的呼叫端 metadata。</param>
     public static LineRichMenuResult Success(string userId, string? richMenuId, IReadOnlyDictionary<string, string> metadata)
         => new(true, LineRichMenuStatus.Succeeded, userId, richMenuId, null, null, null, metadata);
 
+    /// <summary>
+    /// 建立失敗的 RichMenu workflow 結果，並包含標準化狀態與診斷資訊。
+    /// </summary>
+    /// <param name="userId">失敗操作涉及的 LINE userId；若已知才提供。</param>
+    /// <param name="richMenuId">失敗前已涉及的 provider richMenuId；若已知才提供。</param>
+    /// <param name="status">標準化失敗狀態。</param>
+    /// <param name="errorCode">穩定的應用程式錯誤代碼。</param>
+    /// <param name="errorMessage">可讀的失敗細節。</param>
+    /// <param name="exception">捕捉到的原始例外；若沒有則為 null。</param>
+    /// <param name="metadata">要保留在結果中的呼叫端 metadata。</param>
     public static LineRichMenuResult Failure(
         string? userId,
         string? richMenuId,
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs b/LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
index 01fc6687..f4b568f0 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
@@ -1,10 +1,33 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// RichMenu workflows 回傳的標準化狀態值。
+/// 這些值讓呼叫端不必直接依賴 LINE SDK exception 類型。
+/// </summary>
 public enum LineRichMenuStatus
 {
+    /// <summary>
+    /// workflow 已成功完成。
+    /// </summary>
     Succeeded,
+
+    /// <summary>
+    /// 在嘗試呼叫 LINE API 前，本機 request 驗證已失敗。
+    /// </summary>
     ValidationFailed,
+
+    /// <summary>
+    /// LINE 以 provider response 拒絕 request，例如 payload 錯誤或授權失敗。
+    /// </summary>
     ProviderRejected,
+
+    /// <summary>
+    /// LINE 或網路路徑無法使用、逾時，或在取得可信 provider response 前失敗。
+    /// </summary>
     ProviderUnavailable,
+
+    /// <summary>
+    /// 已知 provider 或 validation 分類以外的非預期應用程式錯誤。
+    /// </summary>
     UnexpectedError
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs b/LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
index 9e7f5c1f..69716171 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
@@ -1,7 +1,17 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 記錄單一應用程式 RichMenu definition 的同步結果。
+/// </summary>
 public sealed class LineRichMenuSyncItem
 {
+    /// <summary>
+    /// 建立單一選單的同步結果項目。
+    /// </summary>
+    /// <param name="menuKey">catalog definition 中的應用程式層級 menu key。</param>
+    /// <param name="richMenuId">已知的 LINE provider id；若同步失敗且尚未取得則可為空字串。</param>
+    /// <param name="outcome">此 definition 的同步結果。</param>
+    /// <param name="errorMessage">選填的 provider 或 validation 錯誤訊息。</param>
     public LineRichMenuSyncItem(
         string menuKey,
         string richMenuId,
@@ -14,11 +24,23 @@ public sealed class LineRichMenuSyncItem
         ErrorMessage = errorMessage;
     }
 
+    /// <summary>
+    /// 取得此項目代表的應用程式 menu key。
+    /// </summary>
     public string MenuKey { get; }
 
+    /// <summary>
+    /// 取得此選單建立或重用的 LINE richMenuId；若無資料則為空字串。
+    /// </summary>
     public string RichMenuId { get; }
 
+    /// <summary>
+    /// 取得此選單是新建、已最新或同步失敗。
+    /// </summary>
     public LineRichMenuSyncOutcome Outcome { get; }
 
+    /// <summary>
+    /// 取得失敗項目的錯誤細節。
+    /// </summary>
     public string? ErrorMessage { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs b/LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
index 9665fe9d..0194985d 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
@@ -1,8 +1,22 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 分類單一 RichMenu definition 的同步結果。
+/// </summary>
 public enum LineRichMenuSyncOutcome
 {
+    /// <summary>
+    /// 選單原本不存在於 LINE，已在同步期間建立。
+    /// </summary>
     Created,
+
+    /// <summary>
+    /// 選單已存在且 fingerprint 相符，因此不需要重新建立。
+    /// </summary>
     UpToDate,
+
+    /// <summary>
+    /// 選單同步失敗；workflow 仍可繼續處理後續 definitions。
+    /// </summary>
     Failed
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs b/LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
index ac32770c..c5f42997 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
@@ -1,7 +1,20 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 描述 RichMenu catalog 與 LINE 同步後的結果。
+/// report 將 provider ids、新建/重用/刪除集合與逐選單 outcome 分開，
+/// 讓呼叫端可記錄高階佈署狀態，同時保留調查單一選單失敗所需的資訊。
+/// </summary>
 public sealed class LineRichMenuSyncReport
 {
+    /// <summary>
+    /// 建立 RichMenu 同步報告。
+    /// </summary>
+    /// <param name="menuIds">已解析的應用程式 menu key 到 LINE richMenuId 對照。</param>
+    /// <param name="createdMenuKeys">本次同步中新建 LINE RichMenu 的應用程式 menu keys。</param>
+    /// <param name="reusedMenuKeys">與既有 fingerprinted LINE RichMenu 相符並被重用的應用程式 menu keys。</param>
+    /// <param name="deletedRichMenuIds">cleanup 期間刪除的 provider RichMenu ids。</param>
+    /// <param name="items">選填的逐 definition 同步結果。</param>
     public LineRichMenuSyncReport(
         IReadOnlyDictionary<string, string> menuIds,
         IReadOnlyList<string> createdMenuKeys,
@@ -16,13 +29,29 @@ public sealed class LineRichMenuSyncReport
         Items = items ?? Array.Empty<LineRichMenuSyncItem>();
     }
 
+    /// <summary>
+    /// 取得已解析的應用程式 menu key 到 LINE richMenuId 對照。
+    /// assignment workflows 會透過 <see cref="ILineRichMenuIdCache"/> 使用這些值。
+    /// </summary>
     public IReadOnlyDictionary<string, string> MenuIds { get; }
 
+    /// <summary>
+    /// 取得本次需要新建並上傳 LINE RichMenu 的 menu keys。
+    /// </summary>
     public IReadOnlyList<string> CreatedMenuKeys { get; }
 
+    /// <summary>
+    /// 取得 fingerprint 與既有 LINE RichMenu 相符、因此被重用的 menu keys。
+    /// </summary>
     public IReadOnlyList<string> ReusedMenuKeys { get; }
 
+    /// <summary>
+    /// 取得已從 LINE 移除的 richMenuIds；這些選單已不再由目前 catalog 擁有。
+    /// </summary>
     public IReadOnlyList<string> DeletedRichMenuIds { get; }
 
+    /// <summary>
+    /// 取得逐選單同步結果，包含未中止整體同步流程的單一選單失敗。
+    /// </summary>
     public IReadOnlyList<LineRichMenuSyncItem> Items { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
index 831c0813..1e97ffc9 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
@@ -1,6 +1,14 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 設定文字觸發的 RichMenu 切換。
+/// dictionary 中每筆資料都將一段精確的 LINE inbound message 對應到應指派的應用程式 menu key。
+/// </summary>
 public sealed class LineRichMenuTextTriggerOptions
 {
+    /// <summary>
+    /// 取得精確文字到 menu key 的對照表。
+    /// 預設 comparer 不分大小寫；resolver 查詢前仍會先 trim 前後空白。
+    /// </summary>
     public Dictionary<string, string> ExactTextToMenuKey { get; } = new(StringComparer.OrdinalIgnoreCase);
 }
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
index 7ae71db5..fa6427d1 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
@@ -8,11 +8,20 @@ public sealed class LineRichMenuTextTriggerPolicy : IRichMenuPolicy
 {
     private readonly ILineRichMenuTextTriggerResolver _resolver;
 
+    /// <summary>
+    /// 建立文字觸發 policy。
+    /// </summary>
+    /// <param name="resolver">將收到的 LINE 文字解析成 menu key 的 resolver。</param>
     public LineRichMenuTextTriggerPolicy(ILineRichMenuTextTriggerResolver resolver)
     {
         _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
     }
 
+    /// <summary>
+    /// 若收到的文字命中設定表，回傳高優先權的 RichMenu 指派決策。
+    /// </summary>
+    /// <param name="context">包含 received text 的使用者互動上下文。</param>
+    /// <param name="cancellationToken">此 in-memory policy 目前不使用，保留以符合 policy 介面。</param>
     public Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default)
     {
         if (context == null)
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
index 748499a4..fcc69f5d 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
@@ -1,14 +1,29 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 使用精確文字對照，將 LINE 傳入文字解析成應用程式 RichMenu key。
+/// resolver 會先 trim 使用者輸入，再依 options dictionary 的 comparer 決定是否區分大小寫。
+/// </summary>
 public sealed class LineRichMenuTextTriggerResolver : ILineRichMenuTextTriggerResolver
 {
+    /// <summary>
+    /// 將 trigger text 對應到應用程式 menu key 的設定。
+    /// </summary>
     private readonly LineRichMenuTextTriggerOptions _options;
 
+    /// <summary>
+    /// 使用傳入的 trigger options 建立 resolver。
+    /// </summary>
+    /// <param name="options">解析時使用的精確文字到 menu key 對照。</param>
     public LineRichMenuTextTriggerResolver(LineRichMenuTextTriggerOptions options)
     {
         _options = options ?? throw new ArgumentNullException(nameof(options));
     }
 
+    /// <summary>
+    /// 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
+    /// </summary>
+    /// <param name="receivedText">LINE 收到的原始文字。</param>
     public string? ResolveMenuKey(string? receivedText)
     {
         if (string.IsNullOrWhiteSpace(receivedText))
@@ -22,6 +37,11 @@ public sealed class LineRichMenuTextTriggerResolver : ILineRichMenuTextTriggerRe
             : null;
     }
 
+    /// <summary>
+    /// 嘗試解析 received text；沒有對照時透過 out 參數回傳空字串。
+    /// </summary>
+    /// <param name="receivedText">LINE 收到的原始文字。</param>
+    /// <param name="menuKey">方法回傳 true 時為解析出的 menu key；否則為空字串。</param>
     public bool TryResolve(string? receivedText, out string menuKey)
     {
         menuKey = ResolveMenuKey(receivedText) ?? string.Empty;
diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs b/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
index 9574f16f..cca2cb76 100644
--- a/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
@@ -22,6 +22,7 @@ namespace LineMessagingProcessor.RichMenus;
 public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow
 {
     // 對 LINE RichMenu API 的抽象。這裡不直接 new HTTP client，也不直接處理 token。
+    // 這讓舊流程可以沿用 create/upload/link/delete 能力，同時由測試以 processor 假物件精準模擬 provider 回應。
     private readonly ILineRichMenuProcessor _processor;
 
     public LineRichMenuWorkflow(ILineRichMenuProcessor processor)
diff --git a/LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs b/LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
index 4f593255..c6f1e5d4 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
@@ -2,11 +2,28 @@ using Line.Messaging;
 
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 建立可在 RichMenu aliases 之間切換使用者選單的 LINE template actions。
+/// 將 action 建立集中在此處，可讓需要 LINE <c>richmenuswitch</c> action type 的應用程式選單，
+/// 共用一致的 alias 驗證與 postback data 驗證規則。
+/// </summary>
 public static class RichMenuActionFactory
 {
+    /// <summary>
+    /// 建立指向指定 alias id 的 RichMenu switch action。
+    /// </summary>
+    /// <param name="aliasId">provisioning 期間設定的 LINE RichMenu alias id。</param>
+    /// <param name="data">使用者點擊 action 時，LINE webhook 回傳的 postback data。</param>
+    /// <param name="label">選填標籤，供會顯示 action 文字的 client 使用。</param>
     public static RichMenuSwitchTemplateAction SwitchToAlias(string aliasId, string data, string? label = null)
         => Switch(aliasId, data, label);
 
+    /// <summary>
+    /// 建立已驗證的 <see cref="RichMenuSwitchTemplateAction"/>。
+    /// </summary>
+    /// <param name="aliasId">LINE 會解析成目前 richMenuId 的 alias id。</param>
+    /// <param name="data">必要的 postback data payload。</param>
+    /// <param name="label">選填顯示標籤；未提供時會送出空字串。</param>
     public static RichMenuSwitchTemplateAction Switch(string aliasId, string data, string? label = null)
     {
         if (string.IsNullOrWhiteSpace(aliasId))
diff --git a/LineMessagingProcessor.RichMenus/RichMenuContext.cs b/LineMessagingProcessor.RichMenus/RichMenuContext.cs
index 4cf79852..b24d5801 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuContext.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuContext.cs
@@ -1,7 +1,19 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 攜帶 RichMenu policies 做 decision 時可能需要的所有使用者與訊息事實。
+/// context 刻意使用角色、屬性等應用程式概念，讓 policies 不必直接依賴資料庫 entity 或 LINE SDK payload 型別。
+/// </summary>
 public sealed class RichMenuContext
 {
+    /// <summary>
+    /// 建立單次 LINE 使用者互動的 policy evaluation context。
+    /// </summary>
+    /// <param name="lineUserId">正在評估的 LINE userId。</param>
+    /// <param name="roles">選填角色名稱，供 role-based policies 使用。</param>
+    /// <param name="receivedText">選填 LINE 傳入文字，通常供 trigger policies 使用。</param>
+    /// <param name="currentMenuKey">選填目前已指派給使用者的應用程式層級 menu key。</param>
+    /// <param name="attributes">選填額外 key/value 事實，供自訂 policies 使用。</param>
     public RichMenuContext(
         string lineUserId,
         IReadOnlySet<string>? roles = null,
@@ -21,13 +33,29 @@ public sealed class RichMenuContext
         Attributes = attributes ?? new Dictionary<string, string>();
     }
 
+    /// <summary>
+    /// 取得將傳入 assignment 與 unlink workflows 的 LINE userId。
+    /// </summary>
     public string LineUserId { get; }
 
+    /// <summary>
+    /// 取得 policy implementations 可使用的角色名稱。
+    /// 預設 comparer 不分大小寫，避免應用程式角色大小寫差異影響 decisions。
+    /// </summary>
     public IReadOnlySet<string> Roles { get; }
 
+    /// <summary>
+    /// 取得可能觸發 RichMenu 切換的訊息文字。
+    /// </summary>
     public string? ReceivedText { get; }
 
+    /// <summary>
+    /// 取得應用程式目前已知的 menu key。
+    /// </summary>
     public string? CurrentMenuKey { get; }
 
+    /// <summary>
+    /// 取得應用程式提供給自訂 policy logic 使用的額外事實。
+    /// </summary>
     public IReadOnlyDictionary<string, string> Attributes { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/RichMenuDecision.cs b/LineMessagingProcessor.RichMenus/RichMenuDecision.cs
index c5fa5406..c36afd88 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuDecision.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuDecision.cs
@@ -1,7 +1,16 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 表示 RichMenu policy 在呼叫 LINE API 前回傳的 outcome。
+/// decision 仍維持 provider-neutral：使用應用程式 menu key、priority、選填 TTL 與 reason，
+/// 讓 orchestrator 能可預期地挑出勝出的 action。
+/// </summary>
 public sealed class RichMenuDecision
 {
+    /// <summary>
+    /// 建立標準化 policy decision。
+    /// 使用 static factory methods 可讓 assign 與 unlink 語意保持明確。
+    /// </summary>
     private RichMenuDecision(string? menuKey, bool unlink, RichMenuDecisionPriority priority, TimeSpan? ttl, string reason)
     {
         MenuKey = menuKey;
@@ -11,18 +20,45 @@ public sealed class RichMenuDecision
         Reason = reason;
     }
 
+    /// <summary>
+    /// 取得要指派的應用程式層級 menu key。
+    /// no-op 與 unlink decisions 會是 null。
+    /// </summary>
     public string? MenuKey { get; }
 
+    /// <summary>
+    /// 取得此 decision 是否要求 orchestrator 移除使用者 RichMenu 連結。
+    /// </summary>
     public bool Unlink { get; }
 
+    /// <summary>
+    /// 取得多個 policies 回傳競爭 actions 時用來比較的 decision priority。
+    /// </summary>
     public RichMenuDecisionPriority Priority { get; }
 
+    /// <summary>
+    /// 取得此指派的選填有效期限。
+    /// 若有提供，assignment workflow 會寫入 state，讓後續 sweep 能還原前一個選單。
+    /// </summary>
     public TimeSpan? Ttl { get; }
 
+    /// <summary>
+    /// 取得供診斷、log 與測試斷言使用的可讀 reason。
+    /// </summary>
     public string Reason { get; }
 
+    /// <summary>
+    /// 取得可重用 decision，表示 orchestrator 應保持目前 RichMenu 不變。
+    /// </summary>
     public static RichMenuDecision None { get; } = new(null, false, RichMenuDecisionPriority.None, null, "none");
 
+    /// <summary>
+    /// 建立指派指定應用程式 menu key 的 decision。
+    /// </summary>
+    /// <param name="menuKey">稍後會解析成 LINE richMenuId 的應用程式層級 key。</param>
+    /// <param name="priority">此 decision 相對其他 policies 的強度。</param>
+    /// <param name="reason">說明 policy 為何選擇此選單的診斷 reason。</param>
+    /// <param name="ttl">此指派的選填暫時有效期限。</param>
     public static RichMenuDecision Assign(string menuKey, RichMenuDecisionPriority priority, string reason, TimeSpan? ttl = null)
     {
         if (string.IsNullOrWhiteSpace(menuKey))
@@ -33,6 +69,11 @@ public sealed class RichMenuDecision
         return new RichMenuDecision(menuKey.Trim(), false, priority, ttl, string.IsNullOrWhiteSpace(reason) ? "assign" : reason.Trim());
     }
 
+    /// <summary>
+    /// 建立移除使用者目前 RichMenu 指派的 decision。
+    /// </summary>
+    /// <param name="priority">此 unlink decision 相對其他 policies 的強度。</param>
+    /// <param name="reason">說明 policy 為何要求 unlink 的診斷 reason。</param>
     public static RichMenuDecision Remove(RichMenuDecisionPriority priority, string reason)
         => new(null, true, priority, null, string.IsNullOrWhiteSpace(reason) ? "unlink" : reason.Trim());
 }
diff --git a/LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs b/LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
index dde5723d..24066f30 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
@@ -1,10 +1,33 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 定義 RichMenu policy decision 覆蓋其他 decisions 的強度。
+/// orchestrator 評估同一使用者事件的多個 policies 時，數值較高者勝出。
+/// </summary>
 public enum RichMenuDecisionPriority
 {
+    /// <summary>
+    /// 沒有任何 policy decision。
+    /// </summary>
     None = 0,
+
+    /// <summary>
+    /// 預設或基準選單選擇。
+    /// </summary>
     Default = 10,
+
+    /// <summary>
+    /// 依角色選擇，例如為會員、同工或管理者指派選單。
+    /// </summary>
     Role = 50,
+
+    /// <summary>
+    /// 使用者文字命中已設定 trigger，應覆蓋預設或角色型選單。
+    /// </summary>
     TextTrigger = 80,
+
+    /// <summary>
+    /// 明確命令或直接 workflow request，應優先於其他 policies。
+    /// </summary>
     Explicit = 100
 }
diff --git a/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs b/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
index cb8c5bc0..c79acdc0 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
@@ -1,14 +1,30 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 彙總一次針對 RichMenu 使用者狀態的到期 sweep。
+/// report 刻意只公開計數，讓呼叫端可記錄或監控 sweep 成效，
+/// 而不需要依賴特定 state store 的 record shape。
+/// </summary>
 public sealed class RichMenuExpirationSweepReport
 {
+    /// <summary>
+    /// 建立 sweep report，包含掃描與成功還原的紀錄數。
+    /// </summary>
+    /// <param name="scannedCount">state store 回傳的已到期狀態紀錄數。</param>
+    /// <param name="restoredCount">成功還原或解除指派的紀錄數。</param>
     public RichMenuExpirationSweepReport(int scannedCount, int restoredCount)
     {
         ScannedCount = scannedCount;
         RestoredCount = restoredCount;
     }
 
+    /// <summary>
+    /// 取得 sweep 期間掃描到的已到期紀錄數。
+    /// </summary>
     public int ScannedCount { get; }
 
+    /// <summary>
+    /// 取得掃描紀錄中成功完成 RichMenu 還原或 unlink 的數量。
+    /// </summary>
     public int RestoredCount { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs b/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
index 20f1a428..d3f7e5d4 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
@@ -25,6 +25,11 @@ public sealed class RichMenuExpirationSweepWorkflow : IRichMenuExpirationSweepWo
     // 負責實際 Assign / Unassign 的共用工作流。
     private readonly ILineRichMenuAssignmentWorkflow _assignmentWorkflow;
 
+    /// <summary>
+    /// 建立到期掃描工作流。
+    /// </summary>
+    /// <param name="stateStore">保存使用者 RichMenu 狀態與到期時間的儲存抽象。</param>
+    /// <param name="assignmentWorkflow">用來還原前一個選單或解除綁定的共用指派工作流。</param>
     public RichMenuExpirationSweepWorkflow(
         IRichMenuStateStore stateStore,
         ILineRichMenuAssignmentWorkflow assignmentWorkflow)
@@ -33,6 +38,12 @@ public sealed class RichMenuExpirationSweepWorkflow : IRichMenuExpirationSweepWo
         _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
     }
 
+    /// <summary>
+    /// 找出已到期的 RichMenu 狀態，並逐一還原到上一個選單或解除個人綁定。
+    /// </summary>
+    /// <param name="now">用來判斷是否到期的目前時間。</param>
+    /// <param name="cancellationToken">背景工作停止或使用者取消時的取消權杖。</param>
+    /// <returns>本次掃描到期筆數與成功還原/解除筆數。</returns>
     public async Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
     {
         // 取得所有到期狀態。
diff --git a/LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs b/LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
index 47d93b5d..332430f2 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
@@ -26,6 +26,13 @@ public sealed class RichMenuOrchestrator : IRichMenuOrchestrator
     // Orchestrator 只決定「該做什麼」，assignment workflow 負責「怎麼呼叫 LINE」。
     private readonly ILineRichMenuAssignmentWorkflow _assignmentWorkflow;
 
+    /// <summary>
+    /// 建立 RichMenu 協調器，並接收產品端註冊的所有決策 policy。
+    /// </summary>
+    /// <param name="policies">
+    /// 由 DI 註冊進來的決策規則集合；若兩個 policy 回傳相同優先權，會保留先註冊者。
+    /// </param>
+    /// <param name="assignmentWorkflow">真正負責呼叫 LINE link / unlink 的共用指派工作流。</param>
     public RichMenuOrchestrator(
         IEnumerable<IRichMenuPolicy> policies,
         ILineRichMenuAssignmentWorkflow assignmentWorkflow)
@@ -34,6 +41,12 @@ public sealed class RichMenuOrchestrator : IRichMenuOrchestrator
         _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
     }
 
+    /// <summary>
+    /// 評估所有 RichMenu policy，選出最高優先權決策並套用到指定 LINE 使用者。
+    /// </summary>
+    /// <param name="context">包含 LINE user id、角色、收到文字、目前選單與產品屬性的決策上下文。</param>
+    /// <param name="cancellationToken">傳遞給 policy 與 assignment workflow 的取消權杖。</param>
+    /// <returns>標準化的指派結果，描述是否成功、是否變更，以及最後套用的 menu key。</returns>
     public async Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default)
     {
         if (context == null)
diff --git a/LineMessagingProcessor.RichMenus/RichMenuUserState.cs b/LineMessagingProcessor.RichMenus/RichMenuUserState.cs
index 264c4709..22eade1f 100644
--- a/LineMessagingProcessor.RichMenus/RichMenuUserState.cs
+++ b/LineMessagingProcessor.RichMenus/RichMenuUserState.cs
@@ -1,7 +1,20 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 保存單一 LINE 使用者的 RichMenu 指派狀態。
+/// 此狀態讓暫時性選單指派可在稍後還原或移除，
+/// 不必向 LINE 查詢只有應用程式才知道的業務 context。
+/// </summary>
 public sealed class RichMenuUserState
 {
+    /// <summary>
+    /// 建立描述使用者目前 RichMenu 與選填到期時間的 state record。
+    /// </summary>
+    /// <param name="lineUserId">擁有此 state record 的 LINE userId。</param>
+    /// <param name="currentMenuKey">目前指派給使用者的應用程式層級 menu key。</param>
+    /// <param name="previousMenuKey">到期後要還原的應用程式層級 menu key；若無則為 null。</param>
+    /// <param name="expiresAt">目前指派到期的 UTC-aware 時間點。</param>
+    /// <param name="updatedAt">此 state record 最後寫入的時間。</param>
     public RichMenuUserState(
         string lineUserId,
         string currentMenuKey,
@@ -16,13 +29,32 @@ public sealed class RichMenuUserState
         UpdatedAt = updatedAt;
     }
 
+    /// <summary>
+    /// 取得 LINE Messaging API link/unlink 操作使用的 LINE userId。
+    /// </summary>
     public string LineUserId { get; }
 
+    /// <summary>
+    /// 取得應用程式目前視為此使用者作用中的 menu key。
+    /// 此 key 稍後會透過 <see cref="ILineRichMenuIdCache"/> 解析成 provider richMenuId。
+    /// </summary>
     public string CurrentMenuKey { get; }
 
+    /// <summary>
+    /// 取得 <see cref="ExpiresAt"/> 經過後應還原的 menu key。
+    /// null 代表應解除使用者連結，讓使用者回到 LINE 預設行為。
+    /// </summary>
     public string? PreviousMenuKey { get; }
 
+    /// <summary>
+    /// 取得暫時性指派的到期時間。
+    /// 永久指派會將此值保留為 null，並被 expiration sweeps 忽略。
+    /// </summary>
     public DateTimeOffset? ExpiresAt { get; }
 
+    /// <summary>
+    /// 取得此指派狀態最後更新時間。
+    /// 此欄位可供 audit logs 使用，也可給需要可預期排序欄位的 store 使用。
+    /// </summary>
     public DateTimeOffset UpdatedAt { get; }
 }
diff --git a/LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs b/LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
index 3aae3fe4..9ddac0c4 100644
--- a/LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
+++ b/LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
@@ -1,14 +1,34 @@
 namespace LineMessagingProcessor.RichMenus;
 
+/// <summary>
+/// 提供固定的記憶體 RichMenu definitions catalog。
+/// 當應用程式在啟動時已知道所有選單，且希望 provisioning workflow 不必讀取資料庫、設定 provider 或遠端服務時，可使用此實作。
+/// </summary>
 public sealed class StaticLineRichMenuCatalog : ILineRichMenuCatalog
 {
+    /// <summary>
+    /// 建構式傳入 definitions 的不可變時間點快照。
+    /// 先複製成 list，可避免來源 enumerable 後續異動影響 provisioning workflow 要同步的選單。
+    /// </summary>
     private readonly IReadOnlyList<LineRichMenuDefinition> _definitions;
 
+    /// <summary>
+    /// 從傳入的 RichMenu definitions 建立靜態 catalog。
+    /// </summary>
+    /// <param name="definitions">
+    /// 要提供給同步 workflow 的完整 RichMenu definitions 集合。
+    /// </param>
     public StaticLineRichMenuCatalog(IEnumerable<LineRichMenuDefinition> definitions)
     {
         _definitions = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToList();
     }
 
+    /// <summary>
+    /// 回傳預先設定的 RichMenu definitions。
+    /// </summary>
+    /// <param name="cancellationToken">
+    /// 目前未使用；此實作沒有非同步 I/O，但保留此參數以符合會從外部來源載入選單的 catalog。
+    /// </param>
     public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
         => Task.FromResult(_definitions);
 }
diff --git a/LineMessagingProcessor/LineMessagingProcessorClass.cs b/LineMessagingProcessor/LineMessagingProcessorClass.cs
index 3372ad1d..5f916ef0 100644
--- a/LineMessagingProcessor/LineMessagingProcessorClass.cs
+++ b/LineMessagingProcessor/LineMessagingProcessorClass.cs
@@ -338,6 +338,11 @@ namespace LineMessagingProcessor
             await _lineMessagingClient.ReplyMessageAsync(replyToken, messages).ConfigureAwait(false);
         }
 
+        /// <summary>
+        /// RichMenu 相關方法是 LINE SDK 的薄封裝，故意不放產品 catalog、alias 決策或狀態儲存邏輯。
+        /// 上層共用 workflow 會負責將 ChurchReport 或其他產品的 menu key 轉成這裡需要的 provider richMenuId。
+        /// </summary>
+
         /// <summary>
         /// 建立 LINE RichMenu 並回傳 LINE 產生的 richMenuId。
         /// Processor 只包住 SDK 與必要參數驗證；RichMenu 版面、圖片與產品套用規則由產品端或 workflow 決定。
diff --git a/ToolUtility/PushUtility.cs b/ToolUtility/PushUtility.cs
index b0f670b0..1a9047cb 100644
--- a/ToolUtility/PushUtility.cs
+++ b/ToolUtility/PushUtility.cs
@@ -1,4 +1,4 @@
-﻿using System;
+using System;
 using System.Collections.Generic;
 using System.IO;
 using System.Linq;
@@ -273,10 +273,22 @@ namespace ToolUtility
                 //throw e;
             }
         }
+        /// <summary>
+        /// 舊版直接建立 RichMenu、上傳圖片並綁定到指定使用者的流程。
+        ///
+        /// 這段程式保留在 ToolUtility 舊工具中，描述早期產品直接操作 LINE provider 的生命週期：
+        /// 先建立 RichMenu 取得 provider 產生的 richMenuId，再從本機固定路徑讀取圖片，
+        /// 上傳為 RichMenu 圖片，最後把該 richMenuId link 到使用者並推送成功訊息。
+        ///
+        /// ChurchReport 目前已改由共用 RichMenu workflow / assignment workflow 管理 menu key、
+        /// richMenuId 快取、線上選單同步與解除綁定，避免每次呼叫都建立新 RichMenu 或依賴硬編碼圖片路徑。
+        /// </summary>
         public async Task<String> AddRichMenuMessage(string UserId)
         {
             try
             {
+                // 建立 LINE provider 需要的 RichMenu 定義；這裡只有一個全版面 postback 區塊，
+                // 屬於舊版示範式選單，不具備目前共用 catalog 的版本化命名與 fingerprint 機制。
                 RichMenu richMenu = new RichMenu()
                 {
                     Size = ImagemapSize.RichMenuLong,
@@ -297,6 +309,8 @@ namespace ToolUtility
                 //var image = new MemoryStream(File.ReadAllBytes(HttpContext.Current.Server.MapPath(@"~\Images\richmenu.PNG")));
                 //var image = new MemoryStream(File.ReadAllBytes(@"D:\\LINE 佈署\\Logo\\音訊科技\\SpeechMessage.png"));
 
+                // 舊版流程直接依賴伺服器本機固定路徑；部署環境若沒有這個檔案，
+                // RichMenu 建立後會在圖片讀取或上傳階段失敗，且 provider 端可能留下未使用的 richMenuId。
                 String path = @"D:\暫存區\richmenu.PNG";
 
                 byte[] readText = System.IO.File.ReadAllBytes(path);
@@ -305,11 +319,12 @@ namespace ToolUtility
 
                 //var image = new MemoryStream(byDataValue);
 
-                // Upload Image
+                // 將本機 PNG 圖片上傳到剛建立的 provider richMenuId。
                 await this.m_LineMessagingClient.UploadRichMenuPngImageAsync(image, richMenuId);
-                // Link to user
+                // 將 provider richMenuId 綁定到單一使用者；這裡沒有 menu key 抽象，也沒有快取或重試策略。
                 await this.m_LineMessagingClient.LinkRichMenuToUserAsync(UserId, richMenuId);
 
+                // 舊版方法會額外推送文字與貼圖通知，讓使用者知道選單已被建立並綁定。
                 ISendMessage replyMessage = new TextMessage("Rich menu added");
                 List<ISendMessage> MessageToSend = new List<ISendMessage>
                 {
@@ -329,13 +344,25 @@ namespace ToolUtility
                 throw e;
             }
         }
+        /// <summary>
+        /// 舊版直接解除使用者 RichMenu 並刪除 provider richMenuId 的流程。
+        ///
+        /// 此方法先向 LINE 查詢使用者目前綁定的 richMenuId，接著 unlink 使用者，
+        /// 最後直接刪除該 provider RichMenu。這個做法假設該 richMenuId 只屬於單一使用者；
+        /// 若同一選單被多位使用者或多個流程共用，直接刪除 provider 資源會影響其他人。
+        ///
+        /// 新版 ChurchReport 透過共用 assignment workflow 只處理使用者 unlink，
+        /// provider RichMenu 的建立、版本同步與刪除策略交由共用 provisioning / sweep 流程集中管理。
+        /// </summary>
         public async Task<String> DeleteRichMenuMessage(string UserId)
         {
             try
             {
-                // Get Rich Menu for the user
+                // 取得使用者目前在 LINE provider 端實際綁定的 richMenuId。
                 var richMenuId = await this.m_LineMessagingClient.GetRichMenuIdOfUserAsync(UserId);
+                // 先解除使用者與 RichMenu 的連結，避免刪除 provider 資源時仍有使用者指向它。
                 await m_LineMessagingClient.UnLinkRichMenuFromUserAsync(UserId);
+                // 舊版流程會直接刪除 provider RichMenu；新版共用流程避免在產品工具類中做這件事。
                 await m_LineMessagingClient.DeleteRichMenuAsync(richMenuId);
 
                 return "成功";

` 


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.