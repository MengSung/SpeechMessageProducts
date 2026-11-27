/*
 * 檔案整體說明
 * 目的與定位：F8授權及稽核共用的同步SDK生命週期邊界，產品組合根注入固定產品、租約工廠與唯一安全診斷owner。
 * 主要功能與流程：每操作取得短命租約，執行候選組裝後確認caller取消，SDK確實返回後才fault及Dispose；錯誤保留真實Exception並由共用入口去重告警。不使用Task.Run或跨請求Entity快取。
 * 使用邊界與維護：工廠必須事先綁定正確組織與有界傳輸逾時，不能依Actor選路；本引擎不擁有pool或diagnostics。操作最多持有一個租約，所有失敗都嘗試歸還，清理故障不遮蔽主要例外。Contact與Account僅讀取必要欄位，確認active及choice租戶旗標；同步SDK未完成時取消不能提前釋放。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Access;

/// <summary>一個不可變的依賴封裝，沒有主體、查詢候選或租約的跨操作保留。</summary>
internal sealed class AccessDataverseExecutor
{
    private readonly Func<CancellationToken, IAccessDataverseLease> _acquire;
    private readonly ExceptionDiagnostics _diagnostics;

    /// <summary>借用工廠與診斷，不移交它們的Host生命週期；產品固定且不能由請求改動。</summary>
    internal AccessDataverseExecutor(Func<CancellationToken, IAccessDataverseLease> acquire, ExceptionDiagnostics diagnostics, string product)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!AccessValidation.ValidProduct(product)) throw new ArgumentException("Invalid access product.", nameof(product));
        _acquire = acquire;
        _diagnostics = diagnostics;
        Product = product;
    }

    /// <summary>組合根固定產品，只供比對，永遠不以actor路由client。</summary>
    internal string Product { get; }

    /// <summary>執行一次SDK工作並確定清理；保留原失敗物件，內部逾時不當作caller正常取消。</summary>
    internal T Execute<T>(Func<IOrganizationService, T> action, string operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IAccessDataverseLease? lease = null;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        T? result = default;
        try
        {
            lease = _acquire(ct) ?? throw new InvalidOperationException("Access lease unavailable.");
            ct.ThrowIfCancellationRequested();
            result = action(lease.Service);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception cleanup) { ReportSafely(cleanup, "Access.MarkFaulted", ct); }
            }
            ReportSafely(ex, operation, ct);
        }
        if (lease is not null)
        {
            try { lease.Dispose(); }
            catch (Exception cleanup)
            {
                ReportSafely(cleanup, "Access.Dispose", ct);
                failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup);
            }
        }
        failure?.Throw();
        return result!;
    }

    /// <summary>通知管道的次生錯誤不能跳過租約釋放或取代原失敗；Host owner自行提供停止期固定診斷。</summary>
    private void ReportSafely(Exception exception, string operation, CancellationToken ct)
    {
        try { _diagnostics.Report(exception, operation, ct); }
        catch { /* 診斷故障不得逃逸SDK生命週期邊界；原錯誤與租約清理由本owner保留。 */ }
    }

    /// <summary>重新確認Contact與Account的正式ID、實體種類、active與租戶choice；不把GUID或caller產品當授權。</summary>
    internal bool ValidateActor(IOrganizationService service, Actor actor, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (actor.Product != Product || actor.ContactId == Guid.Empty || actor.TenantId == Guid.Empty) return false;
        Entity contact = service.Retrieve("contact", actor.ContactId, new ColumnSet("parentcustomerid", "statecode"));
        ct.ThrowIfCancellationRequested();
        if (contact is null || contact.LogicalName != "contact" || contact.Id != actor.ContactId ||
            !IsActive(contact) || !RefMatches(contact, "parentcustomerid", "account", actor.TenantId)) return false;
        Entity tenant = service.Retrieve("account", actor.TenantId, new ColumnSet("statecode", "new_istenant"));
        ct.ThrowIfCancellationRequested();
        if (tenant is null || tenant.LogicalName != "account" || tenant.Id != actor.TenantId || !IsActive(tenant)) return false;
        int? tenantFlag = tenant.GetAttributeValue<OptionSetValue>("new_istenant")?.Value;
        return tenantFlag switch
        {
            null or 100000000 => false,
            100000001 => true,
            _ => throw new InvalidOperationException("Invalid tenant flag metadata.")
        };
    }

    /// <summary>statecode是必備metadata，缺損或未知不能冒充一般停用；0有效、1停用，其餘告警故障。</summary>
    internal static bool IsActive(Entity entity)
    {
        if (!entity.Attributes.TryGetValue("statecode", out object? value) || value is not OptionSetValue state || state.Value is not (0 or 1))
            throw new InvalidOperationException("Invalid state metadata.");
        return state.Value == 0;
    }

    /// <summary>lookup必須有確切種類與主鍵，formatted name或空ID都不可信。</summary>
    internal static bool RefMatches(Entity entity, string field, string logicalName, Guid id) =>
        entity.Attributes.TryGetValue(field, out object? value) && value is EntityReference reference && reference.LogicalName == logicalName && reference.Id == id && id != Guid.Empty;
}
