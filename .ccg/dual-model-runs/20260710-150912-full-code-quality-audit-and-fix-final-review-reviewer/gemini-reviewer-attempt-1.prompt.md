ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: full-code-quality-audit-and-fix-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.0.Initialization.Worktree

## Request
# Full Code Quality Audit And Fix Final Review Request

Review the current git diff for defects in:

- Session isolation and cross-user data leakage
- Memory cache lifetime, disposal, and bounded growth
- LINE and HTTP client ownership
- Sync-over-async request paths
- Object-level authorization
- CRM query performance and unbounded reads
- Secret/config handling

Use Critical / Warning / Info severity. Cite exact files and line numbers. Do not suggest broad rewrites unless a concrete defect remains.

Notes:

- Secret-like values in appsettings diff are intentionally redacted in this prompt, including commented historical examples. The actual branch blanks active-looking checked-in secrets and adds environment-variable fallback where needed.
- Do not treat test-only 
ew HttpClient(...) around in-memory handlers as production socket exhaustion unless a production call path is involved.
- The untracked code helper ToolUtility.Tests/TestHelpers/MockOrganizationServiceFactory.cs is included below because git diff does not include untracked files.
- Previous degraded CCG reviews found missing list date columns and a missing named LINE OAuth HttpClient registration; both are now fixed and regression-scanned.
- Previous degraded CCG review warned GetContactForKeyIn used TopCount = 1 without deterministic ordering; this diff now includes query.AddOrder("contactid", OrderType.Ascending) and a scan assertion.

Diff under review:

``text
diff --git a/ChurchReport.MemberInfo.Tests/Security/RequestPathHotspotScanTests.cs b/ChurchReport.MemberInfo.Tests/Security/RequestPathHotspotScanTests.cs
index 04f9393e..e649f9a5 100644
--- a/ChurchReport.MemberInfo.Tests/Security/RequestPathHotspotScanTests.cs
+++ b/ChurchReport.MemberInfo.Tests/Security/RequestPathHotspotScanTests.cs
@@ -29,6 +29,9 @@ public sealed class RequestPathHotspotScanTests
         source.Should().Contain("IHttpClientFactory");
         source.Should().Contain("CreateClient(\"LineLoginOAuth\")");
         source.Should().NotContain("new HttpClient(");
+
+        var startup = ReadRepositoryFile("SpeechMessageProducts.ChurchReport", "Startup.cs");
+        startup.Should().Contain("AddHttpClient(\"LineLoginOAuth\"");
     }
 
     [Fact]
@@ -56,6 +59,7 @@ public sealed class RequestPathHotspotScanTests
         var method = ExtractSourceSection(source, "private Entity GetContactForKeyIn", "private static readonly TimeSpan");
 
         method.Should().Contain("TopCount = 1");
+        method.Should().Contain("AddOrder(\"contactid\", OrderType.Ascending)");
         method.Should().Contain("new ColumnSet(");
         method.Should().Contain("\"new_lineid_backup\"");
         method.Should().NotContain("new ColumnSet(true)");
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs b/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
index 8e3a540d..5ed00c77 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs
@@ -372,7 +372,7 @@ namespace ChurchReport.Controllers
                 var callbackUrl = HttpContext.Session.GetString(LineLoginCallbackUrlSessionKey)
                     ?? ResolveLineLoginCallbackUrl(configuration);
 
