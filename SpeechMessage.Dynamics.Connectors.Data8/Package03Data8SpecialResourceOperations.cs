// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package03Data8SpecialResourceOperations.cs
// 用途：在 Data8 connector request scope 內實作 P7.3 五項固定 image、metadata 與 meeting capability。
//
// 信任與生命週期邊界：
// 1. 此檔案是 CRM SDK Entity、QueryExpression、RetrieveAttributeRequest、paging cookie 與 image byte[] 的唯一
//    owner；ProductClient/Gateway 不得傳入 FetchXML、raw metadata、stream、IFormFile 或 caller-selected schema。
// 2. service 由 OnPremiseData8ConnectorClient 擁有並由外層 lease 在成功、fault、取消或 drain 時 Dispose；本類別
//    不得 Dispose service。每個 page、Entity、cookie、temporary byte copy 和 HashSet 只活在同步 method scope。
// 3. 任一 cancellation、read-back mismatch、超限、schema drift 或 metadata/image validation failure 都丟出例外，
//    使外層 lease faulted 並淘汰 client；絕不回傳 partial response 或自動重試 write。
// ============================================================================

using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// P7.3 特殊資源 capability 的 Data8 implementation。所有 CRM logical name、column、filter、sorting、
/// metadata target 與 response size policy 都是 private server-owned detail；不提供 generic CRM operation。
/// </summary>
internal static class Package03Data8SpecialResourceOperations
{
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string ContactImageAttribute = "entityimage";
    private const string MeetingStatisticEntityName = "new_meeting_statistics";
    private const string MeetingStatisticIdAttribute = "new_meeting_statisticsid";
    private const string MeetingStatisticNameAttribute = "new_name";
    private const string MeetingStatisticCreatedOnAttribute = "createdon";
    private const string MeetingStatisticSundayDateAttribute = "new_sunday_date";
    private const string StateCodeAttribute = "statecode";
    private const int MaximumImageBytes = 32 * 1024;
    private const int MaximumImageWidthPixels = 2048;
    private const int MaximumImageHeightPixels = 2048;
    private const long MaximumImagePixels = 2_097_152;
    private const int MaximumMeetingRowsPerPage = 128;
    private const int MaximumMetadataLabelCharacters = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 依 operation ID 執行一個 P7.3 fixed capability。只有 CE 9.1 已核准本機 contract；CE 8.2 或未知 operation
    /// 都在 service 前 fail closed。此同步 method 不保存 operation/service/response 到 static state。
    /// </summary>
    internal static Package03Data8OperationResult Execute(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(ceVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 special-resource CE version is not supported.");
        }

        int? serverResolvedLocale = null;
        OperationResponseData data;
        if (string.Equals(operation.OperationId, OperationIds.MetadataOptionSetByAttribute, StringComparison.Ordinal))
        {
            data = ExecuteRetrieveOptionSet(service, operation, ceVersion, out serverResolvedLocale);
        }
        else
        {
            data = operation.OperationId switch
            {
                OperationIds.MemberInfoContactRetrieveImage => ExecuteRetrieveContactImage(service, operation, ceVersion),
                OperationIds.MemberInfoContactUpdateImage => ExecuteUpdateContactImage(service, operation, ceVersion),
                OperationIds.NewPersonContactUpdateImage => ExecuteUpdateContactImage(service, operation, ceVersion),
                OperationIds.StatsMeetingRetrieveBySunday => ExecuteRetrieveMeetingStatistics(
                    service,
                    operation,
                    ceVersion,
                    cancellationToken),
                _ => throw new InvalidOperationException("The Data8 special-resource operation is not permitted.")
            };
        }
        return new Package03Data8OperationResult(
            data,
            serverResolvedLocale);
    }

