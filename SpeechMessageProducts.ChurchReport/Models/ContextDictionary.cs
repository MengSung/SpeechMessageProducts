/*
 * 檔案整體說明
 *
 * 目的與定位：管理目前 HTTP request 的 InMemoryDataContextSmallGroup。 舊版以程序級 static dictionary 按 Session Id 保存 context，並以
 * static Timer 清理。 那個設計會把 scoped adapter/workflow 及可變使用者資料延長到 request 之外；當 DI scope 結束後，字典仍可能重用已 Dispose 的物件，
 * 亦可能在同一 Session 重新登入另一個帳號時 讀到前一個帳號的 ListManager。這是 Session/Resource Leakage，不可接受。 現在只把 context 放入
 * HttpContext.Items。Items 的 owner 是目前 request， request 結束即與其 scoped 相依項一併失去唯一持有者；因此不需要 Timer、背景工作或 行程級集合，也不會跨
 * request、使用者、租戶或 profile 重用可變狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 ContextDictionary。
 * - 在目前 request 的 Items 中保存 context 的私有鍵；不含使用者資料。
 * - 向後相容的靜態屬性（已標記為過時）。 只回傳目前 request 的 context 快照；沒有 current request 時回傳空字典。 不再建立程序級集合，避免把 scoped 使用者狀態留在 static
 * root。
 * - 取得或建立 InMemoryDataContextSmallGroup
 * - 手動移除目前 request 的 context。request 結束時 Items 會自動清空，因此不需要 Dispose static 資源。
 * 主要操作入口包含 GetInMemoryDataContextSmallGroup、Remove。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 */
using ChurchReport.Tools;
using ChurchReport.Payments;
using LineMessagingProcessor.Workflows;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Models
{
    /// <summary>
    /// 管理目前 HTTP request 的 InMemoryDataContextSmallGroup。
    ///
    /// <para>
    /// 舊版以程序級 static dictionary 按 Session Id 保存 context，並以 static Timer 清理。
    /// 那個設計會把 scoped adapter/workflow 及可變使用者資料延長到 request 之外；當 DI scope
    /// 結束後，字典仍可能重用已 Dispose 的物件，亦可能在同一 Session 重新登入另一個帳號時
    /// 讀到前一個帳號的 ListManager。這是 Session/Resource Leakage，不可接受。
    /// </para>
    /// <para>
    /// 現在只把 context 放入 <see cref="HttpContext.Items"/>。Items 的 owner 是目前 request，
    /// request 結束即與其 scoped 相依項一併失去唯一持有者；因此不需要 Timer、背景工作或
    /// 行程級集合，也不會跨 request、使用者、租戶或 profile 重用可變狀態。
    /// </para>
    /// </summary>
    public static class ContextDictionary
    {
        /// <summary>在目前 request 的 Items 中保存 context 的私有鍵；不含使用者資料。</summary>
        private static readonly object ContextItemKey = new object();

        /// <summary>
        /// 向後相容的靜態屬性（已標記為過時）。
        /// 只回傳目前 request 的 context 快照；沒有 current request 時回傳空字典。
        /// 不再建立程序級集合，避免把 scoped 使用者狀態留在 static root。
        /// </summary>
        [Obsolete("請使用 GetInMemoryDataContextSmallGroup 方法，此屬性將在未來版本移除")]
        public static Dictionary<String, InMemoryDataContextSmallGroup> StaticContextDictionary
        {
            get
            {
                return new Dictionary<String, InMemoryDataContextSmallGroup>();
            }
        }

        /// <summary>
        /// 取得或建立 InMemoryDataContextSmallGroup
        /// </summary>
        public static InMemoryDataContextSmallGroup GetInMemoryDataContextSmallGroup(
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache memoryCache,
            IToolUtilityProvider toolUtilityProvider)
        {
            try
            {
                var httpContext = httpContextAccessor?.HttpContext;
                var session = httpContext?.Session;
                if (session == null)
                {
                    throw new InvalidOperationException("Session is not available");
                }

                // Items 只存活於目前 request，確保 scoped 相依項不會被 static root 延長生命週期。
                // 同一 request 內重複呼叫仍可重用 context，保留原本的 request 內快取效益。
                if (httpContext.Items.TryGetValue(ContextItemKey, out var existing)
                    && existing is InMemoryDataContextSmallGroup existingContext)
                {
                    return existingContext;
                }

                // 從 ASP.NET Core DI 取得中性的奉獻付款建單 adapter。
                // ContextDictionary 只負責把每個 session 的 manager 串起來，不應直接依賴 QPay 命名的相容 adapter。
                var donationPaymentCreateGatewayAdapter =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(IDonationPaymentCreateGatewayAdapter))
                        as IDonationPaymentCreateGatewayAdapter;
                var lineNotificationWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineNotificationWorkflow))
                        as ILineNotificationWorkflow;
                var lineReplyWorkflow =
                    httpContextAccessor.HttpContext?.RequestServices?.GetService(typeof(ILineReplyWorkflow))
                        as ILineReplyWorkflow;

                var context = new InMemoryDataContextSmallGroup(
                    httpContextAccessor,
                    memoryCache,
                    toolUtilityProvider,
                    donationPaymentCreateGatewayAdapter,
                    lineNotificationWorkflow,
                    lineReplyWorkflow);
                httpContext.Items[ContextItemKey] = context;
                return context;
            }
            catch (System.Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[ContextDictionary] Error: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// 手動移除目前 request 的 context。request 結束時 Items 會自動清空，因此不需要 Dispose static 資源。
        /// </summary>
        public static void Remove(string sessionId)
        {
            // sessionId 僅作為相容簽章保留；不可用呼叫端字串決定要清除另一個 session 的資料。
            // ContextDictionary 不再擁有程序級資料，因此沒有需要由 sessionId 尋找並刪除的集合。
            // request 結束時 Items 會由 ASP.NET Core 清空；此相容方法刻意為 no-op，避免
            // 引入 AsyncLocal 或其他 static root 重新延長 request 狀態生命週期。
        }

        /// <summary>
        /// 取得目前的項目數
        /// </summary>
        public static int Count => 0;
    }
}
