/*
 * 檔案整體說明
 * 目的與定位：F8跨產品修改紀錄的Dataverse寫入出口，以當次AuditEntry建立獨立候選，不保存秘密或跨請求可變Entity。
 * 主要功能與流程：先驗有界欄名、主鍵、關聯碼及Dataverse UTC日期範圍；敏感欄位與疑似結構／編碼／秘密前後值整值遮罩，再借用固定產品租約，重新確認有效Contact與租戶後Create。只有SDK返回正式非空ID及租約清理完成才返回成功。
 * 使用邊界與維護：本服務不執行業務授權、讀取稽核、共同交易或自動重送，業務owner須先授權並承擔寫入與稽核的交易語意。Host擁有診斷與pool；每次租約由executor唯一歸還，同步SDK退出前不提前取消或Dispose。取消後不保證未寫入；不明結果不得盲重送，避免重複稽核事件。真正失敗傳真實Exception至安全共用入口，不輸出原值或例外Message。
 */
using System.Text;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Access;

/// <summary>固定組織依賴、無跨請求資料保留的修改紀錄服務；所有資料最多只在當次呼叫存活。</summary>
public sealed class DataverseAuditLog : IAuditLog
{
    private readonly AccessDataverseExecutor _executor;

    /// <summary>借用固定產品的工廠與安全診斷；不取得租約或接管它們的Host生命週期。</summary>
    public DataverseAuditLog(Func<CancellationToken, IAccessDataverseLease> acquire, ExceptionDiagnostics diagnostics, string product) =>
        _executor = new AccessDataverseExecutor(acquire, diagnostics, product);

    /// <summary>驗證並遮罩後單次Create，不重試；以faulted／canceled Task區分內部故障與caller取消。</summary>
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
                ["new_entity"] = entry.Entity,
                ["new_recordid"] = entry.RecordId.ToString("D"),
                ["new_field"] = entry.Field,
                ["new_before"] = sensitive ? "***" : MaskValue(entry.Before),
                ["new_after"] = sensitive ? "***" : MaskValue(entry.After),
                ["new_actorcontactid"] = new EntityReference("contact", entry.ActorContactId),
                ["new_tenantid"] = new EntityReference("account", entry.TenantId),
                ["new_correlationid"] = entry.CorrelationId,
                ["new_at"] = entry.At.UtcDateTime
            };
            _executor.Execute(service =>
            {
                var actor = new Actor(entry.ActorContactId, entry.TenantId, _executor.Product);
                if (!_executor.ValidateActor(service, actor, ct)) throw new UnauthorizedAccessException("Audit actor is not valid.");
                ct.ThrowIfCancellationRequested();
                Guid id = service.Create(candidate);
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
            entry.RecordId == Guid.Empty || entry.ActorContactId == Guid.Empty || entry.TenantId == Guid.Empty ||
            entry.Before?.Length > 4000 || entry.After?.Length > 4000 || entry.At.UtcDateTime.Year < 1753)
            throw new ArgumentException("Invalid audit entry.", nameof(entry));
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

    /// <summary>不嘗試解析可能含秘密的任意結構；保守整值遮罩避免JSON跳脫、URL或卡號進入稽核表。</summary>
    private static string? MaskValue(string? value)
    {
        if (value is null) return null;
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
