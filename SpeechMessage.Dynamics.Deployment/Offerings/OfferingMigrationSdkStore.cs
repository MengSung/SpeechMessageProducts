/*
 * 檔案整體說明
 *
 * 目的與定位：將一次Deployment命令借入的IOrganizationService投影為奉獻轉換器需要的純值，
 * 並提供唯一的Upsert與ExecuteMultiple寫入邊界。本類不建立或Dispose連線；lease由CLI唯一擁有。
 * 遷移前備份模式只讀必要舊選項、付款人與可選直接教會欄，不合併跨欄位的類別標籤，
 * 且不要求尚未部署的新lookup或類別實體；僅回傳可能回填來源的實體／正式ID／教會ID，
 * 交由固定備份寫入器保存。一般plan/apply仍要求兩欄映射無歧義，標籤衝突時拒絕操作。
 * 初始化只讀核對server organization ID／uniqueName、基礎語言1028、兩個choice欄、來源欄位型別、
 * offeringcategory欄及account/contact必要lookup；選項值、標籤和兩欄RequiredLevel均來自server metadata。
 * 除建立plan外，apply會呼叫RecreatePlan，以原plan建立時間重新使用此同一份已核對的metadata、
 * 教會及業務列投影，供executor比對全部將影響寫入的候選欄位後才允許Upsert。
 *
 * 主要功能與流程：讀取每頁最多5000列、最多10頁且來源／account/category總候選各有50000列硬上限；主鍵排序、
 * paging cookie、頁間ID重複、空續頁與取消均核對，任一不完整就拒絕整份候選。收費單及預約只讀
 * 伺服器ID、舊選項、付款人、直接教會（僅metadata存在new_accountid account lookup時）和新lookup；
 * 付款人教會依contact.parentcustomerid且必須為account。完整候選只在本命令存活，不輸出名稱或主鍵。
 *
 * 寫入僅Upsert new_offeringcategory，替代鍵完整使用new_accountid＋new_code，並使用SDK回傳Target ID；
 * 回填以最多100筆ExecuteMultiple，逐筆核對RequestIndex／Fault／Response。沒有自動重送，故障停止後續批次，
 * 重試時重新分頁；已填lookup由executor跳過。SDK同步呼叫前後檢查caller取消，外層dispose先排空lease再放鎖。
 * 使用邊界與維護：本檔無static可變狀態、cache、timer、task或credential。合成SDK測試須覆蓋欄位拒絕、完整分頁、
 * fault／取消、正式回傳ID及跨教會映射；測試不代表Dataverse實機行為。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Offerings;

/// <summary>本命令獨占的奉獻來源投影store；只借用SDK，純值資料僅於一次CLI呼叫保留。</summary>
internal sealed class OfferingMigrationSdkStore : IOfferingMigrationStore, IDisposable
{
    private const int PageSize = 5000;
    private const int MaximumPages = 10;
    private const int MaximumRows = PageSize * MaximumPages;
    private const int BatchSize = 100;
    private const int OrganizationLanguage = 1028;
    private const string CategoryTable = "new_offeringcategory";
    private const string CategoryLookup = "new_offeringcategoryid";
    private static readonly string[] ContactColumns = { "parentcustomerid" };
    private static readonly string[] CategoryColumns = { "new_accountid" };
    private static readonly string[] CategoryAlternateKey = { "new_accountid", "new_code" };

    private static readonly SourceDefinition[] Sources =
    {
        new("new_fee", "new_category", "new_contact_new_fee"),
        new("new_dedication_booking", "new_dedication_category", "new_contact_new_dedication_booking")
    };

    private IOrganizationService? service;
    private readonly CancellationToken cancellationToken;
    private readonly IReadOnlyList<OfferingOption> options;
    private readonly string feeRequiredLevel;
    private readonly string bookingRequiredLevel;
    private readonly string?[] directChurchAttributes;
    private readonly string metadataFingerprint;
    private readonly bool backupOnly;
    private bool disposed;

    /// <summary>通過RetrieveCurrentOrganization與共同catalog核對的正式server組織ID。</summary>
    public Guid OrganizationId { get; }
    /// <summary>server uniqueName，必須與共同入口核准的target完全一致。</summary>
    public string OrganizationName { get; }
    /// <summary>Choice標籤／RequiredLevel／固定畫面選項的SHA-256，不含來源資料列或主鍵。</summary>
    public string MetadataFingerprint => metadataFingerprint;

    /// <summary>
    /// 驗證metadata後建立單命令唯讀選項快照。SDK仍由呼叫端lease擁有；任何欄位型別或語系歧義
    /// 在讀業務資料之前拒絕，不能用本機文化或程式碼選項表補成實際組織資訊。
    /// </summary>
    /// <param name="service">由外層單次OrganizationLease借入的SDK服務。</param>
    /// <param name="target">OrganizationCatalog核准的唯一命令目標。</param>
    /// <param name="cancellationToken">由CLI owner提供的caller取消訊號。</param>
    /// <param name="backupOnly">只建遷移前ID備份讀取器，不要求新lookup與類別結構已存在。</param>
    internal OfferingMigrationSdkStore(IOrganizationService service, OrganizationDefinition target,
        CancellationToken cancellationToken, bool backupOnly = false)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentNullException.ThrowIfNull(target);
        this.cancellationToken = cancellationToken;
        this.backupOnly = backupOnly;
        cancellationToken.ThrowIfCancellationRequested();
        var current = Execute<RetrieveCurrentOrganizationResponse>(
            new RetrieveCurrentOrganizationRequest { AccessType = EndpointAccessType.Default }).Detail;
        if (current is null || current.OrganizationId == Guid.Empty
            || !string.Equals(current.UniqueName, target.Name, StringComparison.Ordinal)
            || !Version.TryParse(current.OrganizationVersion, out _))
            throw new InvalidDataException("Offering migration server identity does not match the authorized organization.");
        OrganizationId = current.OrganizationId;
        OrganizationName = current.UniqueName;
        var organization = Service.Retrieve("organization", OrganizationId, new ColumnSet("languagecode"));
        cancellationToken.ThrowIfCancellationRequested();
        if (organization is null || organization.LogicalName != "organization" || organization.Id != OrganizationId
            || !organization.Attributes.TryGetValue("languagecode", out var rawLanguage)
            || rawLanguage is not int language || language != OrganizationLanguage)
            throw new InvalidDataException("Offering migration requires organization language 1028.");

        var metadata = new EntityMetadata[Sources.Length];
        for (var index = 0; index < Sources.Length; index++)
            metadata[index] = ReadEntityMetadata(Sources[index].Entity);
        var feeOptions = ReadOptions(metadata[0], Sources[0].OptionAttribute, out feeRequiredLevel);
        var bookingOptions = ReadOptions(metadata[1], Sources[1].OptionAttribute, out bookingRequiredLevel);
        // 備份只需舊欄位值及來源身份；不同欄位的同值異標籤不影響ID清冊，映射仍由遷移路徑拒絕。
        options = backupOnly
            ? Array.Empty<OfferingOption>()
            : MergeOptionSets(feeOptions, bookingOptions, feeRequiredLevel, bookingRequiredLevel);
        directChurchAttributes = new string?[Sources.Length];
        for (var index = 0; index < Sources.Length; index++)
        {
            ValidateSourceMetadata(metadata[index], Sources[index], requireCategoryLookup: !backupOnly);
            directChurchAttributes[index] = ReadOptionalDirectChurchAttribute(metadata[index]);
        }
        if (!backupOnly)
        {
            var categoryMetadata = ReadEntityMetadata(CategoryTable);
            var categoryKey = ReadCategoryKeyMetadata();
            ValidateCategoryMetadata(categoryMetadata, categoryKey);
        }
        metadataFingerprint = OfferingMigrationPlanner.FingerprintMetadata(
            options, OfferingMigrationCatalog.ScreenOptions, feeRequiredLevel, bookingRequiredLevel);
    }

    /// <summary>建立單次純值plan；教會集合完整來自account正式主鍵，資料列仍由獨立來源摘要驗證。</summary>
    internal OfferingMigrationPlan CreatePlan(IReadOnlyList<OfferingScreenOption> screenOptions)
    {
        ThrowIfDisposed();
        if (backupOnly) throw new InvalidOperationException("Backup-only store cannot create migration plans.");
        var churches = ReadChurchIds();
        var records = ReadSourceRecords();
        return OfferingMigrationPlanner.Create(OrganizationId, OrganizationName, churches, options,
            records, screenOptions, DateTimeOffset.UtcNow, feeRequiredLevel, bookingRequiredLevel);
    }

    /// <summary>以呼叫端plan的建立時間重建目前完整候選；時間只作顯示欄，不影響metadata或資料授權。</summary>
    /// <param name="createdUtc">原plan的UTC時間，僅用來保持序列化欄位，不作為可信來源。</param>
    /// <param name="records">executor本次完整讀取並將據以回填的同一份來源快照，避免重驗與寫入之間重新查詢。</param>
    /// <returns>依當下已核對server資料重建的有界候選。</returns>
    public OfferingMigrationPlan RecreatePlan(DateTimeOffset createdUtc, IReadOnlyList<OfferingSourceRecord> records)
    {
        ThrowIfDisposed();
        var churches = ReadChurchIds();
        return OfferingMigrationPlanner.Create(OrganizationId, OrganizationName, churches, options,
            records, OfferingMigrationCatalog.ScreenOptions, createdUtc, feeRequiredLevel, bookingRequiredLevel);
    }

    /// <summary>完整重讀兩來源及類別映射；所有SDK實體轉成無個資的短生命週期純值。</summary>
    public IReadOnlyList<OfferingSourceRecord> ReadSourceRecords()
    {
        ThrowIfDisposed();
        if (backupOnly) throw new InvalidOperationException("Backup-only store cannot read migration records.");
        var sourceEntities = new List<Entity>(MaximumRows);
        var contacts = new HashSet<Guid>();
        foreach (var source in Sources)
        {
            var columns = new List<string> { source.OptionAttribute, source.ContactAttribute, CategoryLookup };
            var sourceIndex = Array.IndexOf(Sources, source);
            if (directChurchAttributes[sourceIndex] is { } churchAttribute) columns.Add(churchAttribute);
            var rows = ReadRows(source.Entity, columns.ToArray(), MaximumRows - sourceEntities.Count);
            foreach (var row in rows)
            {
                if (row.GetAttributeValue<EntityReference>(source.ContactAttribute) is { } contact)
                {
                    if (contact.LogicalName != "contact" || contact.Id == Guid.Empty)
                        throw new InvalidDataException("Offering source contact lookup is invalid.");
                    contacts.Add(contact.Id);
                }
                sourceEntities.Add(row);
                if (sourceEntities.Count > MaximumRows) throw new InvalidDataException("Offering source exceeds its bounded budget.");
            }
        }

        var contactChurches = ReadContactChurches(contacts);
        var categoryChurches = ReadCategoryChurches();
        var output = new List<OfferingSourceRecord>(sourceEntities.Count);
        for (var index = 0; index < Sources.Length; index++)
        {
            var source = Sources[index];
            var churchAttribute = directChurchAttributes[index];
            foreach (var row in sourceEntities.Where(item => item.LogicalName == source.Entity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var legacyValue = ReadOptionValue(row, source.OptionAttribute);
                var payer = row.GetAttributeValue<EntityReference>(source.ContactAttribute);
                Guid? churchId = null;
                if (churchAttribute is not null && row.GetAttributeValue<EntityReference>(churchAttribute) is { } directChurch)
                {
                    if (directChurch.LogicalName != "account" || directChurch.Id == Guid.Empty)
                        throw new InvalidDataException("Offering source direct church lookup is invalid.");
                    churchId = directChurch.Id;
                }
                else if (payer is not null && contactChurches.TryGetValue(payer.Id, out var payerChurch))
                {
                    churchId = payerChurch;
                }

                Guid? categoryId = null;
                Guid? categoryChurchId = null;
                if (row.Attributes.TryGetValue(CategoryLookup, out var categoryRaw))
                {
                    if (categoryRaw is not EntityReference category || category.LogicalName != CategoryTable || category.Id == Guid.Empty
                        || !categoryChurches.TryGetValue(category.Id, out var owningChurch))
                        throw new InvalidDataException("Offering source category lookup is invalid or unresolved.");
                    categoryId = category.Id;
                    categoryChurchId = owningChurch;
                }
                output.Add(new OfferingSourceRecord(row.Id, source.Entity, churchId, legacyValue, categoryId, categoryChurchId));
            }
        }
        return Array.AsReadOnly(output.ToArray());
    }

    /// <summary>
    /// 在新lookup尚未部署前讀取可能回填來源的最小身份；未設定舊選項或無法判定教會的列不會被備份，
    /// 因為遷移不會回填這些列。查詢不讀名稱、付款內容或任何其他聯絡人欄位。
    /// </summary>
    /// <returns>唯讀且以來源實體及正式資料庫ID唯一識別的復原清冊。</returns>
    internal IReadOnlyList<OfferingMigrationBackupEntry> ReadBackupRecords()
    {
        ThrowIfDisposed();
        if (!backupOnly) throw new InvalidOperationException("Migration store cannot create pre-migration backups.");
        var candidates = new List<(int SourceIndex, Entity Row, int Option)>();
        var contacts = new HashSet<Guid>();
        foreach (var source in Sources)
        {
            var sourceIndex = Array.IndexOf(Sources, source);
            var columns = new List<string> { source.OptionAttribute, source.ContactAttribute };
            if (directChurchAttributes[sourceIndex] is { } churchAttribute) columns.Add(churchAttribute);
            foreach (var row in ReadRows(source.Entity, columns.ToArray(), MaximumRows - candidates.Count))
            {
                var option = ReadOptionValue(row, source.OptionAttribute);
                if (option is null) continue;
                var payer = row.GetAttributeValue<EntityReference>(source.ContactAttribute);
                if (payer is not null)
                {
                    if (payer.LogicalName != "contact" || payer.Id == Guid.Empty)
                        throw new InvalidDataException("Offering backup contact lookup is invalid.");
                    contacts.Add(payer.Id);
                }
                candidates.Add((sourceIndex, row, option.Value));
                if (candidates.Count > MaximumRows)
                    throw new InvalidDataException("Offering backup exceeds its bounded row budget.");
            }
        }

        var contactChurches = ReadContactChurches(contacts);
        var backup = new List<OfferingMigrationBackupEntry>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid? churchId = null;
            var churchAttribute = directChurchAttributes[candidate.SourceIndex];
            if (churchAttribute is not null
                && candidate.Row.GetAttributeValue<EntityReference>(churchAttribute) is { } directChurch)
            {
                if (directChurch.LogicalName != "account" || directChurch.Id == Guid.Empty)
                    throw new InvalidDataException("Offering backup direct church lookup is invalid.");
                churchId = directChurch.Id;
            }
            else if (candidate.Row.GetAttributeValue<EntityReference>(
                         Sources[candidate.SourceIndex].ContactAttribute) is { } payer
                     && contactChurches.TryGetValue(payer.Id, out var payerChurch))
            {
                churchId = payerChurch;
            }
            if (churchId is { } resolvedChurch)
                backup.Add(new OfferingMigrationBackupEntry(
                    Sources[candidate.SourceIndex].Entity, candidate.Row.Id, resolvedChurch));
        }
        return Array.AsReadOnly(backup.ToArray());
    }

    /// <summary>使用正式替代索引鍵Upsert；只有Dataverse回傳的RecordId能用於後續業務列lookup。</summary>
    public Guid UpsertCategory(OfferingCategoryPlan category)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var targetEntity = new Entity(CategoryTable);
        targetEntity.KeyAttributes["new_accountid"] = new EntityReference("account", category.ChurchId);
        targetEntity.KeyAttributes["new_code"] = category.Code;
        targetEntity["new_accountid"] = new EntityReference("account", category.ChurchId);
        targetEntity["new_code"] = category.Code;
        targetEntity["new_name"] = category.Name;
        targetEntity["new_sortorder"] = category.SortOrder;
        if (category.LegacyOptionValue is { } optionValue)
            targetEntity["new_legacyoptionvalue"] = optionValue;
        var response = Execute<UpsertResponse>(new UpsertRequest { Target = targetEntity });
        cancellationToken.ThrowIfCancellationRequested();
        return response.Target is { LogicalName: CategoryTable, Id: var categoryId } && categoryId != Guid.Empty
            ? categoryId
            : throw new InvalidDataException("Offering category Upsert returned no database identity.");
    }

    /// <summary>透過ExecuteMultiple每批更新至多100列並驗證所有RequestIndex及逐筆Response/Fault。</summary>
    public IReadOnlyList<OfferingUpdateResult> ExecuteMultiple(IReadOnlyList<OfferingLookupUpdate> updates)
    {
        ThrowIfDisposed();
        if (updates.Count is < 1 or > BatchSize) throw new InvalidDataException("Offering update batch exceeds its bounded size.");
        var request = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
            Requests = new OrganizationRequestCollection()
        };
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (update.Entity is not ("new_fee" or "new_dedication_booking")
                || update.RecordId == Guid.Empty || update.ChurchId == Guid.Empty || update.CategoryId == Guid.Empty)
                throw new InvalidDataException("Offering update identity is invalid.");
            var targetEntity = new Entity(update.Entity, update.RecordId)
            {
                ["new_offeringcategoryid"] = new EntityReference(CategoryTable, update.CategoryId)
            };
            request.Requests.Add(new UpdateRequest { Target = targetEntity });
        }
        var response = Execute<ExecuteMultipleResponse>(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Responses is null || response.Responses.Count != updates.Count)
            throw new InvalidDataException("ExecuteMultiple response count is incomplete.");
        var results = new OfferingUpdateResult[updates.Count];
        var seen = new bool[updates.Count];
        foreach (var item in response.Responses)
        {
            if (item is null || item.RequestIndex < 0 || item.RequestIndex >= updates.Count || seen[item.RequestIndex])
                throw new InvalidDataException("ExecuteMultiple response identity is invalid.");
            seen[item.RequestIndex] = true;
            if (item.Fault is { } fault)
                results[item.RequestIndex] = new OfferingUpdateResult(false,
                    "0x" + fault.ErrorCode.ToString("X8", CultureInfo.InvariantCulture));
            else if (item.Response is UpdateResponse)
                results[item.RequestIndex] = new OfferingUpdateResult(true, "");
            else
                throw new InvalidDataException("ExecuteMultiple item has no complete outcome.");
        }
        if (seen.Any(value => !value)) throw new InvalidDataException("ExecuteMultiple response has missing items.");
        return Array.AsReadOnly(results);
    }

    /// <summary>store不建立外部資源；Dispose讓後續誤用明確失敗並清除本命令保留的SDK參照。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        service = null;
    }

    /// <summary>完整讀取account正式ID，用作畫面預設類別的教會集合；不讀取或輸出display name。</summary>
    private System.Collections.ObjectModel.ReadOnlyCollection<Guid> ReadChurchIds()
    {
        var rows = ReadRows("account", Array.Empty<string>(), MaximumRows);
        return Array.AsReadOnly(rows.Select(row => row.Id).ToArray());
    }

    /// <summary>對source付款人contact分500筆查詢；每個contact唯一且僅複製account parent ID。</summary>
    private Dictionary<Guid, Guid> ReadContactChurches(HashSet<Guid> contactIds)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var batch in contactIds.OrderBy(id => id).Chunk(500))
        {
            var rows = ReadRows("contact", ContactColumns, batch.Length,
                "contactid", batch.Cast<object>().ToArray());
            foreach (var row in rows)
            {
                var parent = row.GetAttributeValue<EntityReference>("parentcustomerid");
                if (parent is null || parent.LogicalName != "account" || parent.Id == Guid.Empty) continue;
                if (!result.TryAdd(row.Id, parent.Id)) throw new InvalidDataException("Offering payer identity is duplicated.");
            }
        }
        return result;
    }

    /// <summary>建立正式類別ID到教會ID的完整有界映射，供verify檢出跨教會引用。</summary>
    private Dictionary<Guid, Guid> ReadCategoryChurches()
    {
        var rows = ReadRows(CategoryTable, CategoryColumns, MaximumRows);
        var result = new Dictionary<Guid, Guid>();
        foreach (var row in rows)
        {
            var church = row.GetAttributeValue<EntityReference>("new_accountid");
            if (church is null || church.LogicalName != "account" || church.Id == Guid.Empty
                || !result.TryAdd(row.Id, church.Id))
                throw new InvalidDataException("Offering category identity is incomplete or duplicated.");
        }
        return result;
    }

    /// <summary>有序完整分頁讀取固定實體，壞cookie、重複ID、超量或取消均不發布部分列。</summary>
    private List<Entity> ReadRows(string entity, string[] columns, int maximumRows,
        string? filterAttribute = null, object[]? filterValues = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(columns),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 }
        };
        query.AddOrder(entity + "id", OrderType.Ascending);
        if (filterAttribute is not null)
        {
            if (filterValues is null || filterValues.Length is < 1 or > 500)
                throw new InvalidDataException("Offering query filter exceeds its bounded identity set.");
            query.Criteria.AddCondition(filterAttribute, ConditionOperator.In, filterValues);
        }
        var output = new List<Entity>();
        var identities = new HashSet<Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= MaximumPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            query.PageInfo.PageNumber = page;
            var response = Service.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (response.Entities.Count > PageSize || output.Count + response.Entities.Count > maximumRows
                || response.MoreRecords && response.Entities.Count == 0)
                throw new InvalidDataException("Offering query is incomplete or exceeds its row budget.");
            foreach (var row in response.Entities)
            {
                if (row is null || row.LogicalName != entity || row.Id == Guid.Empty || !identities.Add(row.Id))
                    throw new InvalidDataException("Offering query contains an invalid or duplicate database identity.");
                output.Add(row);
            }
            if (!response.MoreRecords) return output;
            var cookie = response.PagingCookie;
            if (string.IsNullOrEmpty(cookie) || cookie.Length > 65536 || !cookies.Add(cookie))
                throw new InvalidDataException("Offering query paging continuation is invalid.");
            query.PageInfo.PagingCookie = cookie;
        }
        throw new InvalidDataException("Offering query exceeds its bounded page count.");
    }

    /// <summary>取得choice與lookup結構；published狀態可供migration只讀應用正式已部署欄位。</summary>
    private EntityMetadata ReadEntityMetadata(string entity)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = Execute<RetrieveEntityResponse>(new RetrieveEntityRequest
        {
            LogicalName = entity,
            EntityFilters = EntityFilters.Attributes,
            RetrieveAsIfPublished = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (response.EntityMetadata is not { } metadata || metadata.LogicalName != entity || metadata.Attributes is null)
            throw new InvalidDataException("Offering entity metadata is incomplete.");
        return metadata;
    }

    /// <summary>以專用SDK訊息明確取得已發布alternate key，不假設一般entity metadata會附帶Keys集合。</summary>
    private EntityKeyMetadata ReadCategoryKeyMetadata()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = Execute<RetrieveEntityKeyResponse>(new RetrieveEntityKeyRequest
        {
            EntityLogicalName = CategoryTable,
            LogicalName = "new_ak_offeringcategory",
            RetrieveAsIfPublished = false
        });
        var key = response.EntityKeyMetadata;
        if (key is null || key.LogicalName != "new_ak_offeringcategory"
            || key.KeyAttributes?.ToHashSet(StringComparer.Ordinal).SetEquals(CategoryAlternateKey) != true)
            throw new InvalidDataException("Offering category alternate key metadata is incomplete.");
        return key;
    }

    /// <summary>兩舊欄位必須是局部choice；每個值需有唯一1028標籤，RequiredLevel未知時明確保留Unknown。</summary>
    private static System.Collections.ObjectModel.ReadOnlyCollection<(int Value, string Label)> ReadOptions(
        EntityMetadata entity, string attribute, out string requiredLevel)
    {
        var metadata = entity.Attributes.SingleOrDefault(item => item.LogicalName == attribute);
        if (metadata is not PicklistAttributeMetadata picklist || picklist.OptionSet?.Options is not { Count: > 0 and <= 256 } choices)
            throw new InvalidDataException("Offering option metadata is missing or has an unsupported type.");
        requiredLevel = metadata.RequiredLevel?.Value.ToString() ?? "Unknown";
        var options = new List<(int Value, string Label)>(choices.Count);
        var values = new HashSet<int>();
        foreach (var choice in choices)
        {
            var value = choice?.Value;
            var labels = choice?.Label?.LocalizedLabels?.Where(item => item.LanguageCode == OrganizationLanguage).ToArray();
            if (value is null || !values.Add(value.Value) || labels is not { Length: 1 }
                || string.IsNullOrWhiteSpace(labels[0].Label) || labels[0].Label.Length > 300
                || labels[0].Label.Any(char.IsControl))
                throw new InvalidDataException("Offering choice value or 1028 label is invalid.");
            options.Add((value.Value, labels[0].Label));
        }
        return Array.AsReadOnly(options.OrderBy(item => item.Value).ToArray());
    }

    /// <summary>
    /// 合併兩個來源各自的choice值；不同欄位允許有不同值，但同一數值的繁中標籤若衝突必須拒絕，
    /// 避免依任意來源覆蓋顯示名稱。各欄位RequiredLevel仍由plan分別保存，不以類別投影取代。
    /// </summary>
    private static System.Collections.ObjectModel.ReadOnlyCollection<OfferingOption> MergeOptionSets(
        System.Collections.ObjectModel.ReadOnlyCollection<(int Value, string Label)> fee,
        System.Collections.ObjectModel.ReadOnlyCollection<(int Value, string Label)> booking,
        string feeLevel, string bookingLevel)
    {
        var merged = new Dictionary<int, OfferingOption>(fee.Count + booking.Count);
        foreach (var option in fee)
            merged.Add(option.Value, new OfferingOption(option.Value, option.Label, feeLevel));
        foreach (var option in booking)
        {
            if (merged.TryGetValue(option.Value, out var existing))
            {
                if (!string.Equals(existing.Label, option.Label, StringComparison.Ordinal))
                    throw new InvalidDataException("Offering source option labels conflict for a shared value.");
                continue;
            }
            merged.Add(option.Value, new OfferingOption(option.Value, option.Label, bookingLevel));
        }
        return Array.AsReadOnly(merged.Values.OrderBy(option => option.Value).ToArray());
    }

    /// <summary>確認舊選項、付款人lookup與新增category lookup型別／目標，不依查閱顯示名推測。</summary>
    private static void ValidateSourceMetadata(EntityMetadata metadata, SourceDefinition source,
        bool requireCategoryLookup = true)
    {
        var option = metadata.Attributes.SingleOrDefault(item => item.LogicalName == source.OptionAttribute);
        var payer = metadata.Attributes.SingleOrDefault(item => item.LogicalName == source.ContactAttribute);
        var category = metadata.Attributes.SingleOrDefault(item => item.LogicalName == CategoryLookup);
        if (option is not PicklistAttributeMetadata
            || payer is not LookupAttributeMetadata payerLookup || !payerLookup.Targets.Contains("contact", StringComparer.Ordinal)
            || requireCategoryLookup && (category is not LookupAttributeMetadata categoryLookup
                || !categoryLookup.Targets.Contains(CategoryTable, StringComparer.Ordinal)))
            throw new InvalidDataException("Offering source metadata does not match the migration contract.");
    }

    /// <summary>只有精確new_accountid且metadata明確指向account時視為直接教會欄；其他關聯不拿來推斷教會。</summary>
    private static string? ReadOptionalDirectChurchAttribute(EntityMetadata metadata)
    {
        var direct = metadata.Attributes.SingleOrDefault(item => item.LogicalName == "new_accountid");
        if (direct is null) return null;
        if (direct is not LookupAttributeMetadata lookup || !lookup.Targets.Contains("account", StringComparer.Ordinal))
            throw new InvalidDataException("Offering direct church attribute does not target account.");
        return "new_accountid";
    }

    /// <summary>確認category主欄、唯一鍵組成與教會lookup存在，阻止對未建立或不相容結構執行遷移。</summary>
    private static void ValidateCategoryMetadata(EntityMetadata metadata, EntityKeyMetadata key)
    {
        var names = metadata.Attributes.Select(attribute => attribute.LogicalName).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("new_accountid") || !names.Contains("new_code") || !names.Contains("new_name")
            || !names.Contains("new_sortorder") || !names.Contains("new_legacyoptionvalue")
            || key.LogicalName != "new_ak_offeringcategory"
            || key.KeyAttributes?.ToHashSet(StringComparer.Ordinal).SetEquals(CategoryAlternateKey) != true)
            throw new InvalidDataException("Offering category metadata or alternate key is incomplete.");
    }

    /// <summary>OPTION欄只接受OptionSetValue，不從raw integer或formatted文字猜正式值。</summary>
    private static int? ReadOptionValue(Entity row, string attribute) =>
        row.Attributes.TryGetValue(attribute, out var raw)
            ? raw is OptionSetValue option ? option.Value : throw new InvalidDataException("Offering option value has an invalid SDK type.")
            : null;

    /// <summary>同步SDK訊息前後檢查取消並核對強型別回應，錯誤仍保留原始SDK例外。</summary>
    private T Execute<T>(OrganizationRequest request) where T : OrganizationResponse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = Service.Execute(request);
        cancellationToken.ThrowIfCancellationRequested();
        return response as T ?? throw new InvalidDataException("Offering metadata request returned an unexpected response.");
    }

    /// <summary>Disposed後拒絕保留或重用已結束命令的SDK參照。</summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>只在store有效期間借用CLI的SDK服務；Dispose後清空欄位，避免操作結果延長lease內物件存活。</summary>
    private IOrganizationService Service =>
        service ?? throw new ObjectDisposedException(nameof(OfferingMigrationSdkStore));

    /// <summary>固定來源metadata欄位名稱，不接受caller提供實體或查詢欄位。</summary>
    private sealed record SourceDefinition(string Entity, string OptionAttribute, string ContactAttribute);
}