    /// <summary>
    /// 讀取固定 contact.entityimage。僅投影已複製 bytes 和 closed media kind；空/錯誤 Entity、缺 image、未知
    /// signature 或超限都 fail closed，不以 null、raw stream 或 format guess 向產品回傳。
    /// </summary>
    private static OperationResponseData ExecuteRetrieveContactImage(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var entity = service.Retrieve(ContactEntityName, contactId, new ColumnSet(ContactImageAttribute));
        var image = ReadRequiredContactImage(entity, contactId);
        return OperationResponseData.ForContactImage(operation.OperationId, ceVersion, image);
    }

    /// <summary>
    /// 更新固定 contact.entityimage 並立即精確 read-back。payload 已由 executor defensive copy；此處再複製作為
    /// Entity owner，更新後才讀回。任何 transport/CRM exception、取消或 bytes/media mismatch 都讓外層 fault lease，
    /// 不能宣稱 mutation 成功、不能重送。
    /// </summary>
    private static OperationResponseData ExecuteUpdateContactImage(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var payload = ReadRequiredImagePayload(operation.Parameters);
        var requestedBytes = payload.GetImageBytes();
        ValidateImagePayload(requestedBytes, payload.MediaKind);

        var update = new Entity(ContactEntityName, contactId)
        {
            [ContactImageAttribute] = requestedBytes.ToArray()
        };
        service.Update(update);

        var readBack = ReadRequiredContactImage(
            service.Retrieve(ContactEntityName, contactId, new ColumnSet(ContactImageAttribute)),
            contactId);
        if (readBack.MediaKind != payload.MediaKind ||
            !readBack.GetImageBytes().AsSpan().SequenceEqual(requestedBytes))
        {
            throw new InvalidOperationException("The Data8 contact image read-back did not match.");
        }

        return OperationResponseData.ForContactImageUpdate(
            operation.OperationId,
            ceVersion,
            ContactImageUpdateDisposition.Changed,
            ContactImageUpdateCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 執行唯一允許的 metadata target。target enum 先被 executor 正規化；此處仍採 switch 建構固定
    /// RetrieveAttributeRequest，避免任何 entity/attribute/locale string 或 raw metadata request 由產品進入 SDK。
    /// </summary>
    private static OperationResponseData ExecuteRetrieveOptionSet(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion,
        out int? serverResolvedLocale)
    {
        serverResolvedLocale = null;
        if (!operation.Parameters.TryGetValue("target", out var value) ||
            value is not MetadataOptionSetTarget.ContactCustomerTypeCode)
        {
            throw new InvalidOperationException("The Data8 metadata target is invalid.");
        }

        var response = service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = ContactEntityName,
            LogicalName = "customertypecode",
            RetrieveAsIfPublished = true
        }) as RetrieveAttributeResponse
            ?? throw new InvalidOperationException("The Data8 metadata response is invalid.");
        if (response.AttributeMetadata is not PicklistAttributeMetadata { OptionSet.Options: { } options })
        {
            throw new InvalidOperationException("The Data8 metadata attribute is not an OptionSet.");
        }

        var records = new List<OptionSetOptionRecord>(options.Count);
        var values = new HashSet<int>();
        var serverLocaleIsConsistent = true;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var optionValue = option.Value;
            var localizedLabel = GetRequestLocalizedLabel(option, out var hasServerResolvedLocale);
            var label = localizedLabel?.Label;
            if (optionValue is null || string.IsNullOrWhiteSpace(label) ||
                label.Length > MaximumMetadataLabelCharacters || !values.Add(optionValue.Value))
            {
                throw new InvalidOperationException("The Data8 metadata option is invalid.");
            }

            try
            {
                _ = StrictUtf8.GetByteCount(label);
            }
            catch (EncoderFallbackException)
            {
                throw new InvalidOperationException("The Data8 metadata option label is invalid.");
            }

            records.Add(new OptionSetOptionRecord
            {
                Value = optionValue.Value,
                Label = label,
                ConfiguredOrder = index
            });

            // CRM 為每個 option 提供的 UserLocalizedLabel.LanguageCode 是唯一可接受的 cache locale authority。
            // 缺值、非正或同一 response 的不一致表示本次 UI label 沒有單一可證實 locale；仍可安全回傳已投影
            // 的 request-local DTO，但不可建立長生命週期 cache entry，更不可用 OS/HTTP/caller locale 猜補。
            if (!hasServerResolvedLocale || localizedLabel is not { LanguageCode: > 0 } serverLocalizedLabel)
            {
                serverLocaleIsConsistent = false;
            }
            else if (serverResolvedLocale is null)
            {
                serverResolvedLocale = serverLocalizedLabel.LanguageCode;
            }
            else if (serverResolvedLocale.Value != serverLocalizedLabel.LanguageCode)
            {
                serverLocaleIsConsistent = false;
            }
        }