-                using (var httpClient = new HttpClient())
+                using (var httpClient = CreateLineLoginOAuthHttpClient())
                 {
                     var requestData = new FormUrlEncodedContent(new[]
                     {
@@ -416,7 +416,7 @@ namespace ChurchReport.Controllers
         {
             try
             {
-                using (var httpClient = new HttpClient())
+                using (var httpClient = CreateLineLoginOAuthHttpClient())
                 {
                     httpClient.DefaultRequestHeaders.Authorization =
                         new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
@@ -447,6 +447,17 @@ namespace ChurchReport.Controllers
             }
         }
 
+        private HttpClient CreateLineLoginOAuthHttpClient()
+        {
+            var httpClientFactory = HttpContext?.RequestServices?.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
+            if (httpClientFactory == null)
+            {
+                throw new InvalidOperationException("IHttpClientFactory is required for LINE OAuth HTTP calls.");
+            }
+
+            return httpClientFactory.CreateClient("LineLoginOAuth");
+        }
+
         /// <summary>
         /// 處理 LINE 用戶登入
         /// </summary>
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs b/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
index 904ad6e0..33619043 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs
@@ -399,7 +399,7 @@ namespace ChurchReport.Controllers
         /// 訪問 URL: /Home/TestCachePerformance
         /// </summary>
         [Route("/Home/TestCachePerformance")]
-        public IActionResult TestCachePerformance()
+        public async Task<IActionResult> TestCachePerformance()
         {
             try
             {
@@ -436,7 +436,10 @@ namespace ChurchReport.Controllers
                 report += "\n\n";
 
                 // 清除快取以進行下一個測試
-                cacheService?.InvalidateAsync($"list_query_{testContactId}_vice_family_leader").Wait();
+                if (cacheService != null)
+                {
+                    await cacheService.InvalidateAsync($"list_query_{testContactId}_vice_family_leader");
+                }
 
                 return Content(report, "text/plain; charset=utf-8");
             }
diff --git a/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs b/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
index ebad9118..38b76540 100644
--- a/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.LineLogin.cs
@@ -35,11 +35,8 @@ namespace ChurchReport.Controllers
         {
             try
             {
-                var contactTask = Task.Run(() =>
-                    ToolUtility.RetrieveContactEntityByLineUserId(lineUserId),
-                    cancellationToken);
-
-                var contact = await contactTask.ConfigureAwait(false);
+                cancellationToken.ThrowIfCancellationRequested();
+                var contact = ToolUtility.RetrieveContactEntityByLineUserId(lineUserId);
 
                 if (contact == null)
                 {
@@ -63,21 +60,11 @@ namespace ChurchReport.Controllers
                     HttpContext?.Session?.SetString("_SessionUserId", lineUserId);
                     await IssueAuthTicketAsync(contact.Id.ToString(), "LineIdLogin", lineUserId, "LINE");
 
-                    var setupDataTask = Task.Run(() =>
-                        InMemoryContext.SetupSmallGroupData(
-                            fullName, "LineIdLogin", lineUserId, DateTime.Now, true),
-                        cancellationToken);
-
-                    var setupViewBagTask = Task.Run(() =>
-                        SetupViewBagForSmallGroup(),
-                        cancellationToken);
-
-                    var ensureDataTask = Task.Run(() =>
-                        EnsureIntegrateDataLoaded(lineUserId),
-                        cancellationToken);
-
-                    await Task.WhenAll(setupDataTask, setupViewBagTask, ensureDataTask)
-                        .ConfigureAwait(false);
+                    cancellationToken.ThrowIfCancellationRequested();
+                    InMemoryContext.SetupSmallGroupData(
+                        fullName, "LineIdLogin", lineUserId, DateTime.Now, true);
+                    SetupViewBagForSmallGroup();
+                    EnsureIntegrateDataLoaded(lineUserId);
 
                     return View("~/Views/Home/IntegrateView.cshtml",
                         InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport);
diff --git a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
index 317cbc30..c5796e27 100644
--- a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
+++ b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
@@ -43,7 +43,7 @@ namespace ChurchReport.Models
     public class DonationPaymentManager
     {
         #region 資料區
-        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
+        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddEnvironmentVariables();
         static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 商店編號
diff --git a/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs b/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
index db2c6f52..bf64518b 100644
--- a/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs
@@ -36,6 +36,7 @@ public sealed class ChurchReportLineAdminNotificationService
         new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables()
             .Build());
 
     private static readonly Lazy<ChurchReportLineAdminNotificationService> s_default = new(() =>
diff --git a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
index 7c5d73e0..aedd8294 100644
--- a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
@@ -45,7 +45,8 @@ namespace ChurchReport.Services
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/Startup.cs b/SpeechMessageProducts.ChurchReport/Startup.cs
index a8c0e3b2..af497061 100644
--- a/SpeechMessageProducts.ChurchReport/Startup.cs
+++ b/SpeechMessageProducts.ChurchReport/Startup.cs
@@ -162,6 +162,10 @@ namespace ChurchReport
             // 使用 HttpClientFactory 來管理 HttpClient 實例，避免記憶體洩漏問題。
             // 這是最佳實務，能夠重用連接並自動處理資源清理。
             services.AddHttpClient();
+            services.AddHttpClient("LineLoginOAuth", client =>
+            {
+                client.Timeout = TimeSpan.FromSeconds(30);
+            });
 
             // ========================================
             // 🔧 修復：MemoryCache 添加過期策略（不限制大小，避免登入卡住）
diff --git a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
index 11e2bdf0..0cc02f85 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
@@ -56,7 +56,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs b/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
index a8304e2a..9910590b 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs
@@ -31,7 +31,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
+                .AddEnvironmentVariables();
 
             return builder.Build();
         });
diff --git a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
index 473926f8..c87d9896 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
@@ -55,7 +55,8 @@ namespace ChurchReport.Tools
             // ?蔭撱箸??刻?撖虫?
             private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+                .AddEnvironmentVariables();
             private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
             // 敺?蝵株???Channel Access Token
diff --git a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
index 1f20349d..2ec44352 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
@@ -63,7 +63,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
index 51aefcb1..75448018 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
@@ -69,7 +69,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 追蹤等級
diff --git a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
index 5d34f91e..e05e4a17 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
@@ -42,7 +42,8 @@ namespace ChurchReport.Tools
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
         private static IConfiguration m_Configuration => s_lazyConfiguration.Value;
diff --git a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
index 5bacefbd..2c8d1bb1 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
@@ -73,7 +73,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
index caf4f51f..98a53719 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
@@ -63,7 +63,8 @@ namespace ChurchReport.Tools
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
index feb27d2e..c70234a6 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
@@ -51,7 +51,8 @@ namespace ChurchReport.WebServiceConnector
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
+                .AddEnvironmentVariables();
             return builder.Build();
         });
 
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
index de94f188..2b008cc6 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
@@ -261,9 +261,29 @@ namespace ChurchReport.WebServiceConnector
                     return null;
                 }
 
-                var query = new QueryByAttribute("contact") { ColumnSet = new ColumnSet(true) };
-                query.Attributes.AddRange("pager", "fullname", "statecode");
-                query.Values.AddRange(DonationPaymentFormModel.DedicationNumber, DonationPaymentFormModel.FullName, 0);
+                var query = new QueryExpression("contact")
+                {
+                    ColumnSet = new ColumnSet(
+                        "contactid",
+                        "fullname",
+                        "pager",
+                        "new_personal_id",
+                        "new_lineid",
+                        "new_lineid_backup",
+                        "parentcustomerid",
+                        "ownerid"),
+                    Criteria = new FilterExpression(LogicalOperator.And)
+                    {
+                        Conditions =
+                        {
+                            new ConditionExpression("pager", ConditionOperator.Equal, DonationPaymentFormModel.DedicationNumber),
+                            new ConditionExpression("fullname", ConditionOperator.Equal, DonationPaymentFormModel.FullName),
+                            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
+                        }
+                    },
+                    TopCount = 1
+                };
+                query.AddOrder("contactid", OrderType.Ascending);
 
                 var matches = m_ToolUtilityClass.m_Crm2011OrganizationService.RetrieveMultiple(query);
                 return matches.Entities.Count > 0 ? matches.Entities[0] : null;
diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
index e5a058cb..fb857dc9 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
@@ -48,7 +48,8 @@ namespace ChurchReport.WebServiceConnector
         // 配置管理
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
+            .AddEnvironmentVariables();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
         #endregion
         #region 常數參數
diff --git a/SpeechMessageProducts.ChurchReport/appsettings.json b/SpeechMessageProducts.ChurchReport/appsettings.json
index 3c561116..4125c8ff 100644
--- a/SpeechMessageProducts.ChurchReport/appsettings.json
+++ b/SpeechMessageProducts.ChurchReport/appsettings.json
@@ -167,11 +167,11 @@
   "LineMessaging": {
     // 好牧人 Line 2.0 (雲端機房)
     "Jesus": {
-      "ChannelAccessToken": "[REDACTED]"
+      "ChannelAccessToken": "[REDACTED]"
     },
     // 好牧人 Line 2.0 (公司內部機房)
     "JesusBack": {
-      "ChannelAccessToken": "[REDACTED]"
+      "ChannelAccessToken": "[REDACTED]"
     },
     // 預設組織 (需與 LineMessaging 區段的 key 名稱大小寫一致)
     "DefaultOrganization": "Jesus"
@@ -184,7 +184,7 @@
   // 請在 LINE Developers Console 建立 LINE Login Channel 並填入以下資訊
   "LineLogin": {
     "ChannelId": "2007621061", // ✅ LINE Login Channel ID
-    "ChannelSecret": "[REDACTED]", // ✅ LINE Login Channel Secret
+    "ChannelSecret": "[REDACTED]", // ✅ LINE Login Channel Secret
     "CallbackUrl": "https://jesus.speechmessage.com.tw:807/Authentication/LineCallback", // ✅ Callback URL（請根據實際環境修改）
     "Scope": "profile openid", // 請求的權限範圍
     "State": "random_state_string" // CSRF 防護用的 state 參數（每次動態生成）
@@ -209,7 +209,7 @@
   "MiniApp": {
     // Mini App Channel 基本資訊
     "ChannelId": "2009427707", // Mini App Channel ID（目前使用 Developing 環境）
-    "ChannelSecret": "[REDACTED]", // Mini App Channel Secret（目前使用 Developing 環境）
+    "ChannelSecret": "[REDACTED]", // Mini App Channel Secret（目前使用 Developing 環境）
 
     // 三個環境的 LIFF ID（在 Console 建立 Mini App Channel 後會自動產生）
     "DevelopingLiffId": "2009427707-Fi5L5blD", // Developing 環境 LIFF ID
@@ -248,14 +248,14 @@
     "Domain": "DYNAMICS-365", // CRM 網域
     "ServerUrl": "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc", // CRM 伺服器網址
     "Username": "SPEECHMESSAGE\\Administrator", // CRM 使用者名稱
-    "Password": "[REDACTED]", // CRM 密碼
+    "Password": "[REDACTED]", // CRM 密碼
 
     // 公司內部機房
     //"Organization": "jesusback", // CRM 組織名稱
     //"Domain": "SPEECHMESSAGE", // CRM 網域
     //"ServerUrl": "https://jesusback.speechmessage.com.tw/XRMServices/2011/Organization.svc", // CRM 伺服器網址
     //"Username": "SPEECHMESSAGE\\Administrator", // CRM 使用者名稱
-    //"Password": "[REDACTED]", // CRM 密碼
+    //"Password": "[REDACTED]", // CRM 密碼
 
     "MinPoolSize": 3, // 最小連接池大小
     "MaxPoolSize": 20, // 最大連接池大小
@@ -268,7 +268,7 @@
   // ==============================================
   "LinePay": {
     "ChannelId": "1634548482", // LINE Pay 通道 ID
-    "ChannelSecret": "[REDACTED]", // LINE Pay 通道密鑰
+    "ChannelSecret": "[REDACTED]", // LINE Pay 通道密鑰
     "IsSandbox": true // 是否使用沙盒測試環境
   },
 
@@ -294,11 +294,11 @@
         "Environment": "Sandbox",
         "Credentials": {
           "ShopNo": "NA0149_001",
-          "A1": "5E854757C751413F",
-          "A2": "D743D0EB06904837",
-          "B1": "08169D5445644513",
-          "B2": "8E52B5A180EE4399",
-          "XKeyId": "[REDACTED]"
+          "A1": "",
+          "A2": "",
+          "B1": "",
+          "B2": "",
+          "XKeyId": "[REDACTED]"
         },
         "Endpoints": {
           "ApiBaseUrl": "https://sandbox.sinopac.com/QPay.WebAPI/api/"
@@ -309,7 +309,7 @@
         "Environment": "Production",
         "Credentials": {
           "StoreId": "130544850001",
-          "Key": "[REDACTED]",
+          "Key": "[REDACTED]",
           "IV": "[REDACTED]"
         },
         "Endpoints": {
@@ -321,8 +321,8 @@
         "Environment": "Sandbox",
         "Credentials": {
           "StoreId": "999812777000199",
-          "StoreKey": "[REDACTED]",
-          "StoreIV": "[REDACTED]",
+          "StoreKey": "[REDACTED]",
+          "StoreIV": "[REDACTED]",
           "TerminalId": "T0000000",
           "MerchantId": "999812777000199"
         },
@@ -339,11 +339,11 @@
   "Sinopac": {
     "Site": "https://api.sinopac.com/funBIZ/QPay.WebAPI/api/", // 正式環境 API 網址
     "ShopNo": "DA4272_001", // 商店代號
-    "A1": "00DC1BDACCB645C6", // 加密金鑰 A1
-    "A2": "185B6F59F737462E", // 加密金鑰 A2
-    "B1": "6F9C2936E8524F76", // 加密金鑰 B1
-    "B2": "8BB48C2260304E29", // 加密金鑰 B2
-    "XKeyID": "[REDACTED]" // X-Key 識別碼
+    "A1": "", // 加密金鑰 A1
+    "A2": "", // 加密金鑰 A2
+    "B1": "", // 加密金鑰 B1
+    "B2": "", // 加密金鑰 B2
+    "XKeyID": "[REDACTED]" // X-Key 識別碼
   },
 
   // ==============================================
@@ -354,11 +354,11 @@
     //"Site": "https://apisbx.sinopac.com/funBIZ-Sbx/QPay.WebAPI/api/", // 沙盒環境 API 網址
     "Site": "https://sandbox.sinopac.com/QPay.WebAPI/api/", // 沙盒環境 API 網址
     "ShopNo": "NA0149_001", // 測試商店代號
-    "A1": "5E854757C751413F", // 測試加密金鑰 A1
-    "A2": "D743D0EB06904837", // 測試加密金鑰 A2
-    "B1": "08169D5445644513", // 測試加密金鑰 B1
-    "B2": "8E52B5A180EE4399", // 測試加密金鑰 B2
-    "XKeyID": "[REDACTED]" // 測試 X-Key 識別碼
+    "A1": "", // 測試加密金鑰 A1
+    "A2": "", // 測試加密金鑰 A2
+    "B1": "", // 測試加密金鑰 B1
+    "B2": "", // 測試加密金鑰 B2
+    "XKeyID": "[REDACTED]" // 測試 X-Key 識別碼
   },
 
   // ==============================================
@@ -367,12 +367,12 @@
   "MyPay": {
     // --- 基本商店資訊 (Basic Store Information) ---
     "Store_Id": "130544850001", // 音訊科技商店代號
-    "Key": "[REDACTED]", // 音訊科技加密金鑰
+    "Key": "[REDACTED]", // 音訊科技加密金鑰
     "Url": "https://ka.usecase.cc/api/init", // 測試環境 API 初始化網址
     //"Url": "https://ka.usecase.cc/api/agent", // 測試環境 API 初始化網址
 
     //"Store_Id": "200043350001", // 好牧人商店代號
-    //"Key": "[REDACTED]", // 好牧人加密金鑰
+    //"Key": "[REDACTED]", // 好牧人加密金鑰
     ///"Url": "https://ka.mypay.tw/api/init", // 正式環境 API 初始化網址
     ////"Url": "https://ka.mypay.tw/api/agent", // 正式環境 API 初始化網址
 
@@ -498,8 +498,8 @@
   "TSPG": {
     // --- 基本商店資訊 (Basic Store Information) ---
     "StoreId": "999812777000199", // 特店代號 (正式或測試)
-    "StoreKey": "[REDACTED]", // Hash Key (商店金鑰)需要替換為實際值
-    "StoreIV": "[REDACTED]", // Hash IV (初始向量)需要替換為實際值
+    "StoreKey": "[REDACTED]", // Hash Key (商店金鑰)需要替換為實際值
+    "StoreIV": "[REDACTED]", // Hash IV (初始向量)需要替換為實際值
     "ApiBaseUrl": "https://tspg-t.taishinbank.com.tw/tspgapi/restapi", // API 基礎網址 (測試環境)
 
     // --- 特店與端末設定 (Merchant and Terminal Settings) ---
diff --git a/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs b/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
index 7606ecec..a040a2c0 100644
--- a/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
+++ b/ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
@@ -27,11 +27,11 @@ namespace ToolUtility.Tests.AttachmentOperations
         public void DownloadAttachment_WhenCalled_ShouldReturnCollection()
         {
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
 
-            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);
+            var service = new AttachmentService(mockLogger.Object, mockCrm.Object);
 
-            var crm = (IOrganizationService)null;
+            var crm = mockCrm.Object;
             var result = service.DownloadAttachment(ref crm, Guid.NewGuid());
 
             result.Should().NotBeNull();
@@ -42,15 +42,15 @@ namespace ToolUtility.Tests.AttachmentOperations
         public void UploadAttachment_WhenCalled_ShouldCreateAnnotation()
         {
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
 
-            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);
+            var service = new AttachmentService(mockLogger.Object, mockCrm.Object);
 
-            var crm = (IOrganizationService)null;
+            var crm = mockCrm.Object;
 
             service.UploadAttachment(ref crm, "contact", "sub", "note", "file.txt", "text/plain", new byte[] {1,2,3}, Guid.NewGuid());
 
-            Assert.True(true);
+            mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
         }
     }
 }
diff --git a/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs b/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
index c49b24c3..31fa518e 100644
--- a/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
+++ b/ToolUtility.Tests/ContactOperations/ContactServiceTests.cs
@@ -15,8 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.ContactOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.EntityOperations;
-using Moq;
 using System;
 using Microsoft.Xrm.Sdk;
 using Microsoft.Xrm.Sdk.Query;
@@ -30,12 +28,11 @@ namespace ToolUtility.Tests.ContactOperations
         {
             var expected = TestEntityFactory.CreateContact("U123456", "測試聯絡人");
 
-            var mockQueryService = new Mock<IEntityQueryService>();
-            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
-                .Returns(new EntityCollection(new[] { expected }));
+            var mockOrganizationService = MockOrganizationServiceFactory.CreateMockWithCollection(
+                new EntityCollection(new[] { expected }));
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var service = new ContactService(mockLogger.Object, mockQueryService.Object);
+            var service = new ContactService(mockLogger.Object, mockOrganizationService.Object);
 
             var result = service.RetrieveByLineId("U123456");
 
@@ -52,12 +49,10 @@ namespace ToolUtility.Tests.ContactOperations
                 TestEntityFactory.CreateContact("U456", "B")
             });
 
-            var mockQueryService = new Mock<IEntityQueryService>();
-            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
-                .Returns(collection);
+            var mockOrganizationService = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var service = new ContactService(mockLogger.Object, mockQueryService.Object);
+            var service = new ContactService(mockLogger.Object, mockOrganizationService.Object);
 
             var result = service.RetrieveCollectionByName("A");
 
diff --git a/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs b/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
index 5df10472..474bae6c 100644
--- a/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
+++ b/ToolUtility.Tests/Core/ToolUtilityClassIntegrationTests.cs
@@ -31,10 +31,10 @@ namespace ToolUtility.Tests.Core
             var expected = TestEntityFactory.CreateContact("U123", "測試");
             var collection = new EntityCollection(new[] { expected });
 
-            var mockCrm = MockCrmClientFactory.CreateMockWithCollection(collection);
+            var mockCrm = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             // Act
             var result = facade.RetrieveContactByLineId("U123");
@@ -48,10 +48,10 @@ namespace ToolUtility.Tests.Core
         public void SetEntityBoolAttribute_ShouldDelegateToAttributeService()
         {
             // Arrange
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var entity = new Entity("contact");
 
diff --git a/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs b/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
index a540c0bf..86621800 100644
--- a/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
+++ b/ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
@@ -15,6 +15,7 @@ using Xunit;
 using FluentAssertions;
 using Moq;
 using System;
+using Microsoft.Crm.Sdk.Messages;
 using Microsoft.Xrm.Sdk;
 using ToolUtilityNameSpace.Core;
 using ToolUtility.Tests.TestHelpers;
@@ -29,13 +30,13 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void Create_Update_Delete_Entity_Via_Facade()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var createdId = Guid.NewGuid();
             mockCrm.Setup(x => x.Create(It.IsAny<Entity>())).Returns(createdId);
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var entity = new Entity("account") { ["name"] = "TDD Test" };
 
@@ -56,11 +57,11 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void UploadAttachment_ShouldCallCreateAnnotation()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
-            var crmService = (IOrganizationService)null;
+            var crmService = mockCrm.Object;
             facade.UploadAnAttachment(ref crmService, "contact", "sub", "note", "file.txt", "text/plain", new byte[] { 1,2,3 }, Guid.NewGuid());
 
             mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
@@ -69,37 +70,53 @@ namespace ToolUtility.Tests.Core
         [Fact]
         public void AddAndRemoveMembersToMarketingList_ShouldCallListService()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             var listId = Guid.NewGuid();
             var members = new System.Collections.Generic.List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
 
             facade.AddMembersToMarketingList(listId, members);
 
-            // Verify create called for each member (ListService calls ICrmClient.Create)
-            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "listmember")), Times.Exactly(members.Count));
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsAddListMembersRequest(request, listId, members.Count))), Times.Once);
 
             var memberToRemove = members[0];
             facade.RemoveMembersToMarketingList(listId, memberToRemove);
 
-            // Removal in our simple impl calls Delete on list entity - verify Delete called
-            mockCrm.Verify(x => x.Delete("list", It.IsAny<Guid>()), Times.AtLeastOnce);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsRemoveMemberRequest(request, listId, memberToRemove))), Times.Once);
         }
 
         [Fact]
         public void CreatePushLineMessage_ShouldCallCrudCreate()
         {
-            var mockCrm = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);
+            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);
 
             facade.CreatePushLineMessage("U123", "sub", "hello");
 
             // LineMessageService creates an entity via IEntityCrudService which uses ICrmClient.Create
             mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
         }
