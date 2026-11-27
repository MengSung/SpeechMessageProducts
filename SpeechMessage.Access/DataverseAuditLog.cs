/*
 * 檔案整體說明
 * 目的與定位：F8跨產品修改紀錄的Dataverse寫入出口，以當次AuditEntry建立獨立候選，不保存秘密或跨請求可變Entity。
 * 主要功能與流程：先驗有界欄名、正式主鍵及伺服器權威EventId；遮罩敏感內容後重新驗證有效Contact及租戶，將事件時刻正規化為UTC整秒，再受tenant＋event資料庫唯一鍵保護執行Create。只有明確完整性衝突才讀回最多兩列，按相同整秒精度核對唯一正式ID及完整已遮罩內容；不同事件不按內容合併，既有證據永不覆寫。
 * 使用邊界與維護：本服務不執行業務授權、共同交易或自動重送；業務owner須先授權並持久保存每個欄位修改事實的事件ID及原始時刻。為符合D365 9.1既有整秒讀回先例，`new_at` 向下量化為UTC整秒；本地離線契約不代表F8欄位已實機確認，實機精度仍待Live3P。正式索引須部署且Active才有跨程序保證。Host擁有diagnostics及pool；executor唯一歸還租約，SDK返回前不提前取消或Dispose。取消或不明傳輸結果不得換事件ID盲重送；真正失敗交安全共用入口，不輸出原值或例外Message。資料只在當次呼叫保留。
 */
using System.Text;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Access;

/// <summary>固定組織依賴、無跨請求資料保留的修改紀錄服務；所有資料最多只在當次呼叫存活。</summary>
public sealed class DataverseAuditLog : IAuditLog
{
    private readonly AccessDataverseExecutor _executor;

    /// <summary>借用固定產品的工廠與安全診斷；不取得租約或接管它們的Host生命週期。</summary>
    public DataverseAuditLog(Func<CancellationToken, IAccessDataverseLease> acquire, ExceptionDiagnostics diagnostics, string product) =>
        _executor = new AccessDataverseExecutor(acquire, diagnostics, product);