        if (!serverLocaleIsConsistent)
        {
            serverResolvedLocale = null;
        }

        return OperationResponseData.ForOptionSetOptions(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 從 CRM metadata 取得本次 request 的唯一顯示文字，並區分能否證實 server-resolved locale。當
    /// <c>UserLocalizedLabel</c> 存在時它是唯一 authority，即使完整 <c>LocalizedLabels</c> collection 含多筆
    /// 翻譯也不可依集合順序覆寫。若 convenience property 缺失但完整集合恰一筆，該筆仍可作本次 request-local
    /// projection，但 <paramref name="hasServerResolvedLocale"/> 為 false，禁止 cache；多筆或空集合則無法安全選擇。
    /// 本方法不讀 process culture、HTTP header 或 caller locale，回傳的 <c>LocalizedLabel</c> 只活在 connector scope。
    /// </summary>
    private static LocalizedLabel? GetRequestLocalizedLabel(
        OptionMetadata option,
        out bool hasServerResolvedLocale)
    {
        hasServerResolvedLocale = false;
        var label = option.Label;
        if (label?.UserLocalizedLabel is { } serverSelectedLabel)
        {
            hasServerResolvedLocale = true;
            return serverSelectedLabel;
        }

        var localizedLabels = label?.LocalizedLabels;
        return localizedLabels is { Count: 1 } ? localizedLabels[0] : null;
    }

    /// <summary>
    /// 依 UTC Sunday 執行固定 meeting statistic QueryExpression。每個 page 的 EntityCollection/cookie 只活在 loop；
    /// MoreRecords 缺 cookie、超頁/超列/超 bytes、欄位型別不符或 cancellation（由外層在前後檢查）都中止而不回傳 partial。
    /// </summary>
    private static OperationResponseData ExecuteRetrieveMeetingStatistics(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion,
        CancellationToken cancellationToken)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.MeetingStatistics);
        var sunday = ReadRequiredUtcSunday(operation.Parameters, "sundayDate");
        var query = CreateMeetingStatisticsQuery(sunday);
        var records = new List<MeetingStatisticRecord>(MaximumMeetingRowsPerPage);
        var totalBytes = 0;
        string? pagingCookie = null;
        for (var pageNumber = 1; pageNumber <= definition.MaximumPageCount; pageNumber++)
        {
            // IOrganizationService 是同步 transport，無法把 token 傳給 SDK；因此在每次可能產生下一頁 I/O
            // 前後明確觀察 request-owned cancellation。token 不被保存、註冊或連結，取消後不讀取下一頁、更不回傳
            // 已投影的 partial records，外層 lease 會將不確定的 client 淘汰並由唯一 owner dispose。
            cancellationToken.ThrowIfCancellationRequested();
            var pageBytes = 0;
            query.PageInfo.PageNumber = pageNumber;
            query.PageInfo.PagingCookie = pagingCookie;
            var page = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The Data8 meeting statistics page is invalid.");
            cancellationToken.ThrowIfCancellationRequested();
            if (page.Entities.Count > MaximumMeetingRowsPerPage ||
                checked(records.Count + page.Entities.Count) > definition.MaximumResultItemCount)
            {
                throw new InvalidOperationException("The Data8 meeting statistics result exceeded its limit.");
            }

            foreach (var entity in page.Entities)
            {
                var record = ProjectMeetingStatistic(entity);
                if (!TryAddMeetingStatisticBytes(ref pageBytes, record, definition.MaximumPageBytes) ||
                    !TryAddMeetingStatisticBytes(ref totalBytes, record, definition.MaximumCumulativeResponseBytes))
                {
                    throw new InvalidOperationException("The Data8 meeting statistics response exceeded its budget.");
                }

                records.Add(record);
            }

            if (!page.MoreRecords)
            {
                return OperationResponseData.ForMeetingStatistics(operation.OperationId, ceVersion, records);
            }

            if (pageNumber == definition.MaximumPageCount || string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                throw new InvalidOperationException("The Data8 meeting statistics paging contract is invalid.");
            }

            pagingCookie = page.PagingCookie;
        }