+
+        private static bool IsAddListMembersRequest(OrganizationRequest request, Guid listId, int memberCount)
+        {
+            var addRequest = request as AddListMembersListRequest;
+            return addRequest != null &&
+                addRequest.ListId == listId &&
+                addRequest.MemberIds.Length == memberCount;
+        }
+
+        private static bool IsRemoveMemberRequest(OrganizationRequest request, Guid listId, Guid memberId)
+        {
+            var removeRequest = request as RemoveMemberListRequest;
+            return removeRequest != null &&
+                removeRequest.ListId == listId &&
+                removeRequest.EntityId == memberId;
+        }
     }
 }
diff --git a/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs b/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
index bff056b0..8a9ae90f 100644
--- a/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
+++ b/ToolUtility.Tests/EntityOperations/EntityCrudServiceTests.cs
@@ -15,7 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.EntityOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.Interfaces;
 using Microsoft.Xrm.Sdk;
 using Moq;
 using System;
@@ -28,7 +27,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void CreateEntity_ShouldReturnGuid()
         {
             var entity = TestEntityFactory.CreateEmpty("contact");
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
@@ -44,7 +43,7 @@ namespace ToolUtility.Tests.EntityOperations
             var entity = TestEntityFactory.CreateEmpty("contact");
             entity["fullname"] = "new name";
 
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
@@ -58,7 +57,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void DeleteEntity_ShouldCallClient()
         {
             var id = Guid.NewGuid();
-            var mockClient = MockCrmClientFactory.CreateMock();
+            var mockClient = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
             var service = new EntityCrudService(mockLogger.Object, mockClient.Object);
diff --git a/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs b/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
index 0269e213..28da0153 100644
--- a/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
+++ b/ToolUtility.Tests/EntityOperations/EntityQueryServiceTests.cs
@@ -15,7 +15,6 @@ using Xunit;
 using FluentAssertions;
 using ToolUtilityNameSpace.EntityOperations;
 using ToolUtility.Tests.TestHelpers;
-using ToolUtilityNameSpace.Interfaces;
 using Microsoft.Xrm.Sdk;
 using Microsoft.Xrm.Sdk.Query;
 using Moq;
@@ -29,7 +28,7 @@ namespace ToolUtility.Tests.EntityOperations
         public void RetrieveEntity_WhenEntityExists_ShouldReturnEntity()
         {
             var expected = TestEntityFactory.CreateContact("U123", "測試");
-            var mockClient = MockCrmClientFactory.CreateMockWithEntity(expected);
+            var mockClient = MockOrganizationServiceFactory.CreateMockWithEntity(expected);
 
             var mockLogger = MockLoggerFactory.CreateMock<object>();
             var service = new EntityQueryService(mockLogger.Object, mockClient.Object);
@@ -49,7 +48,7 @@ namespace ToolUtility.Tests.EntityOperations
                 TestEntityFactory.CreateContact("U456", "測試2")
             });
 
-            var mockClient = MockCrmClientFactory.CreateMockWithCollection(collection);
+            var mockClient = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
             var mockLogger = MockLoggerFactory.CreateMock<object>();
             var service = new EntityQueryService(mockLogger.Object, mockClient.Object);
 
diff --git a/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs b/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
index 26317cdc..7047d406 100644
--- a/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
+++ b/ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs
@@ -16,7 +16,6 @@ using FluentAssertions;
 using ToolUtilityNameSpace.LineMessaging;
 using ToolUtility.Tests.TestHelpers;
 using Moq;
-using ToolUtilityNameSpace.EntityOperations;
 using System;
 using Microsoft.Xrm.Sdk;
 
@@ -27,14 +26,14 @@ namespace ToolUtility.Tests.LineMessaging
         [Fact]
         public void CreatePushMessage_ShouldCallCreateEntity()
         {
-            var mockCrud = new Mock<IEntityCrudService>();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new LineMessageService(mockLogger.Object, mockCrud.Object);
+            var service = new LineMessageService(mockLogger.Object, mockCrm.Object);
 
             service.CreatePushMessage("U123", "sub", "hello");
 
-            mockCrud.Verify(x => x.CreateEntity(It.IsAny<Entity>()), Times.Once);
+            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
         }
     }
 }
