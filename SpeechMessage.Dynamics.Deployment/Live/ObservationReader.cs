/*
檔案整體說明
目的與定位：P7觀察期唯讀計數reader，依核定step/type與UTC時間窗計系統作業、trace、待辦及流程失敗，不輸出記錄內容。
主要流程：先驗正式ID與唯一type對照，再有界讀取最少欄位，全部成功組裝安全純值；trace例外按Plugin型別及解析出的例外型別分組，關閉或權限不足明示無資料。
安全與隔離：借入SDK先包ReadOnlyOrganizationService；回傳不包含GUID、名稱、錯誤本文、payload或credential，只含型別識別與數量。
資源生命週期：候選列只存活本次同步Read，取消在每頁前後檢查；不持有cache、背景task、timer或獨立連線。
失敗與驗證：分頁／重複／超量或必要狀態缺漏fail closed，不當空結果；ObservationReaderTests驗counts與敏感sentinel零輸出。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>命令從新組件metadata核定的正式步驟與完整型別，僅作查詢scope，不輸出ID。</summary>
/// <param name="Id">正式step ID。</param><param name="Type">核定type名稱。</param>
internal sealed record ObservationStep(Guid Id, string Type);
/// <summary>一型別的系統作業安全計數。</summary>
/// <param name="Type">型別名稱。</param><param name="Failed">失敗數。</param><param name="Waiting">未完成數。</param>
internal sealed record ObservationStepCount(string Type, int Failed, int Waiting);
/// <summary>一類型／狀態待辦的安全計數。</summary>
/// <param name="Type">選項值。</param><param name="State">狀態選項值。</param><param name="Count">筆數。</param>
internal sealed record ObservationTaskCount(int Type, int State, int Count);
/// <summary>一種Plugin型別與例外型別組合的trace安全計數，不保存任何trace本文。</summary>
/// <param name="PluginType">經安全字元檢查的Plugin完整型別，無法確認時為固定未知標籤。</param>
/// <param name="ExceptionType">從例外本文首行解析並驗證的CLR型別，無法確認時為固定未知標籤。</param>
/// <param name="Count">符合查詢時間窗的例外筆數。</param>
internal sealed record ObservationTraceExceptionCount(string PluginType, string ExceptionType, int Count);
/// <summary>觀察安全報告，不包含正式記錄身分或任何欄位本文。</summary>
/// <param name="SinceUtc">UTC查詢下限。</param><param name="Steps">新組件系統作業counts。</param><param name="TraceAvailable">當下trace可用與否。</param>
/// <param name="TraceExceptions">不可用為null，查詢成功且空才為0。</param><param name="TraceExceptionGroups">例外型別與Plugin型別的有序安全計數。</param><param name="Tasks">待辦counts。</param>
/// <param name="OldestWaitingMinutes">最舊等待分鐘，沒有等待為null。</param><param name="WorkflowFailures">已改指流程失敗數。</param><param name="MutualExclusionPassed">D7-7當下結果。</param>
internal sealed record ObservationReport(DateTimeOffset SinceUtc, IReadOnlyList<ObservationStepCount> Steps, bool TraceAvailable,
    int? TraceExceptions, IReadOnlyList<ObservationTraceExceptionCount> TraceExceptionGroups,
    IReadOnlyList<ObservationTaskCount> Tasks, double? OldestWaitingMinutes, int WorkflowFailures, bool MutualExclusionPassed)
{
    /// <summary>只根據可用計數判斷；trace無資料不能宣稱沒有新增錯誤。</summary>
    public bool NoNewErrors => TraceAvailable && TraceExceptions == 0 && Steps.All(row => row.Failed == 0) &&
        Tasks.All(row => row.State != 100000003) && (OldestWaitingMinutes is null or <= 30) && WorkflowFailures == 0 && MutualExclusionPassed;
}

/// <summary>單次唯讀觀察入口，production與test皆允許，但白名單與production旗標仍由CLI先驗。</summary>
internal static class ObservationReader
{
    /// <summary>只讀固定白名單欄位；since必UTC且不能晚於now；每類上限一萬筆，拒絕不完整或重複正式ID。</summary>
    internal static ObservationReport Read(IOrganizationService service, string kind, Guid organizationId, DateTimeOffset sinceUtc,
        DateTimeOffset now, IReadOnlyList<ObservationStep> steps, IReadOnlyList<Guid> workflowIds, bool mutualExclusionPassed, IReadOnlyList<string>? activityTypes = null, CancellationToken cancellationToken = default)
    {
        if (kind is not ("test" or "production") || organizationId == Guid.Empty || sinceUtc.Offset != TimeSpan.Zero || sinceUtc > now ||
            steps.Count > 256 || (activityTypes?.Count ?? 0) > 256 ||
            (activityTypes?.Any(type => string.IsNullOrWhiteSpace(type) || type.Length > 512) ?? false) || workflowIds.Count > 10000 || steps.Any(row => row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.Type)) ||
            steps.Select(row => row.Id).Distinct().Count() != steps.Count || workflowIds.Any(id => id == Guid.Empty) || workflowIds.Distinct().Count() != workflowIds.Count) throw Invalid();
        var readOnly = new ReadOnlyOrganizationService(service);
        var reader = new LegacyRegistrationExporter.MetadataReader(readOnly, 500, 40000, 100, cancellationToken);
        var jobs = reader.Read("asyncoperation", ["owningextensionid", "statecode", "statuscode", "createdon"]).Where(row => AtOrAfter(row, sinceUtc)).ToArray();
        var counts = steps.GroupBy(row => row.Type, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
        {
            var ids = group.Select(row => row.Id).ToHashSet();
            var scoped = jobs.Where(row => row.GetAttributeValue<EntityReference>("owningextensionid") is { } reference && ids.Contains(reference.Id)).ToArray();
            return new ObservationStepCount(group.Key, scoped.Count(Failed), scoped.Count(row => Option(row, "statecode") != 3));
        }).ToArray();
        var taskRows = reader.Read("new_outboundtask", ["new_type", "new_taskstatus", "createdon"]).ToArray();
        var tasks = taskRows.GroupBy(row => (Type: Option(row, "new_type"), State: Option(row, "new_taskstatus")))
            .OrderBy(group => group.Key.Type).ThenBy(group => group.Key.State)
            .Select(group => new ObservationTaskCount(group.Key.Type, group.Key.State, group.Count())).ToArray();
        var waiting = taskRows.Where(row => Option(row, "new_taskstatus") == 100000000).Select(row => Date(row, "createdon")).ToArray();
        double? oldest = waiting.Length == 0 ? null : Math.Max(0, (now.UtcDateTime - waiting.Min()).TotalMinutes);
        cancellationToken.ThrowIfCancellationRequested();
        var organization = readOnly.Retrieve("organization", organizationId, new Microsoft.Xrm.Sdk.Query.ColumnSet("plugintracelogsetting"));
        cancellationToken.ThrowIfCancellationRequested();
        var available = Option(organization, "plugintracelogsetting") != 0;
        int? exceptions = null;
        IReadOnlyList<ObservationTraceExceptionCount> exceptionGroups = Array.Empty<ObservationTraceExceptionCount>();
        if (available)
        {
            try
            {
                var types = steps.Select(row => row.Type).Concat(activityTypes ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
                var traces = reader.Read("plugintracelog", ["typename", "createdon", "exceptiondetails"])
                    .Where(row => AtOrAfter(row, sinceUtc) && types.Contains(row.GetAttributeValue<string>("typename") ?? "") &&
                        !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("exceptiondetails")))
                    .Select(row => (PluginType: SafePluginType(row.GetAttributeValue<string>("typename")),
                        ExceptionType: SafeExceptionType(row.GetAttributeValue<string>("exceptiondetails"))))
                    .ToArray();
                exceptions = traces.Length;
                exceptionGroups = Array.AsReadOnly(traces.GroupBy(row => (row.PluginType, row.ExceptionType))
                    .OrderBy(group => group.Key.PluginType, StringComparer.Ordinal)
                    .ThenBy(group => group.Key.ExceptionType, StringComparer.Ordinal)
                    .Select(group => new ObservationTraceExceptionCount(group.Key.PluginType, group.Key.ExceptionType, group.Count()))
                    .ToArray());
            }
            catch (FaultException<OrganizationServiceFault> failure) when (failure.Detail?.ErrorCode is unchecked((int)0x80040220) or unchecked((int)0x80048306)) { available = false; exceptions = null; }
        }
        var workflows = workflowIds.ToHashSet();
        var workflowFailures = jobs.Count(row => row.GetAttributeValue<EntityReference>("owningextensionid") is { LogicalName: "workflow" } reference &&
            workflows.Contains(reference.Id) && Failed(row));
        return new ObservationReport(sinceUtc, Array.AsReadOnly(counts), available, exceptions, exceptionGroups,
            Array.AsReadOnly(tasks), oldest, workflowFailures, mutualExclusionPassed);
    }

    /// <summary>外部trace欄位只接受bounded CLR型別字元；任意文字不回傳給報告消費端。</summary>
    private static string SafePluginType(string? value) => IsSafeTypeName(value) ? value! : "(unknown-plugin)";

    /// <summary>僅解析例外本文第一行冒號前的型別token；訊息、stack及其餘本文不進入報告。</summary>
    private static string SafeExceptionType(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(unknown-exception)";
        var firstLineEnd = value.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0 ? value.AsSpan() : value.AsSpan(0, firstLineEnd);
        var separator = firstLine.IndexOf(':');
        if (separator <= 0) return "(unknown-exception)";
        var candidate = firstLine[..separator].Trim();
        return IsSafeTypeName(candidate) ? candidate.ToString() : "(unknown-exception)";
    }

    /// <summary>CLR型別標籤限長並限字元，避免把自由本文或控制字元投影進診斷。</summary>
    private static bool IsSafeTypeName(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > 512 || !char.IsAsciiLetter(value[0])) return false;
        foreach (var character in value)
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '`' or '[' or ']' or ',' or '&' or '*'))
                return false;
        return true;
    }

    /// <summary>平台成功status30與失敗31／32；非完成作業不計失敗，但另列waiting。</summary>
    private static bool Failed(Entity row) => Option(row, "statecode") == 3 && Option(row, "statuscode") != 30;
    /// <summary>日期只接受正式DateTime，未指定Kind按CRM UTC解讀，不使用主機本地文化。</summary>
    private static DateTime Date(Entity row, string field) => row.GetAttributeValue<DateTime?>(field) is { } value
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : throw Invalid();
    /// <summary>以UTC下限比對已讀純metadata，沒有秘密或任意查詢文字。</summary>
    private static bool AtOrAfter(Entity row, DateTimeOffset sinceUtc) => Date(row, "createdon") >= sinceUtc.UtcDateTime;
    /// <summary>必要狀態缺漏不得默認0，避免把未知資料當成功。</summary>
    private static int Option(Entity row, string field) => row.Attributes.TryGetValue(field, out var value)
        ? value switch { OptionSetValue option => option.Value, int number => number, _ => throw Invalid() } : throw Invalid();
    /// <summary>固定拒絕不帶SDK本文或記錄值。</summary>
    private static InvalidDataException Invalid() => new("Observation metadata is invalid or incomplete.");
}
