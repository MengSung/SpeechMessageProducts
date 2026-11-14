/*
 * 檔案整體說明
 *
 * 目的與定位：P5-03a 的完整文件部署編排，將兩個核定組件逐一交給既有單組件引擎。
 * 主要功能與流程：輸入 RegistrationDocument 與各組件 AssemblyImage，先驗整份來源、
 * 計畫雜湊及即時組織指紋，全部通過才備份及套用；輸出完整計畫或逐組件具名備份。
 * 使用邊界與維護：只借用命令租約的 SDK、時鐘與秘密委派；連線與組織寫入鎖由 CLI
 * 擁有。沒有 static 可變狀態、快取或背景工作；候選僅存活於同步操作。備份由呼叫端
 * 保留至人工回復期限，串流均在返回前關閉。回復新建組件後清除無引用的安全設定列，
 * 只查 lookup／ID 而不讀秘密內容。跨組件不是資料庫交易；中途 SDK 失敗時
 * 保留備份供明確 rollback，不自動掩蓋原始失敗。驗證依合成 SDK 測試，不宣稱實機結果。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>完整文件的逐組件核准計畫；外層雜湊涵蓋所有子計畫與完整來源摘要。</summary>
/// <param name="Organization">已核定組織。</param><param name="CreatedUtc">規劃時間。</param>
/// <param name="SourceHash">完整文件及映像描述摘要。</param><param name="Assemblies">全部子計畫。</param>
/// <param name="Sha256">整份計畫完整性雜湊，不是數位簽章或授權。</param>
internal sealed record DocumentDeploymentPlan(string Organization, DateTimeOffset CreatedUtc, string SourceHash,
    IReadOnlyList<DocumentAssemblyPlan> Assemblies, string Sha256);

/// <summary>以核定組件名稱綁定原單組件計畫，避免位置或顯示文字作為身分。</summary>
/// <param name="AssemblyName">核定簡名。</param><param name="Plan">單組件完整計畫。</param>
internal sealed record DocumentAssemblyPlan(string AssemblyName, DeploymentPlan Plan);

/// <summary>完整操作的備份索引；只保存安全檔案位置及摘要，不保存 SDK 或已解析秘密。</summary>
/// <param name="Organization">備份組織。</param><param name="Assemblies">所有組件備份。</param>
/// <param name="Sha256">整份索引摘要。</param>
internal sealed record DocumentDeploymentBackup(string Organization, IReadOnlyList<DocumentAssemblyBackup> Assemblies, string Sha256);

/// <summary>同一操作中獨立目錄下的具名備份；檔案摘要防止回復前替換內容。</summary>
/// <param name="AssemblyName">核定組件簡名。</param><param name="Path">本機備份絕對路徑。</param>
/// <param name="Sha256">完整檔案位元組摘要。</param>
internal sealed record DocumentAssemblyBackup(string AssemblyName, string Path, string Sha256);

/// <summary>一次命令生命週期的同步編排器；呼叫端負責寫入鎖及 SDK 租約釋放。</summary>
internal sealed class DocumentDeploymentEngine
{
    private readonly IOrganizationService service;
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly Func<string, string> secret;

    /// <summary>借用命令服務、輸出根、時鐘及按需秘密來源，不接管其生命週期。</summary>
    public DocumentDeploymentEngine(IOrganizationService service, string directory, TimeProvider clock, Func<string, string> secret)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(secret);
        this.service = service;
        this.directory = Path.GetFullPath(directory);
        this.clock = clock;
        this.secret = secret;
    }

    /// <summary>
    /// 規劃完整文件；先複製並驗證所有輸入，再唯讀查詢所有組件，任一失敗不發布部分計畫。
    /// 來源摘要包含全部註冊欄位及映像內容摘要與型別描述，不解析秘密。
    /// </summary>
    public DocumentDeploymentPlan Plan(RegistrationDocument document, IReadOnlyList<AssemblyImage> images, string organization)
    {
        var source = Prepare(document, images, organization);
        return Plan(source, organization);
    }

    /// <summary>
    /// 先驗完整計畫及全部即時指紋，再完成所有備份，最後逐組件套用。子計畫亦與即時規劃
    /// 逐欄比較，重算外層 SHA 不能注入操作。備份先於任何 SDK 寫入且每次呼叫使用唯一目錄。
    /// 後續外部競爭或 SDK 失敗仍可能部分套用；呼叫端須持有組織寫入鎖並保留目錄供回復。
    /// </summary>
    public DocumentDeploymentBackup Apply(DocumentDeploymentPlan plan, RegistrationDocument document, IReadOnlyList<AssemblyImage> images)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var source = Prepare(document, images, plan.Organization);
        // 呼叫端可持有可變集合，因此計畫和來源均在任何回呼／SDK 操作前脫離外部參考。
        var approved = Clone(plan);
        if (approved.Assemblies is null || approved.Assemblies.Count != source.Document.Assemblies.Count ||
            approved.SourceHash != source.Hash || approved.Sha256 != Hash(approved with { Sha256 = "" }))
            throw InvalidDocument();
        var current = Plan(source, approved.Organization);
        for (var index = 0; index < current.Assemblies.Count; index++)
        {
            var child = approved.Assemblies[index];
            var live = current.Assemblies[index];
            if (child is null || child.Plan is null || child.AssemblyName != live.AssemblyName ||
                child.Plan.Sha256 != Hash(child.Plan with { Sha256 = "" }) ||
                // 時間不是 metadata 差異；保留原核准時間，其餘欄位全部必須與即時規劃相同。
                Hash(child.Plan with { Sha256 = "" }) !=
                Hash(live.Plan with { CreatedUtc = child.Plan.CreatedUtc, Sha256 = "" }))
                throw InvalidDocument();
        }
        var backup = Export(source, approved.Organization);
        for (var index = 0; index < approved.Assemblies.Count; index++)
        {
            var item = approved.Assemblies[index];
            var manifest = source.Document.Assemblies[index];
            var output = Path.GetDirectoryName(backup.Assemblies[index].Path)!;
            // 原引擎仍做單組件二次指紋核對及 export.json；文件備份另具名保存，不會被覆寫。
            Engine(output).Apply(item.Plan, manifest, source.Images[index]);
        }
        return backup;
    }

    /// <summary>唯讀重新規劃所有組件；任一差異回一，全相等但秘密未比較回三，完整相等回零。</summary>
    public int Verify(RegistrationDocument document, IReadOnlyList<AssemblyImage> images, string organization)
    {
        var plan = Plan(document, images, organization);
        if (plan.Assemblies.Any(item => item.Plan.Operations.Count != 0)) return 1;
        return plan.Assemblies.Any(item => item.Plan.SecretPlaceholders.Count != 0) ? 3 : 0;
    }

    /// <summary>先唯讀規劃全部來源與 metadata，再逐組件建立獨立具名安全備份；失敗不回傳部分索引。</summary>
    public DocumentDeploymentBackup Export(RegistrationDocument document, IReadOnlyList<AssemblyImage> images, string organization)
    {
        var source = Prepare(document, images, organization);
        _ = Plan(source, organization);
        return Export(source, organization);
    }

    /// <summary>
    /// 先檢查全部索引、檔案摘要、原型別與步驟擁有關係，再委派原引擎回復。
    /// 同時持有最多兩個唯讀 handle，Windows 上禁止寫入／刪除替換直到回復結束；finally
    /// 釋放全部 handle。回復不解析秘密，也不保證 SDK 故障時的跨組件交易。
    /// </summary>
    public void Rollback(DocumentDeploymentBackup backup, RegistrationDocument document, IReadOnlyList<AssemblyImage> images)
    {
        ArgumentNullException.ThrowIfNull(backup);
        var source = Prepare(document, images, backup.Organization);
        var approved = Clone(backup);
        if (approved.Assemblies is null || approved.Assemblies.Count != source.Document.Assemblies.Count ||
            approved.Sha256 != Hash(approved with { Sha256 = "" })) throw InvalidDocument();
        var streams = new List<FileStream>(2);
        var secureConfigurations = new List<Guid[]>(2);
        try
        {
            for (var index = 0; index < approved.Assemblies.Count; index++)
            {
                var item = approved.Assemblies[index];
                var manifest = source.Document.Assemblies[index];
                if (item is null || item.AssemblyName != manifest.AssemblyName ||
                    string.IsNullOrWhiteSpace(item.Path)) throw InvalidDocument();
                var path = Path.GetFullPath(item.Path);
                if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(path) != item.AssemblyName + ".backup.json")
                    throw InvalidDocument();
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(stream);
                if (stream.Length is <= 0 or > 100 * 1024 * 1024 ||
                    Convert.ToHexString(SHA256.HashData(stream)) != item.Sha256) throw InvalidDocument();
                stream.Position = 0;
                DeploymentExport snapshot;
                try { snapshot = JsonSerializer.Deserialize<DeploymentExport>(stream) ?? throw InvalidDocument(); }
                catch (JsonException) { throw InvalidDocument(); }
                ValidateSnapshot(snapshot, manifest.AssemblyName, approved.Organization);
                secureConfigurations.Add(snapshot.Assembly is null
                    ? ReadNewAssemblySecureConfigurations(manifest.AssemblyName) : Array.Empty<Guid>());
            }
            for (var index = 0; index < approved.Assemblies.Count; index++)
            {
                Engine(directory).Rollback(approved.Assemblies[index].Path, source.Document.Assemblies[index], approved.Organization);
                ReleaseOrphanSecureConfigurations(secureConfigurations[index]);
            }
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }

    /// <summary>
    /// 空組件快照代表回復會刪除本次新建的組件；先擷取其步驟安全設定 ID，最多二百五十六筆。
    /// 不讀 secureconfig 內容，候選僅存活於回復期間。非空快照保留既有秘密設定，避免假裝能
    /// 由安全匯出的 presence bit 還原秘密；SDK 故障仍由呼叫端保留備份並明確重試。
    /// </summary>
    private Guid[] ReadNewAssemblySecureConfigurations(string assemblyName)
    {
        var assemblies = ReadRows("pluginassembly", "name", assemblyName, new ColumnSet("name"));
        if (assemblies.Length > 1) throw InvalidDocument();
        if (assemblies.Length == 0) return Array.Empty<Guid>();
        var types = ReadRows("plugintype", "pluginassemblyid", assemblies[0].Id, new ColumnSet("pluginassemblyid"));
        if (types.Length > 256) throw InvalidDocument();
        var ids = new HashSet<Guid>();
        var count = 0;
        foreach (var type in types)
        {
            var steps = ReadRows("sdkmessageprocessingstep", "eventhandler", type.Id,
                new ColumnSet("sdkmessageprocessingstepsecureconfigid"));
            count += steps.Length;
            if (count > 256) throw InvalidDocument();
            foreach (var step in steps)
            {
                var reference = step.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid");
                if (reference is null) continue;
                if (reference.Id == Guid.Empty || reference.LogicalName != "sdkmessageprocessingstepsecureconfig")
                    throw InvalidDocument();
                // 多步驟合法引用同一設定時只安排一次清理；這不是對正式資料列作去重。
                ids.Add(reference.Id);
            }
        }
        return ids.ToArray();
    }

    /// <summary>
    /// 原引擎成功刪除新建組件後才清理已無引用的安全設定；若其他步驟仍引用則由該步驟擁有。
    /// 先驗存在性以允許回復重試，不擷取秘密值；任何 SDK 失敗向上傳遞，不能宣稱已排空。
    /// 呼叫端持有組織寫入鎖，本操作沒有獨立連線、重試佇列或背景清理。
    /// </summary>
    private void ReleaseOrphanSecureConfigurations(IEnumerable<Guid> ids)
    {
        foreach (var id in ids)
        {
            if (ReadRows("sdkmessageprocessingstep", "sdkmessageprocessingstepsecureconfigid", id,
                new ColumnSet("sdkmessageprocessingstepid")).Length != 0) continue;
            if (ReadRows("sdkmessageprocessingstepsecureconfig", "sdkmessageprocessingstepsecureconfigid", id,
                new ColumnSet("sdkmessageprocessingstepsecureconfigid")).Length != 0)
                service.Delete("sdkmessageprocessingstepsecureconfig", id);
        }
    }

    /// <summary>將已脫離呼叫端參考的全部來源交給原引擎；完整成功後才封存候選計畫。</summary>
    private DocumentDeploymentPlan Plan(Source source, string organization)
    {
        var plans = new List<DocumentAssemblyPlan>(2);
        for (var index = 0; index < source.Document.Assemblies.Count; index++)
        {
            var manifest = source.Document.Assemblies[index];
            plans.Add(new DocumentAssemblyPlan(manifest.AssemblyName,
                Engine(directory).Plan(manifest, source.Images[index], organization)));
        }
        var plan = new DocumentDeploymentPlan(organization, clock.GetUtcNow().ToUniversalTime(),
            source.Hash, plans.AsReadOnly(), "");
        return plan with { Sha256 = Hash(plan) };
    }

    /// <summary>每次建立唯一操作根及固定組件子目錄；所有安全匯出完成後才發布完整索引。</summary>
    private DocumentDeploymentBackup Export(Source source, string organization)
    {
        var operationDirectory = Path.Combine(directory, "document-" + Guid.NewGuid().ToString("N"));
        var backups = new List<DocumentAssemblyBackup>(2);
        foreach (var manifest in source.Document.Assemblies)
        {
            var path = Path.Combine(operationDirectory, manifest.AssemblyName, manifest.AssemblyName + ".backup.json");
            Engine(Path.GetDirectoryName(path)!).WriteExport(organization, path, manifest.AssemblyName);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            backups.Add(new DocumentAssemblyBackup(manifest.AssemblyName, path, Convert.ToHexString(SHA256.HashData(stream))));
        }
        var backup = new DocumentDeploymentBackup(organization, backups.AsReadOnly(), "");
        return backup with { Sha256 = Hash(backup) };
    }

    /// <summary>
    /// 在任何組織 I/O 前驗完整文件、跨組件舊步驟 ID 與一對一映像。文件上限一 MiB，
    /// 每映像小於十六 MiB，與 AssemblyCatalog 相同；字串與型別集合只保留本操作的獨立副本。
    /// PE／簽章結構仍由 AssemblyCatalog 負責，這裡另核對實際 bytes 摘要防止 DTO 內容被替換。
    /// </summary>
    private static Source Prepare(RegistrationDocument document, IReadOnlyList<AssemblyImage> images, string organization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(images);
        if (organization != "goodshepherdback" || document.SchemaVersion != 2 ||
            document.Assemblies is null || document.Assemblies.Count is < 1 or > 2 ||
            images.Count != document.Assemblies.Count) throw InvalidDocument();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var legacyIds = new HashSet<Guid>();
        foreach (var manifest in document.Assemblies)
        {
            if (manifest is null || !names.Add(manifest.AssemblyName)) throw InvalidDocument();
            RegistrationManifestLoader.Validate(manifest);
            foreach (var legacy in manifest.Steps.SelectMany(step => step.Legacy))
                if (!legacyIds.Add(Guid.Parse(legacy.StepId))) throw InvalidDocument();
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        if (bytes.Length > 1024 * 1024) throw InvalidDocument();
        var candidate = JsonSerializer.Deserialize<RegistrationDocument>(bytes) ?? throw InvalidDocument();
        candidate = candidate with
        {
            Assemblies = candidate.Assemblies.OrderBy(manifest => manifest.AssemblyName, StringComparer.Ordinal).ToArray()
        };
        var imageNames = new HashSet<string>(StringComparer.Ordinal);
        var copiedImages = new List<AssemblyImage>(2);
        foreach (var image in images)
        {
            if (image is null || !names.Contains(image.Name) || !imageNames.Add(image.Name) ||
                image.Content is null || image.Content.Length is < 1 or > 22369624 ||
                image.PluginTypes is null || image.ActivityTypes is null ||
                image.PluginTypes.Count + image.ActivityTypes.Count > 256) throw InvalidDocument();
            byte[] content;
            try { content = Convert.FromBase64String(image.Content); }
            catch (FormatException) { throw InvalidDocument(); }
            if (content.Length is <= 0 or >= 16 * 1024 * 1024 ||
                Convert.ToHexString(SHA256.HashData(content)) != image.Sha256) throw InvalidDocument();
            copiedImages.Add(image with
            {
                PluginTypes = Array.AsReadOnly(image.PluginTypes.ToArray()),
                ActivityTypes = Array.AsReadOnly(image.ActivityTypes.ToArray())
            });
        }
        var ordered = copiedImages.OrderBy(image => image.Name, StringComparer.Ordinal).ToArray();
        // Content 由已核對的 Sha256 代表，避免為計算來源摘要再複製兩個大型 Base64 字串。
        return new Source(candidate, ordered, Hash(new
        {
            Document = candidate,
            Images = ordered.Select(image => image with { Content = "" }).ToArray()
        }));
    }

    /// <summary>
    /// 回復前核對安全匯出及即時父身分；型別／步驟不可遺失或改屬其他組件。
    /// 只查受管理 metadata，不讀 secureconfig、業務資料或跨組織狀態。
    /// </summary>
    private void ValidateSnapshot(DeploymentExport snapshot, string assemblyName, string organization)
    {
        if (snapshot.SchemaVersion != 2 || snapshot.Organization != organization ||
            snapshot.ManagedAssemblyName != assemblyName || snapshot.Types is null ||
            snapshot.Steps is null || snapshot.Images is null ||
            snapshot.Types.Count > 256 || snapshot.Steps.Count > 256 || snapshot.Images.Count > 32768 ||
            snapshot.Sha256 != Hash(snapshot with { Sha256 = "" })) throw InvalidDocument();
        var typeIds = new HashSet<Guid>();
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var stepIds = new HashSet<Guid>();
        var imageIds = new HashSet<Guid>();
        foreach (var type in snapshot.Types)
            if (type is null || type.Id == Guid.Empty || !typeIds.Add(type.Id) ||
                string.IsNullOrWhiteSpace(type.TypeName) || !typeNames.Add(type.TypeName)) throw InvalidDocument();
        foreach (var step in snapshot.Steps)
            if (step is null || step.Id == Guid.Empty || !stepIds.Add(step.Id) || !typeNames.Contains(step.TypeName))
                throw InvalidDocument();
        foreach (var image in snapshot.Images)
            if (image is null || image.Id == Guid.Empty || !imageIds.Add(image.Id) || !stepIds.Contains(image.StepId))
                throw InvalidDocument();
        if (snapshot.Assembly is null)
        {
            if (typeIds.Count != 0 || stepIds.Count != 0 || imageIds.Count != 0) throw InvalidDocument();
            return;
        }
        if (snapshot.Assembly.Name != assemblyName || snapshot.Assembly.Id == Guid.Empty) throw InvalidDocument();
        var assemblies = ReadRows("pluginassembly", "name", assemblyName, new ColumnSet("name"));
        if (assemblies.Length > 1) throw InvalidDocument();
        var currentAssembly = assemblies.SingleOrDefault();
        if (typeIds.Count > 0 && (currentAssembly is null || currentAssembly.Id != snapshot.Assembly.Id)) throw InvalidDocument();
        if (currentAssembly is not null)
        {
            var types = ReadRows("plugintype", "pluginassemblyid", currentAssembly.Id, new ColumnSet("typename", "pluginassemblyid"));
            foreach (var previous in snapshot.Types)
                if (!types.Any(type => type.Id == previous.Id && type.GetAttributeValue<string>("typename") == previous.TypeName))
                    throw InvalidDocument();
        }
        foreach (var previous in snapshot.Steps)
        {
            var steps = ReadRows("sdkmessageprocessingstep", "sdkmessageprocessingstepid", previous.Id, new ColumnSet("eventhandler"));
            if (steps.Length != 1) throw InvalidDocument();
            var owner = steps[0].GetAttributeValue<EntityReference>("eventhandler");
            var expected = snapshot.Types.Single(type => type.TypeName == previous.TypeName);
            if (owner is null || owner.LogicalName != "plugintype" || owner.Id != expected.Id) throw InvalidDocument();
        }
    }

    /// <summary>精確且有界的回復查詢；超過頁面、空／重複 ID 均拒絕，不發布不完整候選。</summary>
    private Entity[] ReadRows(string logicalName, string attribute, object value, ColumnSet columns)
    {
        var query = new QueryExpression(logicalName) { ColumnSet = columns, TopCount = 4097 };
        query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        var result = service.RetrieveMultiple(query);
        var ids = new HashSet<Guid>();
        if (result.MoreRecords || result.Entities.Count > 4096 ||
            result.Entities.Any(row => row.Id == Guid.Empty || !ids.Add(row.Id))) throw InvalidDocument();
        return result.Entities.ToArray();
    }

    /// <summary>原單組件引擎按需建立；不取得借入 SDK 的 Dispose 責任。</summary>
    private DeploymentEngine Engine(string output) => new(service, output, clock, secret);

    /// <summary>以純 DTO 序列化作完整性摘要；只有範本／安全 metadata，不輸入解析後秘密。</summary>
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    /// <summary>同步深複製 DTO 的集合，防止 SDK 回呼或呼叫端修改影響已核准候選。</summary>
    private static T Clone<T>(T value)
    {
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value)) ?? throw InvalidDocument(); }
        catch (JsonException) { throw InvalidDocument(); }
    }

    /// <summary>固定錯誤不包含來源 JSON、檔案內容、SDK 回應或秘密。</summary>
    private static InvalidDataException InvalidDocument() => new("完整部署文件、來源、計畫或備份驗證失敗。");

    /// <summary>本操作獨占的已驗來源，只在同步方法堆疊內保留，不跨命令共用。</summary>
    private sealed record Source(RegistrationDocument Document, AssemblyImage[] Images, string Hash);
}