diff --git a/ToolUtility.Tests/ListOperations/ListServiceTests.cs b/ToolUtility.Tests/ListOperations/ListServiceTests.cs
index 7613b387..99542bce 100644
--- a/ToolUtility.Tests/ListOperations/ListServiceTests.cs
+++ b/ToolUtility.Tests/ListOperations/ListServiceTests.cs
@@ -18,8 +18,8 @@ using ToolUtility.Tests.TestHelpers;
 using Moq;
 using System;
 using System.Collections.Generic;
-using ToolUtilityNameSpace.EntityOperations;
-using ToolUtilityNameSpace.EntityOperations;
+using Microsoft.Crm.Sdk.Messages;
+using Microsoft.Xrm.Sdk;
 
 namespace ToolUtility.Tests.ListOperations
 {
@@ -28,37 +28,51 @@ namespace ToolUtility.Tests.ListOperations
         [Fact]
         public void AddMembers_ShouldCallCreateForEachMember()
         {
-            var mockQuery = new Mock<IEntityQueryService>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new ListService(mockLogger.Object, mockQuery.Object, mockCrudClient.Object);
+            var service = new ListService(mockLogger.Object, mockCrm.Object);
 
             var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
             var listId = Guid.NewGuid();
 
             service.AddMembers(listId, members);
 
-            // No exception means success for this simple impl
-            Assert.True(true);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsAddListMembersRequest(request, listId, members.Count))), Times.Once);
         }
 
         [Fact]
         public void RemoveMember_ShouldCallDelete()
         {
-            var mockQuery = new Mock<IEntityQueryService>();
-            var mockCrudClient = MockCrmClientFactory.CreateMock();
+            var mockCrm = MockOrganizationServiceFactory.CreateMock();
             var mockLogger = MockLoggerFactory.CreateMock<object>();
 
-            var service = new ListService(mockLogger.Object, mockQuery.Object, mockCrudClient.Object);
+            var service = new ListService(mockLogger.Object, mockCrm.Object);
 
             var member = Guid.NewGuid();
             var listId = Guid.NewGuid();
 
             service.RemoveMember(listId, member);
 
-            // No exception means success
-            Assert.True(true);
+            mockCrm.Verify(x => x.Execute(It.Is<OrganizationRequest>(request =>
+                IsRemoveMemberRequest(request, listId, member))), Times.Once);
+        }
+
+        private static bool IsAddListMembersRequest(OrganizationRequest request, Guid listId, int memberCount)
+        {
+            var addRequest = request as AddListMembersListRequest;
+            return addRequest != null &&
+                addRequest.ListId == listId &&
+                addRequest.MemberIds.Length == memberCount;
+        }
+
+        private static bool IsRemoveMemberRequest(OrganizationRequest request, Guid listId, Guid memberId)
+        {
+            var removeRequest = request as RemoveMemberListRequest;
+            return removeRequest != null &&
+                removeRequest.ListId == listId &&
+                removeRequest.EntityId == memberId;
         }
     }
 }