    /// <summary>先原子Create，將事件時間固定為UTC整秒；僅在明確唯一性衝突時以相同精度核對既有列，不改寫證據或盲重試，並在清理後才完成操作。</summary>
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Validate(entry);
            bool sensitive = Sensitive(entry.Field);
            var candidate = new Entity("new_auditentry")
            {
                ["new_name"] = "Audit",
                ["new_eventid"] = entry.EventId.ToString("D"),
                ["new_entity"] = entry.Entity,
                ["new_recordid"] = entry.RecordId.ToString("D"),
                ["new_field"] = entry.Field,
                ["new_before"] = sensitive ? "***" : MaskValue(entry.Before),
                ["new_after"] = sensitive ? "***" : MaskValue(entry.After),
                ["new_actorcontactid"] = new EntityReference("contact", entry.ActorContactId),
                ["new_tenantid"] = new EntityReference("account", entry.TenantId),
                ["new_correlationid"] = entry.CorrelationId,
                ["new_at"] = NormalizeUtcToSecond(entry.At.UtcDateTime)
            };
            _executor.Execute(service =>
            {
                var actor = new Actor(entry.ActorContactId, entry.TenantId, _executor.Product);
                if (!_executor.ValidateActor(service, actor, ct)) throw new UnauthorizedAccessException("Audit actor is not valid.");
                ct.ThrowIfCancellationRequested();
                Guid id;
                try { id = service.Create(candidate); }
                catch (FaultException<OrganizationServiceFault> failure) when
                    (failure.Detail?.ErrorCode is unchecked((int)0x80040237) or unchecked((int)0x80060892))
                {
                    // 官方DuplicateRecord／DuplicateRecordEntityKey才可查回；逾時等不明結果保留原錯，不推定已寫入。
                    ct.ThrowIfCancellationRequested();
                    id = ReadExistingEvent(service, candidate, entry, ct);
                }
                if (id == Guid.Empty) throw new InvalidOperationException("Audit identity is missing.");
                return id;
            }, "Access.AuditWrite", ct);
            return Task.CompletedTask;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Task.FromCanceled(ct); }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    /// <summary>只驗格式與容量，格式合法不代表已獲業務授權；錯誤不回顯任何候選內容。</summary>
    private static void Validate(AuditEntry entry)
    {
        if (entry is null || !AccessValidation.ValidIdentifier(entry.Entity, 128) ||
            !ValidField(entry.Field) || !AccessValidation.ValidIdentifier(entry.CorrelationId, 128) ||
            entry.RecordId == Guid.Empty || entry.EventId == Guid.Empty || entry.ActorContactId == Guid.Empty || entry.TenantId == Guid.Empty ||
            entry.Before?.Length > 4000 || entry.After?.Length > 4000 || entry.At.UtcDateTime.Year < 1753)
            throw new ArgumentException("Invalid audit entry.", nameof(entry));
    }

    /// <summary>
    /// 把UTC或Dataverse以Unspecified回傳、但ticks仍代表UTC的日期時間向下量化至整秒。
    /// 統一寫入與衝突讀回的可持久化精度，避免亞秒資訊在資料庫往返中消失後把同一EventId誤判為內容衝突；不做本地時區轉換，也不向上進位。
    /// 呼叫端須先拒絕Local kind；此純值轉換不保存狀態或取得外部資源。
    /// </summary>
    private static DateTime NormalizeUtcToSecond(DateTime value)
    {
        long ticks = value.Ticks - value.Ticks % TimeSpan.TicksPerSecond;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// 只有明確索引衝突才按完整租戶／事件鍵讀回；零筆、多筆、更多資料、空正式ID或內容衝突均拒絕。
    /// 事件時間與寫入相同，將候選及讀回值用NormalizeUtcToSecond比較，接受SDK將UTC讀回標為Unspecified但拒絕Local kind。投影只讀本次候選的十一欄，既有列不交給caller也不Update；任何SDK失敗仍由同一executor排空並告警。
    /// </summary>
    private static Guid ReadExistingEvent(IOrganizationService service, Entity candidate, AuditEntry entry, CancellationToken ct)
    {
        var query = new QueryExpression("new_auditentry")
        {
            TopCount = 2,
            ColumnSet = new ColumnSet(candidate.Attributes.Keys.ToArray())
        };
        query.Criteria.AddCondition("new_tenantid", ConditionOperator.Equal, entry.TenantId);
        query.Criteria.AddCondition("new_eventid", ConditionOperator.Equal, entry.EventId.ToString("D"));
        EntityCollection rows = service.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (rows is null || rows.MoreRecords || rows.Entities.Count != 1)
            throw new InvalidOperationException("Audit event identity conflict.");
        Entity row = rows.Entities[0];
        if (row is null || row.LogicalName != "new_auditentry" || row.Id == Guid.Empty)
            throw new InvalidOperationException("Audit event identity conflict.");
        foreach (var expected in candidate.Attributes)
        {
            row.Attributes.TryGetValue(expected.Key, out object? actual);
            bool equal = expected.Value switch
            {
                EntityReference reference => actual is EntityReference other && other.LogicalName == reference.LogicalName && other.Id == reference.Id,
                DateTime at => actual is DateTime other && other.Kind != DateTimeKind.Local && NormalizeUtcToSecond(other).Ticks == NormalizeUtcToSecond(at).Ticks,
                string text => actual is string other && string.Equals(text, other, StringComparison.Ordinal),
                null => actual is null,
                _ => false
            };
            if (!equal) throw new InvalidOperationException("Audit event content conflict.");
        }
        return row.Id;
    }

    /// <summary>允許多語欄位識別與固定分隔符；控制字元與任意本文不可成為欄位名。</summary>
    private static bool ValidField(string? field) => !string.IsNullOrWhiteSpace(field) && field.Length <= 128 &&
        field.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':');

    /// <summary>兼容全形及分隔變形的保守秘密分類；不使用顯示名稱去推導授權。</summary>
    private static bool Sensitive(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormKC);
        string compact = new(normalized.Where(char.IsLetterOrDigit).ToArray());
        return compact.Contains("token", StringComparison.OrdinalIgnoreCase) || compact.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            compact.Contains("password", StringComparison.OrdinalIgnoreCase) || compact.Contains("passwd", StringComparison.OrdinalIgnoreCase) ||
            compact.Contains("pwd", StringComparison.OrdinalIgnoreCase) || compact.Contains("card", StringComparison.OrdinalIgnoreCase) ||
            compact.Contains("bearer", StringComparison.OrdinalIgnoreCase) || compact.Contains("卡號", StringComparison.Ordinal);
    }

    /// <summary>空文字先正規化為null以穩定Dataverse讀回；不解析任意結構，保守遮罩避免嵌入秘密進入稽核表。</summary>
    private static string? MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string normalized = value.Normalize(NormalizationForm.FormKC);
        if (Sensitive(normalized) || normalized.IndexOfAny(['{', '}', '[', ']', '=', '%', '\\']) >= 0 ||
            normalized.Contains("://", StringComparison.Ordinal) || normalized.Count(c => c == '.') >= 2 || normalized.Any(char.IsControl)) return "***";
        int digits = 0, opaque = 0;
        foreach (char c in normalized)
        {
            digits = char.IsDigit(c) ? digits + 1 : c is '-' or ' ' ? digits : 0;
            opaque = char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '+' or '/' ? opaque + 1 : 0;
            if (digits >= 13 || opaque >= 32) return "***";
        }
        return value;
    }
}