        throw new InvalidOperationException("The Data8 meeting statistics page limit was exceeded.");
    }

    /// <summary>
    /// 建立 server-owned meeting statistic query。只選取 legacy method 已證實的 ID/name/createdOn/Sunday 欄位，
    /// 固定 active filter、Sunday equality 與 deterministic createdOn/ID order；產品無法插入 FetchXML 或改寫欄位。
    /// </summary>
    private static QueryExpression CreateMeetingStatisticsQuery(DateTimeOffset sunday)
    {
        var query = new QueryExpression(MeetingStatisticEntityName)
        {
            ColumnSet = new ColumnSet(
                MeetingStatisticIdAttribute,
                MeetingStatisticNameAttribute,
                MeetingStatisticCreatedOnAttribute,
                MeetingStatisticSundayDateAttribute),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo
            {
                Count = MaximumMeetingRowsPerPage,
                PageNumber = 1
            }
        };
        query.Criteria.AddCondition(StateCodeAttribute, ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(MeetingStatisticSundayDateAttribute, ConditionOperator.On, sunday.UtcDateTime);
        query.AddOrder(MeetingStatisticCreatedOnAttribute, OrderType.Descending);
        query.AddOrder(MeetingStatisticIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 將單一 meeting Entity 投影為純值。所有 CRM SDK object 都在 method 結束時失去參考；缺欄可投影為 null，
    /// 已存在欄位卻是錯誤 CLR 型別則 fail closed，避免 `ToString` 將未知 SDK graph 帶出 connector boundary。
    /// </summary>
    private static MeetingStatisticRecord ProjectMeetingStatistic(Entity entity)
    {
        if (entity is null || !string.Equals(entity.LogicalName, MeetingStatisticEntityName, StringComparison.Ordinal) ||
            entity.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 meeting statistic entity is invalid.");
        }

        return new MeetingStatisticRecord
        {
            MeetingStatisticId = entity.Id,
            Name = ReadOptionalString(entity, MeetingStatisticNameAttribute),
            CreatedOn = ReadOptionalUtcDateTime(entity, MeetingStatisticCreatedOnAttribute),
            SundayDate = ReadOptionalUtcDateTime(entity, MeetingStatisticSundayDateAttribute)
        };
    }

    /// <summary>
    /// 將 contact Entity 的唯一 entityimage 讀為封閉 payload。影像 bytes 先複製，避免 CRM Entity.Attributes 的
    /// mutable array 留到 response；缺 image、未知 signature、無法由 ImageSharp 識別的內容，以及超出格式、尺寸、
    /// 像素或 byte 上限的內容都不能偽裝成功。解碼器與暫存格式資訊只活在本方法呼叫範圍，絕不留在 response 或 client。
    /// </summary>
    private static ContactImageResponseData ReadRequiredContactImage(Entity entity, Guid expectedContactId)
    {
        if (entity is null || !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != expectedContactId || !entity.Attributes.TryGetValue(ContactImageAttribute, out var value) ||
            value is not byte[] source)
        {
            throw new InvalidOperationException("The Data8 contact image response is invalid.");
        }

        var bytes = source.ToArray();
        var mediaKind = DetectMediaKind(bytes);
        ValidateImagePayload(bytes, mediaKind);
        return new ContactImageResponseData(bytes, mediaKind);
    }

    /// <summary>
    /// 讀取 executor-owned image payload。getter 產生新 copy，connector 再驗 bytes/format；不接受 byte[] 直接傳入，
    /// 避免繞過 closed media kind 和跨層 defensive-copy boundary。
    /// </summary>
    private static ContactImageResponseData ReadRequiredImagePayload(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("imagePayload", out var value) || value is not ContactImageResponseData payload)
        {
            throw new InvalidOperationException("The Data8 image payload is invalid.");
        }

        var copy = payload.GetImageBytes();
        return new ContactImageResponseData(copy, payload.MediaKind);
    }

    /// <summary>
    /// 驗證固定 PNG/JPEG signature、實際 decoder format、payload byte ceiling、寬高與總像素。任何無法完整識別、
    /// 格式不符或超限資料都在 CRM 寫入／產品回應前 fail closed；byte[]、decoder 與 format 物件不會被保存。P7.4
    /// cutover 前 consumer 仍維持 disabled，這些本機驗證不構成 CE evidence。
    /// </summary>
    private static void ValidateImagePayload(byte[] bytes, ContactImageMediaKind mediaKind)
    {
        if (bytes is null || bytes.Length is < 1 or > MaximumImageBytes || !Enum.IsDefined(mediaKind))
        {
            throw new InvalidOperationException("The Data8 image payload is not permitted.");
        }

        try
        {
            var decodedFormat = Image.DetectFormat(bytes);
            var info = Image.Identify(bytes);
            if (info is null || decodedFormat is null ||
                !TryMapDecodedImageFormat(decodedFormat, out var decodedMediaKind) ||
                decodedMediaKind != mediaKind ||
                info.Width is < 1 or > MaximumImageWidthPixels ||
                info.Height is < 1 or > MaximumImageHeightPixels ||
                checked((long)info.Width * info.Height) > MaximumImagePixels)
            {
                throw new InvalidOperationException("The Data8 image payload is not permitted.");
            }
        }
        catch (UnknownImageFormatException exception)
        {
            throw new InvalidOperationException("The Data8 image payload is not permitted.", exception);
        }
        catch (InvalidImageContentException exception)
        {
            throw new InvalidOperationException("The Data8 image payload is not permitted.", exception);
        }
        catch (ImageFormatException exception)
        {
            throw new InvalidOperationException("The Data8 image payload is not permitted.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The Data8 image payload is not permitted.", exception);
        }
    }

    /// <summary>
    /// 依最小 magic bytes 判斷允許格式。這不是 MIME/副檔名 trust；未知或過短資料都立即拒絕。實際 decode/dimension
    /// guard 需在具備受控 decoder dependency 與相稱 CE/host evidence 的後續深化工作完成前保持 fail closed。
    /// </summary>
    private static ContactImageMediaKind DetectMediaKind(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ContactImageMediaKind.Png;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ContactImageMediaKind.Jpeg;
        }

        throw new InvalidOperationException("The Data8 image media format is invalid.");
    }

    /// <summary>
    /// 將 ImageSharp decoder 讀出的格式限制在 P7.3 合約的 PNG/JPEG。任何 library 新增格式、檔名或 MIME 都
    /// 不會自動擴張允許集合；若要支援新格式，必須同步變更 closed DTO、Gateway normalizer、容量 policy 和
    /// negative tests。此短生命週期 format 物件不會寫入 Data8 client、pool、cache 或 product response。
    /// </summary>
    /// <param name="decodedFormat">已完成 Identify 的 decoder format。</param>
    /// <param name="mediaKind">成功時輸出的 P7.3 封閉 enum。</param>
    /// <returns>只在格式是 PNG/JPEG 時為 <see langword="true"/>。</returns>
    private static bool TryMapDecodedImageFormat(IImageFormat decodedFormat, out ContactImageMediaKind mediaKind)
    {
        ArgumentNullException.ThrowIfNull(decodedFormat);
        if (string.Equals(decodedFormat.Name, "PNG", StringComparison.OrdinalIgnoreCase))
        {
            mediaKind = ContactImageMediaKind.Png;
            return true;
        }

        if (string.Equals(decodedFormat.Name, "JPEG", StringComparison.OrdinalIgnoreCase))
        {
            mediaKind = ContactImageMediaKind.Jpeg;
            return true;
        }

        mediaKind = default;
        return false;
    }


    /// <summary>讀取必填非空 Guid，失敗在任何固定 CRM request 前中止。</summary>
    private static Guid ReadRequiredGuid(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not Guid guid || guid == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 special-resource GUID is invalid.");
        }

        return guid;
    }

    /// <summary>讀取必填 UTC Sunday 00:00，禁止 host local time、partial date 或 arbitrary date range 進入固定 query。</summary>
    private static DateTimeOffset ReadRequiredUtcSunday(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not DateTimeOffset dateTime)
        {
            throw new InvalidOperationException("The Data8 meeting Sunday is invalid.");
        }

        var utc = dateTime.ToUniversalTime();
        if (utc.Offset != TimeSpan.Zero || utc.TimeOfDay != TimeSpan.Zero || utc.DayOfWeek != DayOfWeek.Sunday)
        {
            throw new InvalidOperationException("The Data8 meeting Sunday is invalid.");
        }

        return utc;
    }

    /// <summary>取得 immutable registry definition 並確認 response branch；registry declaration 不可取代 connector implementation。</summary>
    private static OperationDefinition GetDefinition(string operationId, OperationResponseKind expectedResponseKind)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) || definition is null ||
            definition.ResponseKind != expectedResponseKind)
        {
            throw new InvalidOperationException("The Data8 special-resource definition is invalid.");
        }

        return definition;
    }

    /// <summary>精確讀取 optional string，不用 ToString fallback，避免未知 SDK object/metadata graph 越過投影邊界。</summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? throw new InvalidOperationException("The Data8 meeting attribute type is invalid.");
    }

    /// <summary>將 CRM DateTime 轉成 UTC pure value；Unspecified 視為 UTC，避免主機時區/DST 改變同一 Sunday result。</summary>
    private static DateTimeOffset? ReadOptionalUtcDateTime(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        if (value is not DateTime dateTime)
        {
            throw new InvalidOperationException("The Data8 meeting date attribute type is invalid.");
        }

        var utc = dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
        return new DateTimeOffset(utc).ToUniversalTime();
    }

    /// <summary>以 checked 和嚴格 UTF-8 累積 meeting row 的保守序列化大小，超限不產生 partial success。</summary>
    private static bool TryAddMeetingStatisticBytes(ref int totalBytes, MeetingStatisticRecord record, int maximumBytes)
    {
        if (!TryAddBytes(ref totalBytes, 64, maximumBytes))
        {
            return false;
        }

        if (record.Name is null)
        {
            return true;
        }

        try
        {
            return TryAddBytes(ref totalBytes, StrictUtf8.GetByteCount(record.Name), maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>以 checked arithmetic 累積 bytes，overflow/limit failure 都回傳 false 給 caller 的 fail-closed path。</summary>
    private static bool TryAddBytes(ref int totalBytes, int additionalBytes, int maximumBytes)
    {
        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return totalBytes <= maximumBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

/// <summary>
/// P7.3 connector request scope 的特殊資源投影結果。<see cref="Data"/> 是唯一可交給 executor 的封閉產品 envelope；
/// <see cref="ServerResolvedMetadataLocale"/> 僅在 metadata operation 由 CRM <c>UserLocalizedLabel.LanguageCode</c>
/// 完整證實時存在，供 executor 於同一已解析 profile/generation 中建立 private cache key。此型別不持有
/// Entity、LocalizedLabel、request、service、lease、credential、cookie 或 stream，不能序列化或傳遞給產品。
/// </summary>
internal sealed record Package03Data8OperationResult(
    OperationResponseData Data,
    int? ServerResolvedMetadataLocale);