diff --git a/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs b/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
index 4f7ffe4c..fb8e0b27 100644
--- a/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
+++ b/ToolUtility.Tests/QueryOperations/PresentRecordQueryServiceTests.cs
@@ -34,7 +34,9 @@ public sealed class PresentRecordQueryServiceTests
             "new_app_named",
             "new_contact_family_leader_list",
             "new_contact_race_leager_list",
-            "new_contact_list_arealeader"
+            "new_contact_list_arealeader",
+            "new_happy_start_date",
+            "new_happy_end_date"
         });
         capturedQuery.PageInfo.Should().NotBeNull();
         capturedQuery.PageInfo.PageNumber.Should().Be(1);
diff --git a/ToolUtility.Tests/ToolUtility.Tests.csproj b/ToolUtility.Tests/ToolUtility.Tests.csproj
index 8fd5a0dc..3257bb55 100644
--- a/ToolUtility.Tests/ToolUtility.Tests.csproj
+++ b/ToolUtility.Tests/ToolUtility.Tests.csproj
@@ -1,7 +1,7 @@
 <Project Sdk="Microsoft.NET.Sdk">
 
   <PropertyGroup>
-    <TargetFramework>net8.0</TargetFramework>
+    <TargetFramework>net10.0</TargetFramework>
     <ImplicitUsings>enable</ImplicitUsings>
     <Nullable>enable</Nullable>
     <IsPackable>false</IsPackable>
