using System;
using System.Diagnostics;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 供 IOrganizationService 代理共用的 crm.op 量測工具。
/// </summary>
/// <remarks>
/// <para>
/// 此型別是 <see cref="GatewayOrganizationService"/> 與 <see cref="AmbientGatewayOrganizationService"/>
/// 記錄 CRM 操作的唯一路徑，因此兩個代理的稽核輸出必然一致，不會因為某一邊漏改而產生盲區。
/// </para>
/// <para>
/// <b>隱私邊界</b>：只輸出 entity logical name、SDK 訊息名稱、耗時、筆數與成敗。資料列 GUID、
/// 欄位值、ColumnSet、查詢條件與 FetchXML 本文一律不取用，因此稽核檔不會成為資料外洩管道。
/// </para>
/// <para>
/// 關閉 Trace 時完全不進入量測路徑，直接執行原委派，維持零額外配置的熱路徑成本。
/// </para>
/// </remarks>
internal static class CrmOperationTrace
{
    /// <summary>執行並量測有回傳值的 CRM 操作。</summary>
    /// <typeparam name="T">CRM 操作的回傳型別。</typeparam>
    /// <param name="operation">操作種類名稱。</param>
    /// <param name="entity">entity logical name；未知時傳入空字串。</param>
    /// <param name="work">實際的 CRM 呼叫。</param>
    /// <param name="countSelector">自結果取出筆數的選擇器；不適用時為 <see langword="null"/>。</param>
    /// <returns>原 CRM 操作的回傳值，內容不受量測影響。</returns>
    internal static T Measure<T>(string operation, string entity, Func<T> work, Func<T, int> countSelector = null)
    {
        var trace = DataverseTrace.Current;
        if (trace?.Enabled != true)
            return work();

        var startedTimestamp = Stopwatch.GetTimestamp();
        var ok = false;
        var result = default(T);
        try
        {
            result = work();
            ok = true;
            return result;
        }
        finally
        {
            // 失敗也必須留下紀錄：慢而後失敗的呼叫正是最需要被看見的那一種。
            var count = ok && countSelector != null ? countSelector(result) : -1;
            trace.CrmOperation(operation, entity, ElapsedMilliseconds(startedTimestamp), ok, count);
        }
    }

    /// <summary>執行並量測沒有回傳值的 CRM 操作。</summary>
    /// <param name="operation">操作種類名稱。</param>
    /// <param name="entity">entity logical name；未知時傳入空字串。</param>
    /// <param name="work">實際的 CRM 呼叫。</param>
    internal static void Measure(string operation, string entity, Action work)
        => Measure<object>(operation, entity, () => { work(); return null; });

    /// <summary>
    /// 取出查詢的 entity logical name。
    /// </summary>
    /// <remarks>
    /// <see cref="FetchExpression"/> 只回傳固定標記：其 XML 內嵌欄位、條件與資料列識別，
    /// 解析並輸出等同把查詢內容寫進稽核檔，因此刻意不做。
    /// </remarks>
    /// <param name="query">要判定的查詢。</param>
    /// <returns>entity logical name、固定標記，或無法判定時的空字串。</returns>
    internal static string DescribeQuery(QueryBase query) => query switch
    {
        QueryExpression expression => expression.EntityName ?? string.Empty,
        QueryByAttribute byAttribute => byAttribute.EntityName ?? string.Empty,
        FetchExpression => "(fetchxml)",
        null => string.Empty,
        _ => string.Empty
    };

    /// <summary>
    /// 取出組織要求的訊息名稱作為 entity 欄位值。
    /// </summary>
    /// <remarks>
    /// 只讀取 <see cref="OrganizationRequest.RequestName"/>；<c>Parameters</c> 內含實際資料，絕不取用。
    /// </remarks>
    /// <param name="request">要判定的組織要求。</param>
    /// <returns>SDK 訊息名稱，未知時為空字串。</returns>
    internal static string DescribeRequest(OrganizationRequest request)
        => request?.RequestName ?? string.Empty;

    /// <summary>取得自指定時間戳起算的毫秒數，不會回傳負值。</summary>
    private static long ElapsedMilliseconds(long startedTimestamp)
        => Math.Max(0, (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
}
