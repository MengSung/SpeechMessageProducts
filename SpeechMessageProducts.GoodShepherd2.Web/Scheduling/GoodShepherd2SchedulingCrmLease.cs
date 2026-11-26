/*
檔案整體說明
目的與定位：F4產品排程介接的短期CRM租約邊界，供啟動組織驗證與每次工作重驗共用，沿用F6的fault及確定性歸還模式。
主要功能與流程：從固定產品manager借單次lease，先執行WhoAmI並核對完整非空身分與部署組織，再於相同租約執行借入委派。候選只在操作與歸還都成功後返回；不把URL或caller提供的GUID當成權威證明。
使用邊界與維護：manager及pool由既有Host唯一擁有，本檔只歸還lease，不關閉manager。同步SDK不支援中途取消，故在呼叫前後檢查caller token並完整等待當次SDK返回；取消或任何不明故障先MarkFaulted再Dispose。原始例外保留堆疊，額外清理故障交既有共用告警。租約、SDK回應及委派只存活於當次同步呼叫，不建立Task.Run、timer、授權cache或跨租戶資料；傳輸逾時沿既有manager設定，不能以提早返回冒稱排空。
*/
using System.Runtime.ExceptionServices;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Scheduling;

/// <summary>固定組織的操作區域租約邊界；不持有全域連線、身分或可變候選。</summary>
internal static class GoodShepherd2SchedulingCrmLease
{
    /// <summary>在同一租約驗實際組織後執行委派，完成fault／release才發布純值結果。</summary>
    /// <typeparam name="T">不含SDK或資源參照的操作結果；目前呼叫端只使用bool。</typeparam>
    /// <param name="manager">Host已按固定GoodShepherd2產品建立的管理器，只借用不接管。</param>
    /// <param name="organizationId">部署預期組織，必須與WhoAmI實際組織相等。</param>
    /// <param name="operation">只在本次lease內執行，不得保留service或啟動逃離此範圍的工作。</param>
    /// <param name="ct">原呼叫端取消；同步SDK返回後才會傳遞取消。</param>
    /// <returns>完整操作與歸還成功後的純值候選。</returns>
    internal static T Execute<T>(IDataverseConnectionManager manager, Guid organizationId,
        Func<IOrganizationService, T> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IClientLease? lease = null;
        Exception? primary = null;
        T candidate = default!;
        try
        {
            lease = manager.Acquire(ct);
            ct.ThrowIfCancellationRequested();
            var service = lease.Service;
            if (service.Execute(new WhoAmIRequest()) is not WhoAmIResponse identity ||
                identity.OrganizationId == Guid.Empty || identity.UserId == Guid.Empty ||
                identity.BusinessUnitId == Guid.Empty || identity.OrganizationId != organizationId)
                throw new InvalidOperationException("Scheduling CRM organization identity is invalid.");
            ct.ThrowIfCancellationRequested();
            candidate = operation(service);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            primary = exception;
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception cleanup)
                {
                    ExceptionReporting.Report(cleanup, "GoodShepherd2.Scheduling.FaultLease", CancellationToken.None);
                }
            }
        }
        finally
        {
            if (lease is not null)
            {
                try { lease.Dispose(); }
                catch (Exception cleanup)
                {
                    if (primary is null) primary = cleanup;
                    else ExceptionReporting.Report(cleanup, "GoodShepherd2.Scheduling.ReleaseLease", CancellationToken.None);
                }
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return candidate;
    }
}