@@ -9,21 +9,21 @@
   </PropertyGroup>
 
   <ItemGroup>
-    <!-- ���ծج[ -->
+    <!-- Test framework -->
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
     <PackageReference Include="xunit" Version="2.6.6" />
     <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
     </PackageReference>
-    
-    <!-- Mock �ج[ -->
+
+    <!-- Mock framework -->
     <PackageReference Include="Moq" Version="4.20.70" />
-    
-    <!-- �_���w -->
+
+    <!-- Assertions -->
     <PackageReference Include="FluentAssertions" Version="6.12.0" />
-    
-    <!-- �{���X�л\�v -->
+
+    <!-- Code coverage -->
     <PackageReference Include="coverlet.collector" Version="6.0.0">
       <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
       <PrivateAssets>all</PrivateAssets>
@@ -35,14 +35,14 @@
   </ItemGroup>
 
   <ItemGroup>
-    <!-- �M�װѦ� -->
+    <!-- Project reference -->
     <ProjectReference Include="..\ToolUtility\ToolUtility.csproj" />
   </ItemGroup>
 
   <ItemGroup>
-    <!-- CRM SDK �M��]�Ω���ա^ -->
+    <!-- CRM SDK packages for tests -->
     <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.56" />
-    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
+    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
   </ItemGroup>
 
 </Project>
diff --git a/ToolUtility/QueryOperations/PresentRecordQueryService.cs b/ToolUtility/QueryOperations/PresentRecordQueryService.cs
index d81714a7..989246ee 100644
--- a/ToolUtility/QueryOperations/PresentRecordQueryService.cs
+++ b/ToolUtility/QueryOperations/PresentRecordQueryService.cs
@@ -291,7 +291,27 @@ namespace ToolUtilityNameSpace.QueryOperations
                 var query = new QueryExpression
                 {
                     EntityName = "list",
-                    ColumnSet = new ColumnSet(true)
+                    ColumnSet = new ColumnSet(
+                        "listid",
+                        "listname",
+                        "purpose",
+                        "new_app_named",
+                        "new_contact_family_leader_list",
+                        "new_contact_race_leager_list",
+                        "new_contact_list_arealeader",
+                        "new_contact_list_vice_family_leader",
+                        "new_contact_co_race_leager_list",
+                        "new_contact_list_co_arealeader",
+                        "new_familyhead_list",
+                        "new_happy_start_date",
+                        "new_happy_end_date",
+                        "statuscode",
+                        "statecode"),
+                    PageInfo = new PagingInfo
+                    {
+                        Count = 5000,
+                        PageNumber = 1
+                    }
                 };
 
                 var filter = new FilterExpression(LogicalOperator.And);
diff --git a/ToolUtility/Utilities/StringUtility.cs b/ToolUtility/Utilities/StringUtility.cs
index d200513a..daf1ce9d 100644
--- a/ToolUtility/Utilities/StringUtility.cs
+++ b/ToolUtility/Utilities/StringUtility.cs
@@ -35,7 +35,7 @@ namespace ToolUtilityNameSpace.Utilities
             int lastIndexEnglish = stringToProcess.LastIndexOf(',');
             int lastIndex = Math.Max(lastIndexChinese, lastIndexEnglish);
 
-            if (lastIndex > 0)
+            if (lastIndex >= 0)
             {
                 stringToProcess = stringToProcess.Substring(0, lastIndex);
             }

# Untracked code file diff
diff --git "a/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs" "b/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs"
new file mode 100644
index 00000000..5e00d612
--- /dev/null
+++ "b/ToolUtility.Tests\\TestHelpers\\MockOrganizationServiceFactory.cs"
@@ -0,0 +1,57 @@
+using System;
+using Microsoft.Xrm.Sdk;
+using Microsoft.Xrm.Sdk.Query;
+using Moq;
+
+namespace ToolUtility.Tests.TestHelpers
+{
+    public static class MockOrganizationServiceFactory
+    {
+        public static Mock<IOrganizationService> CreateMock()
+        {
+            var mock = new Mock<IOrganizationService>();
+
+            mock.Setup(x => x.Retrieve(
+                It.IsAny<string>(),
+                It.IsAny<Guid>(),
+                It.IsAny<ColumnSet>()))
+                .Returns((Entity)null!);
+
+            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
+                .Returns(new EntityCollection());
+
+            mock.Setup(x => x.Create(It.IsAny<Entity>()))
+                .Returns(Guid.NewGuid());
+
+            mock.Setup(x => x.Update(It.IsAny<Entity>()));
+            mock.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<Guid>()));
+            mock.Setup(x => x.Execute(It.IsAny<OrganizationRequest>()))
+                .Returns(new OrganizationResponse());
+
+            return mock;
+        }
+
+        public static Mock<IOrganizationService> CreateMockWithEntity(Entity entity)
+        {
+            var mock = CreateMock();
+
+            mock.Setup(x => x.Retrieve(
+                entity.LogicalName,
+                entity.Id,
+                It.IsAny<ColumnSet>()))
+                .Returns(entity);
+
+            return mock;
+        }
+
+        public static Mock<IOrganizationService> CreateMockWithCollection(EntityCollection collection)
+        {
+            var mock = CreateMock();
+
+            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
+                .Returns(collection);
+
+            return mock;
+        }
+    }
+}
``

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