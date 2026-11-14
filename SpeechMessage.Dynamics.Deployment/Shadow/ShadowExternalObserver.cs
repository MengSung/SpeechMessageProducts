/*
 * 檔案整體說明
 * 目的與定位：影子舊輪的外部既有記錄唯讀摘要，不攔截或回復遠端 Plugin 寫入。
 * 主要功能與流程：呼叫端先以受審盤點及正式 metadata 核對實體、教會 lookup 與
 * 主鍵，再提供權威測試教會 ID。owner 在種子建立前保存有界 ID／版本快照，
 * 舊輪完成後再查同範圍，僅回傳觸發前既有列發生版本變化或離開範圍的筆數。
 * 使用邊界與維護：每次查詢僅取主鍵、教會 lookup 與 versionnumber，最多十六表、
 * 每表六十四列；分頁、超量、缺版本、錯教會或重複身分皆拒絕，不採部分結果。
 * 不讀名稱或訊息，不保存SDK Entity、秘密或業務欄值。並行寫入也可能造成版本
 * 變化，摘要不能獨立證明因果，不宣稱看到了未盤點實體或新建列。
 * SDK及取消token由caller借入；同步SDK回應前不能強制中斷，前後皆核對取消。
 * 快照及候選僅活於單輪，Dispose確定清除ID／版本；無連線、timer、背景task、
 * static cache或跨租戶狀態。驗證使用獨占合成SDK，不能冒稱實機修改證據。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>受審且經正式metadata核對的外部既有列範圍；不接受任意查詢或原始業務值。</summary>
internal sealed record ShadowExternalScope(string Entity, string ChurchAttribute, string PrimaryIdAttribute);

/// <summary>只包含結構名與變動數量；沒有實際ID、版本或欄位值，不代表全組織寫入稽核。</summary>
internal sealed record ShadowExternalChange(string Entity, int ChangedExistingCount);

/// <summary>單輪唯讀觀察owner；Dispose只清本機快照，絕不修改既有記錄或處置借入SDK。</summary>
internal sealed class ShadowExternalObserver : IDisposable
{
    private IOrganizationService? service;
    private readonly Guid churchId;
    private readonly ShadowExternalScope[] scopes;
    private Dictionary<string, Dictionary<Guid, long>>? baseline;
    private bool compared;
    private bool disposed;

    /// <summary>建立單輪範圍，不進行I/O；caller負責來源盤點、metadata及教會身分授權。</summary>
    internal ShadowExternalObserver(IOrganizationService service, Guid churchId, IReadOnlyList<ShadowExternalScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(scopes);
        if (churchId == Guid.Empty || scopes.Count > 16 || scopes.Any(scope => scope is null ||
            !Schema(scope.Entity) || !Schema(scope.ChurchAttribute) || !Schema(scope.PrimaryIdAttribute) ||
            scope.ChurchAttribute == scope.PrimaryIdAttribute || scope.ChurchAttribute == "versionnumber" ||
            scope.PrimaryIdAttribute == "versionnumber") ||
            scopes.Select(scope => scope.Entity).Distinct(StringComparer.Ordinal).Count() != scopes.Count) throw Invalid();
        this.service = service;
        this.churchId = churchId;
        this.scopes = scopes.ToArray();
    }

    /// <summary>種子建立前讀取完整快照；失敗不發布部分候選，取消由caller判定。</summary>
    internal void Capture(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (baseline is not null || compared) throw new InvalidOperationException("External snapshot already captured.");
        baseline = Read(cancellationToken);
    }

    /// <summary>完成舊輪後計算受審範圍內的既有列變動；不含後來新建列或原始身分。</summary>
    internal IReadOnlyList<ShadowExternalChange> Compare(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (baseline is null || compared) throw new InvalidOperationException("External snapshot is unavailable.");
        var after = Read(cancellationToken);
        var result = new List<ShadowExternalChange>(scopes.Length);
        foreach (var scope in scopes)
        {
            var current = after[scope.Entity];
            var changed = baseline[scope.Entity].Count(row => !current.TryGetValue(row.Key, out var version) || version != row.Value);
            result.Add(new ShadowExternalChange(scope.Entity, changed));
        }
        cancellationToken.ThrowIfCancellationRequested();
        compared = true;
        ClearBaseline();
        return result.AsReadOnly();
    }

    /// <summary>
    /// 僅以權威教會lookup查詢三個metadata欄，完整讀取後才返回ID／版本候選。
    /// SDK同步回應無法由本層中斷；取消、分頁或錯範圍一律失敗，不發布部分表。
    /// </summary>
    private Dictionary<string, Dictionary<Guid, long>> Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = new Dictionary<string, Dictionary<Guid, long>>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new QueryExpression(scope.Entity)
            {
                ColumnSet = new ColumnSet(scope.PrimaryIdAttribute, scope.ChurchAttribute, "versionnumber"), TopCount = 65
            };
            query.Criteria.AddCondition(scope.ChurchAttribute, ConditionOperator.Equal, churchId);
            var response = service!.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (response is null || response.MoreRecords || response.Entities.Count > 64) throw Invalid();
            var rows = new Dictionary<Guid, long>();
            foreach (var row in response.Entities)
            {
                if (row is null || row.LogicalName != scope.Entity || row.Id == Guid.Empty ||
                    !row.Attributes.TryGetValue(scope.PrimaryIdAttribute, out var primary) ||
                    primary is not Guid primaryId || primaryId != row.Id ||
                    row.GetAttributeValue<EntityReference>(scope.ChurchAttribute) is not { LogicalName: "account" } church ||
                    church.Id != churchId || !row.Attributes.TryGetValue("versionnumber", out var value) ||
                    value is not long version || version < 0 || !rows.TryAdd(row.Id, version)) throw Invalid();
            }
            candidate.Add(scope.Entity, rows);
        }
        return candidate;
    }

    /// <summary>只接受小寫schema識別字，不允許輸入路徑、控制字元或查詢片段。</summary>
    private static bool Schema(string value) => value is { Length: > 0 and <= 128 } &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    /// <summary>清除所有保留ID與版本；候選不含SDK Entity或欄位值參照。</summary>
    private void ClearBaseline()
    {
        if (baseline is null) return;
        foreach (var rows in baseline.Values) rows.Clear();
        baseline.Clear();
        baseline = null;
    }

    /// <summary>安全固定訊息，不包含組織、ID、欄位值或原始SDK回應。</summary>
    private static InvalidDataException Invalid() => new("External observation precondition failed.");

    /// <summary>清除本機快照，不寫遠端、不釋放借入服務；釋放後禁止再次查詢。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClearBaseline();
        Array.Clear(scopes);
        service = null;
    }
}
