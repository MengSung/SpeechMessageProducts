/*
檔案整體說明
目的與定位：P7教會語系預設值的窄範圍plan/apply核心，僅補account已綁Channel的四個空白欄位，從不覆蓋已有設定。
主要流程：有界讀取正式ID／Channel及四欄候選，完整驗證後產只含ID與待補欄位名的計畫；apply重算server與指紋。
安全與回復：計畫不含教會名稱或原欄位值，只存摘要；每次更新前重讀空白及版本，先flush精確備份再送SDK。
失敗／取消反序恢復本次attempted欄位，使用None token；原例外與清理失敗聚合，不能假稱回復成功。
資源與隔離：SDK與server讀取委派由單命令lease借入，備份stream同步flush後關閉；無static mutable、跨組織cache或背景工作。
驗證：ChurchDefaultsTests用合成SDK驗部分空白、冪等、無Channel及hash拒絕，不宣稱正式或測試組織實機結果。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using SpeechMessage.Dynamics.Deployment.Switching;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>只列正式account ID與待補欄位名，不保存原值或教會名稱。</summary>
/// <param name="Id">正式account ID。</param><param name="Fields">四欄白名單的待補子集合。</param>
internal sealed record ChurchDefaultsEntry(Guid Id, IReadOnlyList<string> Fields);

/// <summary>可核定的純值計畫；指紋對所有有Channel列四欄及lookup計算，不公布原值。</summary>
/// <param name="SchemaVersion">固定格式1。</param><param name="Organization">核定名稱。</param><param name="ServerOrganizationId">權威ID。</param>
/// <param name="Fingerprint">當下唯讀投影摘要。</param><param name="Entries">只補空白的項目。</param><param name="Sha256">完整性摘要。</param>
internal sealed record ChurchDefaultsPlan(int SchemaVersion, string Organization, Guid ServerOrganizationId, string Fingerprint,
    IReadOnlyList<ChurchDefaultsEntry> Entries, string Sha256);

/// <summary>一次已授權命令的defaults核心；呼叫端寫入前須共同ProductionGuard及同組織獨占寫鎖。</summary>
internal sealed class ChurchDefaults(IOrganizationService service, string directory, Func<Guid> serverId)
{
    private static readonly string[] Fields = { "new_defaultlanguage", "new_enabledlanguages", "new_timezone", "new_countrycode" };
    private static readonly string[] Defaults = { "zh-TW", "zh-TW", "Asia/Taipei", "TW" };

    /// <summary>只查有正式Channel lookup的account，四欄獨立空白才列入，不將已有值改成預設。</summary>
    internal ChurchDefaultsPlan Plan(string organization, CancellationToken cancellationToken = default)
    {
        var server = serverId();
        if (server == Guid.Empty || string.IsNullOrWhiteSpace(organization)) throw Invalid();
        var rows = Read(cancellationToken);
        var entries = rows.Select(row => new ChurchDefaultsEntry(row.Id,
            Array.AsReadOnly(Fields.Where(field => string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(field))).ToArray())))
            .Where(entry => entry.Fields.Count != 0).ToArray();
        var fingerprint = SwitchPlanner.Hash(rows.Select(row => new { row.Id, Channel = row.GetAttributeValue<EntityReference>("new_linechannelid").Id,
            Values = Fields.Select(field => row.GetAttributeValue<string>(field)).ToArray() }).ToArray());
        var result = new ChurchDefaultsPlan(1, organization, server, fingerprint, Array.AsReadOnly(entries), "");
        return result with { Sha256 = SwitchPlanner.Hash(result) };
    }

    /// <summary>嚴格hash與鎖內重算一致才寫；每筆SDK前保存attempted欄位，不覆蓋並行新增的非空白值。</summary>
    internal void Apply(ChurchDefaultsPlan approved, string reviewedHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approved);
        if (approved.SchemaVersion != 1 || approved.Sha256 != reviewedHash || approved.Sha256 != SwitchPlanner.Hash(approved with { Sha256 = "" })) throw Invalid();
        var current = Plan(approved.Organization, cancellationToken);
        if (current.Sha256 != approved.Sha256) throw Invalid();
        var attempted = new List<(Guid Id, Dictionary<string, string?> Values)>();
        var path = Path.Combine(directory, "church-defaults-before-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            foreach (var entry in approved.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = service.Retrieve("account", entry.Id, new ColumnSet(Fields.Append("new_linechannelid").Append("versionnumber").ToArray()));
                ValidateChannel(row);
                if (entry.Fields.Any(field => !Fields.Contains(field, StringComparer.Ordinal) || !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(field)))) throw Invalid();
                var before = entry.Fields.ToDictionary(field => field, field => (string?)row.GetAttributeValue<string>(field), StringComparer.Ordinal);
                attempted.Add((entry.Id, before));
                Persist(path, attempted);
                var patch = new Entity("account", entry.Id) { RowVersion = Version(row) };
                foreach (var field in entry.Fields) patch[field] = Defaults[Array.IndexOf(Fields, field)];
                try { Write(patch); }
                catch (FaultException<OrganizationServiceFault> failure) when (failure.Detail?.ErrorCode == unchecked((int)0x80060882))
                {
                    // 官方版本衝突代表本次更新未送達；不可回復他人剛寫入且恰好等於預設的值。
                    attempted.RemoveAt(attempted.Count - 1);
                    throw;
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            foreach (var item in attempted.AsEnumerable().Reverse())
            {
                try
                {
                    var now = service.Retrieve("account", item.Id, new ColumnSet(item.Values.Keys.Append("versionnumber").ToArray()));
                    var restore = new Entity("account", item.Id) { RowVersion = Version(now) };
                    foreach (var field in item.Values)
                    {
                        var actual = now.GetAttributeValue<string>(field.Key);
                        if (actual == Defaults[Array.IndexOf(Fields, field.Key)]) restore[field.Key] = field.Value;
                        else if (actual != field.Value) throw Invalid();
                    }
                    if (restore.Attributes.Count != 0) Write(restore);
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count > 1) throw new AggregateException("Church defaults and restoration failed.", failures);
            throw;
        }
    }

    /// <summary>完整有界分頁查詢；無Channel列不讀取，重複ID／錯lookup／超量拒絕，不發布部分結果。</summary>
    private Entity[] Read(CancellationToken cancellationToken)
    {
        var reader = new LegacyRegistrationExporter.MetadataReader(service, 500, 10000, 100, cancellationToken);
        // MetadataReader的IN值不能表達NotNull，故以account正式主鍵排序查詢並在本次候選篩Channel。
        var rows = reader.Read("account", Fields.Append("new_linechannelid").ToArray()).Where(row => row.Contains("new_linechannelid") && row["new_linechannelid"] is not null).ToArray();
        foreach (var row in rows) ValidateChannel(row);
        return rows.OrderBy(row => row.Id).ToArray();
    }

    /// <summary>非空lookup必須是new_linechannel正式ID；不以名稱或其他實體的GUID視為Channel授權。</summary>
    private static void ValidateChannel(Entity row)
    {
        if (row.LogicalName != "account" || row.Id == Guid.Empty || row.GetAttributeValue<EntityReference>("new_linechannelid")
            is not { LogicalName: "new_linechannel" } channel || channel.Id == Guid.Empty ||
            Fields.Any(field => row.Contains(field) && row[field] is not (null or string))) throw Invalid();
    }

    /// <summary>每筆必須有有效版本，不能在版本未知時降級成無條件覆寫。</summary>
    private static string Version(Entity row) => !string.IsNullOrWhiteSpace(row.RowVersion) ? row.RowVersion :
        row.GetAttributeValue<long?>("versionnumber") is > 0 and var value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw Invalid();

    /// <summary>一律官方樂觀並行；讀取至寫入間有其他 writer 即拒絕，回復亦不得蓋掉並行變更。</summary>
    private void Write(Entity patch)
    {
        if (string.IsNullOrWhiteSpace(patch.RowVersion)) throw Invalid();
        service.Execute(new UpdateRequest { Target = patch, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
    }

    /// <summary>空白原值也需逐筆備份，stream flush後關閉；只含精確ID與核定四欄，不含名稱或secret。</summary>
    private static void Persist(string path, IReadOnlyList<(Guid Id, Dictionary<string, string?> Values)> attempted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(attempted.Select(item => new { item.Id, item.Values }).ToArray());
        if (bytes.Length > 4 * 1024 * 1024) throw Invalid();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>固定錯誤不回顯教會原值、名稱、SDK回應或輸入JSON。</summary>
    private static InvalidDataException Invalid() => new("Church defaults plan or current state is invalid.");
}
