/*
 * 檔案整體說明
 *
 * 目的與定位：把 ToolUtility.Core 的雙目標 D365 OutboundTaskRecord／CAS store
 * 轉接成 SpeechMessage.Hosting 使用的 net10 IOutboundWorkStore。Hosting 保持獨立
 * 基礎層，不反向參考 ToolUtility；本檔是唯一的 net10 組合轉接點。
 *
 * 主要功能與流程：建構時借用組合根已授權的 IOrganizationService，並可把 Hosting
 * 工作類型 allowlist 映射到 Core，使候選在 D365 查詢端受限；所有讀取、RowVersion
 * 認領及完成／重試回寫都委派給 D365OutboundTaskStore。回傳前將 SDK 脫離成
 * Hosting detached snapshot；輸入到 Core 前重新建立純值 record，讓類型、狀態、時間、
 * payload 與正式身分再次通過 Core 的 fail-closed 驗證。
 *
 * 使用邊界與維護：本檔不建立、不池化、不 Dispose caller 擁有的 SDK；D365 store
 * 只借用該參照，adapter Dispose 時僅解除內部借用。呼叫端必須在所有同步操作返回
 * 後才釋放 SDK。快照不保存 Session、tenant、credential 或可變 Entity；每筆工作
 * 的教會 scope 由 Hosting handler factory 重新驗證。allowlist 由 Core 建構時有界複製；
 * 未提供時維持原查詢。任何轉換錯誤或非版本衝突例外
 * 原樣交由 worker 的共用告警入口，不在此吞掉或自行重試。驗證使用離線 SDK 替身，
 * 不宣稱 D365 實機結果。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Outbound;
using CoreOutbound = ToolUtilityNameSpace.Outbound;

namespace ToolUtilityNameSpace.Outbound;

/// <summary>
/// net10 組合根唯一使用的 D365 → Hosting 對外待辦轉接器。
/// 只借用 caller 擁有的 SDK，將 Core 的 CAS 結果映射成 Hosting detached snapshot。
/// </summary>
public sealed class D365OutboundWorkStoreAdapter : IOutboundWorkStore, IDisposable
{
    private CoreOutbound.D365OutboundTaskStore? _inner;

    /// <summary>
    /// 建立固定已授權組織的轉接器；不從工作資料或 caller 路由推導組織權限。
    /// 選用類型清單會映射為 Core enum 並由 Core 立即複製及驗證，避免保留可變 caller
    /// 集合；null 不增加查詢限制，維持既有 adapter 行為。
    /// </summary>
    /// <param name="service">由組合根擁有且在本物件所有操作完成後才釋放的 SDK。</param>
    /// <param name="allowedTypes">選填的 Hosting 工作類型白名單；null 表示維持不限類型的舊行為。</param>
    public D365OutboundWorkStoreAdapter(
        IOrganizationService service,
        IEnumerable<OutboundWorkType>? allowedTypes = null)
    {
        _inner = new CoreOutbound.D365OutboundTaskStore(
            service ?? throw new ArgumentNullException(nameof(service)),
            allowedTypes?.Select(ToCore));
    }

    /// <summary>委派 Core 的有界候選查詢並發布完整 Hosting detached snapshot。</summary>
    public IReadOnlyList<OutboundWorkItem> ReadCandidates(
        DateTime utcNow,
        int maximum,
        CancellationToken cancellationToken)
    {
        var inner = Borrow();
        return inner.ReadCandidates(utcNow, maximum, cancellationToken)
            .Select(ToHosting)
            .ToArray();
    }

    /// <summary>以同一 RowVersion 委派 Core CAS 認領；競爭落敗仍回傳 null。</summary>
    public OutboundWorkItem? TryClaim(
        OutboundWorkItem candidate,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var result = Borrow().TryClaim(ToCore(candidate), utcNow, cancellationToken);
        return result is null ? null : ToHosting(result);
    }

    /// <summary>以同一 owner 版本委派 Core CAS 完成、重試或終止。</summary>
    public bool TrySettle(
        OutboundWorkItem claim,
        OutboundWorkStatus status,
        int attempts,
        DateTime utcNow,
        DateTime? nextAttemptOn,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return Borrow().TrySettle(
            ToCore(claim),
            ToCore(status),
            attempts,
            utcNow,
            nextAttemptOn,
            errorCode,
            cancellationToken);
    }

    /// <summary>
    /// 只清除借用的 store 參照；不釋放 caller 所有的 IOrganizationService。
    /// </summary>
    public void Dispose() => System.Threading.Interlocked.Exchange(ref _inner, null)?.Dispose();

    /// <summary>取得尚未 Dispose 的 Core store，避免關閉後重新進入 SDK。</summary>
    private CoreOutbound.D365OutboundTaskStore Borrow() =>
        System.Threading.Volatile.Read(ref _inner)
        ?? throw new ObjectDisposedException(nameof(D365OutboundWorkStoreAdapter));

    /// <summary>將已驗證 Core 純值映射成不持有 SDK 的 Hosting 純值。</summary>
    private static OutboundWorkItem ToHosting(CoreOutbound.OutboundTaskRecord value) =>
        new(
            value.Id,
            value.RowVersion,
            value.ChurchId,
            value.ContactId,
            ToHosting(value.Type),
            value.PayloadJson,
            value.IdempotencyKey,
            ToHosting(value.Status),
            value.Attempts,
            value.NextAttemptOn,
            value.ClaimedOn);

    /// <summary>將 Hosting 值映射回 Core；Core constructor 會再做完整欄位驗證。</summary>
    private static CoreOutbound.OutboundTaskRecord ToCore(OutboundWorkItem value) =>
        new(
            value.Id,
            value.RowVersion,
            value.ChurchId,
            value.ContactId,
            ToCore(value.Type),
            value.PayloadJson,
            value.IdempotencyKey,
            ToCore(value.Status),
            value.Attempts,
            value.NextAttemptOn,
            value.ClaimedOn);

    /// <summary>固定 enum 映射，未知值 fail closed。</summary>
    private static OutboundWorkType ToHosting(CoreOutbound.OutboundTaskType value) => value switch
    {
        CoreOutbound.OutboundTaskType.RichMenuGenerate => OutboundWorkType.RichMenuGenerate,
        CoreOutbound.OutboundTaskType.QrImageGenerate => OutboundWorkType.QrImageGenerate,
        CoreOutbound.OutboundTaskType.AllotQuery => OutboundWorkType.AllotQuery,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    /// <summary>固定 enum 映射，未知值 fail closed。</summary>
    private static CoreOutbound.OutboundTaskType ToCore(OutboundWorkType value) => value switch
    {
        OutboundWorkType.RichMenuGenerate => CoreOutbound.OutboundTaskType.RichMenuGenerate,
        OutboundWorkType.QrImageGenerate => CoreOutbound.OutboundTaskType.QrImageGenerate,
        OutboundWorkType.AllotQuery => CoreOutbound.OutboundTaskType.AllotQuery,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    /// <summary>固定狀態映射，不把平台 statuscode 混入自訂工作狀態。</summary>
    private static OutboundWorkStatus ToHosting(CoreOutbound.OutboundTaskStatus value) => value switch
    {
        CoreOutbound.OutboundTaskStatus.Pending => OutboundWorkStatus.Pending,
        CoreOutbound.OutboundTaskStatus.Processing => OutboundWorkStatus.Processing,
        CoreOutbound.OutboundTaskStatus.Completed => OutboundWorkStatus.Completed,
        CoreOutbound.OutboundTaskStatus.Failed => OutboundWorkStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    /// <summary>固定狀態映射，未知值不得進入 Core 回寫。</summary>
    private static CoreOutbound.OutboundTaskStatus ToCore(OutboundWorkStatus value) => value switch
    {
        OutboundWorkStatus.Pending => CoreOutbound.OutboundTaskStatus.Pending,
        OutboundWorkStatus.Processing => CoreOutbound.OutboundTaskStatus.Processing,
        OutboundWorkStatus.Completed => CoreOutbound.OutboundTaskStatus.Completed,
        OutboundWorkStatus.Failed => CoreOutbound.OutboundTaskStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
