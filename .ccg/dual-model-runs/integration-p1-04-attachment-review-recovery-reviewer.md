# CCG reviewer Task: integration-p1-04-attachment-review-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
請唯讀以reviewer審查P1-04 Attachment切片，繁體中文輸出Critical/High/Warning/Info。只看本文安全快照，禁止探索Facade原碼、任何設定／憑證或其他工作樹。原host附件兩檔已確認不含機密，本文完整提供。Task沿用.ccg/tasks/integration-p1，目前P1仍2/12，整包未完成。
分析200333 dualModelComplete=true。3特性原host綠；安全28紅/2綠；Base64補測13紅/1綠；修後搬前50/50才git mv兩原檔至Core，新limits另建。搬後產品net462/net10；測試net48實載net462 410/410，net10 509/509；Core strict0警告錯誤，coverage每目標恰一份99.48%/100%。完整方案00§8另在執行，不預判。
請核對：原namespace不可換Interfaces、ref SDK不改不Dispose、舊ctor參數拒絕但不保存SDK/logger；保持7欄、null metadata、空bytes、annotation query string parent、只單頁。新差異明載：ID/根表/parent/容量/合法base64及Create空ID fail closed。固定英文wrapper保留同一原InnerException；Core非最終host通知邊界，不引入新logger/notifier/LINE。
limits預設5MiB raw、32Mi字元/頁、5000列，是本地預算非D365實測上限。拒絕分析「舊ctor保留無界」建議，舊ctor也套有限預設。掃描Base64不解碼，容許.NET標準4空白；驗padding/確切rawbytes/long計數。SDK已反序列化配置不能事後撤銷，host仍需限制transport。重用EntityQueryService公開完整驗頁，不複製internal邏輯。
1000 A/B同服務不同ref SDK、fault/retry/partial拒絕、caller mutation、ctor弱參照、borrowed Dispose=0均有斷言；四案實際ToolUtilityClass→Facade→lazy→Core，正常公開ctor只傳合成SDK/空設定/無輸出tracer，不連服務。不得宣稱授權、實機、全部host路徑。沒有需求變更或修改正式計畫。
請核對相容/隔離/有界資源/故障處理/驗證盲區/繁中XML及strict。對有效Critical/High/Warning給可重現場景，不把已列明安全差異視為未授權、不要求全專案重寫。測試基準數已載明，勿泛稱全方案零warning或100%所有target。

## 原host ToolUtility/AttachmentOperations/AttachmentService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AttachmentOperations/AttachmentService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class AttachmentService
// 主要成員：DownloadAttachment、UploadAttachment
// 引用命名空間：System、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    /// <summary>
    /// 附件操作服務 - 處理 CRM 附件的上傳和下載
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public AttachmentService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 下載附件 - 根據實體 ID 查詢所有關聯的附註（annotation）
        /// </summary>
        public EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId)
        {
            try
            {
                // 建立查詢條件：查詢 objectid 等於指定實體 ID 的所有附註
                ConditionExpression condition = new ConditionExpression
                {
                    AttributeName = "objectid",
                    Operator = ConditionOperator.Equal
                };
                condition.Values.Add(entityId.ToString());

                // 建立過濾器
                FilterExpression filter = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And
                };
                filter.Conditions.Add(condition);

                // 建立查詢表達式
                QueryExpression query = new QueryExpression
                {
                    EntityName = "annotation",
                    ColumnSet = new ColumnSet(true),
                    Criteria = filter
                };

                // 執行查詢
                return crmService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: DownloadAttachment failed for entity {entityId}. Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// 上傳附件 - 創建新的附註（annotation）實體並附加到指定實體
        /// </summary>
        public void UploadAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
        {
            try
            {
                // 將文件內容轉換為 Base64 編碼
                string encodedData = Convert.ToBase64String(documentBody);

                // 創建附註實體
                Entity annotation = new Entity("annotation");
                annotation.Attributes["objectid"] = new EntityReference(entityName, toBeAttachedEntityId);
                annotation.Attributes["objecttypecode"] = entityName;
                annotation.Attributes["subject"] = subject;
                annotation.Attributes["notetext"] = noteText;
                annotation.Attributes["documentbody"] = encodedData;
                annotation.Attributes["mimetype"] = mimeType;
                annotation.Attributes["filename"] = fileName;

                // 創建附註
                crmService.Create(annotation);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: UploadAttachment failed for entity {entityName} ({toBeAttachedEntityId}). Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }
    }
}

```

## 原host ToolUtility/AttachmentOperations/IAttachmentService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AttachmentOperations/IAttachmentService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IAttachmentService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    public interface IAttachmentService
    {
        EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId);

        void UploadAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId);
    }
}

```

## ToolUtility.Core/AttachmentOperations/AttachmentService.cs
```
#nullable enable
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    /// <summary>
    /// 同步附註附件操作；只借用每次 ref 傳入、由 host 先完成授權的 SDK。
    /// 服務只保存不可變容量，不保存建構時 SDK／logger、候選、本文或使用者資料，故可並行服務不同 scope。
    /// </summary>
    /// <remarks>
    /// ref 只為舊來源相容，不替換或 Dispose SDK；host 擁有連線、逾時與排空，呼叫期間不得更換其參照。
    /// 所有查詢與候選只屬於本次操作，完整識別、parent 與容量驗證後才回傳，不按名稱去重。
    /// 不建立連線、快取、背景工作或重試；真正故障以固定訊息包裝且保留原 InnerException，交 host 共用告警邊界。
    /// 容量是本地安全預算而非已查證的組織上限；SDK 已反序列化的配置不能事後撤銷，host 仍須約束傳輸。
    /// </remarks>
    public class AttachmentService : IAttachmentService
    {
        private readonly AttachmentTransferLimits _limits;

        /// <summary>
        /// 保留舊簽章及 null 拒絕，啟用有界預設；不保存或處置兩個相容參數，避免延長 request／連線生命。
        /// 方法實際使用的 SDK 仍由各次 ref 明確借入，不能回退使用此建構參數。
        /// </summary>
        /// <param name="logger">不可為 null 的舊相容參數，不呼叫或保存。</param>
        /// <param name="organizationService">不可為 null 的舊相容參數，不呼叫或保存。</param>
        public AttachmentService(object logger, IOrganizationService organizationService)
            : this(logger, organizationService, new AttachmentTransferLimits())
        {
        }

        /// <summary>設定不可變的單檔／單頁安全預算；設定由 host 擁有，可並行讀取且不包含租戶資料。</summary>
        /// <param name="logger">只為相容保留，不可為 null，不保存或 Dispose。</param>
        /// <param name="organizationService">只為相容保留，不可為 null，不保存或 Dispose。</param>
        /// <param name="limits">不可為 null 的有限預算；調整前須由 host 核對組織政策與資源承受度。</param>
        public AttachmentService(object logger, IOrganizationService organizationService, AttachmentTransferLimits limits)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
#if NETFRAMEWORK
            if (organizationService == null) throw new ArgumentNullException(nameof(organizationService));
#else
            ArgumentNullException.ThrowIfNull(organizationService);
#endif
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        /// <summary>
        /// 保留 annotation AllColumns、objectid 字串條件與單頁分頁語義；不擅自抓完所有頁面。
        /// 重用 Core 的正式 ID／根表／重複識別驗證，再驗 parent 與容量，任何一列失敗則整頁不發布。
        /// 缺少或 null documentbody 是合法文字附註；不補空檔、不解碼複製、不裁剪原結果。
        /// </summary>
        /// <param name="crmService">本次已授權的借入 SDK；不更換、不接管生命週期。</param>
        /// <param name="entityId">已授權 parent 的非空正式資料庫 ID，ID 本身不構成授權。</param>
        /// <returns>caller 獨占的完整單頁，MoreRecords／PagingCookie 原樣保留，後續分頁由 caller 擁有。</returns>
        public EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId)
        {
            try
            {
#if NETFRAMEWORK
                if (crmService == null) throw new ArgumentNullException(nameof(crmService));
#else
                ArgumentNullException.ThrowIfNull(crmService);
#endif
                if (entityId == Guid.Empty) throw new ArgumentException("A parent database identity is required.", nameof(entityId));
                var condition = new ConditionExpression("objectid", ConditionOperator.Equal, entityId.ToString());
                var filter = new FilterExpression(LogicalOperator.And);
                filter.Conditions.Add(condition);
                var query = new QueryExpression("annotation") { ColumnSet = new ColumnSet(true), Criteria = filter };
                // 本次 using 僅清除查詢轉接器借用參照；不處置外部 SDK，也不把 Dispose 義務加給舊 caller。
                using var reader = new EntityQueryService(organizationService: crmService);
                var candidate = reader.RetrieveMultiple(query);
                if (candidate.Entities.Count > _limits.MaximumRows)
                    throw new InvalidOperationException("The attachment page exceeds the row budget.");
                long encodedCharacters = 0;
                foreach (var row in candidate.Entities)
                {
                    if (!row.Attributes.TryGetValue("objectid", out var parentValue)
                        || parentValue is not EntityReference parent || parent.Id != entityId
                        || string.IsNullOrWhiteSpace(parent.LogicalName))
                        throw new InvalidOperationException("The attachment parent is invalid.");
                    if (!row.Attributes.TryGetValue("documentbody", out var bodyValue) || bodyValue == null) continue;
                    if (bodyValue is not string body)
                        throw new InvalidOperationException("The attachment body is invalid.");
                    encodedCharacters += body.Length;
                    if (encodedCharacters > _limits.MaximumPageEncodedCharacters)
                        throw new InvalidOperationException("The attachment page exceeds the encoded character budget.");
                    ValidateBody(body);
                }
                return candidate;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The attachments could not be downloaded.", ex);
            }
        }

        /// <summary>
        /// 保留原七欄、合法空 byte[] 及 metadata 原值；先驗 raw 長度再配置 Base64，僅執行一次 Create。
        /// SDK 必須確認正式非空 ID；空 ID 或故障代表結果不確定，不盲目重試以免建立重複附註。
        /// </summary>
        /// <param name="crmService">本次已授權且仍存活的借入 SDK，不替換或 Dispose。</param>
        /// <param name="entityName">host 已授權 parent 的 logical name，非空名稱不是授權依據。</param>
        /// <param name="subject">原主旨，可為 null；不記錄、不正規化。</param>
        /// <param name="noteText">原附註，可為 null；不保存至服務。</param>
        /// <param name="fileName">原檔名，可為 null；不在本機組合檔案路徑。</param>
        /// <param name="mimeType">原 MIME，可為 null；不自行推測或轉換檔案內容。</param>
        /// <param name="documentBody">caller 擁有且執行中不得修改的 bytes；null 拒絕，空陣列合法。</param>
        /// <param name="toBeAttachedEntityId">同一授權 scope 的非空 parent 正式資料庫 ID。</param>
        public void UploadAttachment(ref IOrganizationService crmService, string entityName, string? subject, string? noteText,
            string? fileName, string? mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
        {
            try
            {
#if NETFRAMEWORK
                if (crmService == null) throw new ArgumentNullException(nameof(crmService));
#else
                ArgumentNullException.ThrowIfNull(crmService);
#endif
                if (string.IsNullOrWhiteSpace(entityName)) throw new ArgumentException("A parent entity name is required.", nameof(entityName));
                if (toBeAttachedEntityId == Guid.Empty) throw new ArgumentException("A parent database identity is required.", nameof(toBeAttachedEntityId));
#if NETFRAMEWORK
                if (documentBody == null) throw new ArgumentNullException(nameof(documentBody));
#else
                ArgumentNullException.ThrowIfNull(documentBody);
#endif
                if (documentBody.Length > _limits.MaximumAttachmentBytes)
                    throw new ArgumentOutOfRangeException(nameof(documentBody), "The attachment exceeds the byte budget.");
                var annotation = new Entity("annotation")
                {
                    ["objectid"] = new EntityReference(entityName, toBeAttachedEntityId),
                    ["objecttypecode"] = entityName,
                    ["subject"] = subject,
                    ["notetext"] = noteText,
                    ["documentbody"] = Convert.ToBase64String(documentBody),
                    ["mimetype"] = mimeType,
                    ["filename"] = fileName
                };
                if (crmService.Create(annotation) == Guid.Empty)
                    throw new InvalidOperationException("The SDK did not confirm an attachment database identity.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("The attachment could not be uploaded.", ex);
            }
        }

        /// <summary>
        /// 線性掃描既有字串而不建立解碼副本，驗證 Base64 結構及確切 raw bytes；不能只比較編碼長度。
        /// 允許 Convert.FromBase64String 接受的空白並保留原字串；padding 只能在尾端且至多兩個。
        /// 計數使用 long，int 最大設定也不溢位；aggregate 字元預算已先限制每次掃描的成本。
        /// </summary>
        /// <param name="body">本次候選的既有字串，不保存、改寫或解碼。</param>
        private void ValidateBody(string body)
        {
            long symbols = 0;
            var padding = 0;
            foreach (var character in body)
            {
                if (character == ' ' || character == '\t' || character == '\r' || character == '\n') continue;
                symbols++;
                if (character == '=')
                {
                    if (++padding > 2) throw new InvalidOperationException("The attachment Base64 padding is invalid.");
                }
                else if (padding != 0 || !((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9') || character == '+' || character == '/'))
                    throw new InvalidOperationException("The attachment Base64 data is invalid.");
            }
            if (symbols % 4 != 0 || symbols / 4 * 3 - padding > _limits.MaximumAttachmentBytes)
                throw new InvalidOperationException("The attachment body exceeds the byte budget or has invalid Base64 length.");
        }
    }
}

```

## ToolUtility.Core/AttachmentOperations/IAttachmentService.cs
```
#nullable enable
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    /// <summary>
    /// 既有同步附件契約；host 必須先驗證每次 SDK／parent 的組織及使用者權限。
    /// ref 只是來源相容，不轉移 SDK 所有權；實作不得保存請求資料或自行建立連線、背景工作與重試。
    /// </summary>
    public interface IAttachmentService
    {
        /// <summary>
        /// 讀取一頁 annotation 並完整驗證正式識別、parent 及容量後才發布；同名不同 ID 全部保留。
        /// 不解碼本文，不把缺本文的文字附註誤判失敗；分頁與 SDK 逾時／排空由 host 控制。
        /// </summary>
        /// <param name="crmService">本次已授權的借入 SDK，不替換或 Dispose。</param>
        /// <param name="entityId">已授權 parent 的正式非空資料庫 ID，不是授權憑據。</param>
        /// <returns>caller 擁有的完整單頁；不保存至共用狀態或回傳部分候選。</returns>
        EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId);

        /// <summary>
        /// 驗 raw 長度後寫入一次七欄附註，合法空 bytes 保留；故障／未確認 ID 不回報成功或隱藏重試。
        /// 固定訊息包裝保留真 InnerException，真正失敗交 host 共用通知邊界，不在 Core 記錄內容或憑證。
        /// </summary>
        /// <param name="crmService">本次已授權的借入 SDK；host 等同步操作排空後才 Dispose。</param>
        /// <param name="entityName">同一 scope 的 parent logical name，不能取代授權。</param>
        /// <param name="subject">原主旨，含 null 原樣傳遞且不保存。</param>
        /// <param name="noteText">原附註，含 null 原樣傳遞且不記錄。</param>
        /// <param name="fileName">原檔名，含 null 原樣傳遞，不組成本機路徑。</param>
        /// <param name="mimeType">原 MIME，含 null 原樣傳遞，不轉換內容。</param>
        /// <param name="documentBody">操作中不可改寫的 caller-owned bytes；null 拒絕，空值合法。</param>
        /// <param name="toBeAttachedEntityId">已授權 parent 的正式非空資料庫 ID。</param>
        void UploadAttachment(ref IOrganizationService crmService, string entityName, string? subject, string? noteText,
            string? fileName, string? mimeType, byte[] documentBody, Guid toBeAttachedEntityId);
    }
}

```

## ToolUtility.Core/AttachmentOperations/AttachmentTransferLimits.cs
```
using System;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    /// <summary>
    /// 不含使用者／租戶狀態的不可變附件安全預算；預設單檔 5 MiB、單頁 32 Mi 字元及 5000 列。
    /// 這是本地資源界線而非 D365 組織上限，host 可按經核實的政策顯式提供更小或更大正值。
    /// 設定只含三個有限整數，沒有 disposable、快取或需排空的工作，可安全跨操作共用。
    /// </summary>
    public sealed class AttachmentTransferLimits
    {
        /// <summary>單檔解碼後位元組上限；上傳於 Base64 配置與 I/O 前檢查，下載只掃描不配置解碼陣列。</summary>
        public int MaximumAttachmentBytes { get; }

        /// <summary>一頁所有非 null documentbody 的字元總上限，包含 Base64 空白，禁止裁剪後冒充完整成功。</summary>
        public int MaximumPageEncodedCharacters { get; }

        /// <summary>一次回應的最大列數，文字附註也計入；超限整頁拒絕，不自行無界分頁。</summary>
        public int MaximumRows { get; }

        /// <summary>所有值必須為正，沒有無界或停用限制的 sentinel；由服務只讀保存而不複製請求狀態。</summary>
        /// <param name="maximumAttachmentBytes">單檔 raw bytes 上限，預設 5 MiB。</param>
        /// <param name="maximumPageEncodedCharacters">整頁編碼字元上限，預設 32 Mi 字元。</param>
        /// <param name="maximumRows">單頁列數上限，預設 5000。</param>
        public AttachmentTransferLimits(int maximumAttachmentBytes = 5 * 1024 * 1024,
            int maximumPageEncodedCharacters = 32 * 1024 * 1024, int maximumRows = 5000)
        {
#if NETFRAMEWORK
            if (maximumAttachmentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumAttachmentBytes));
            if (maximumPageEncodedCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPageEncodedCharacters));
            if (maximumRows <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRows));
#else
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAttachmentBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPageEncodedCharacters);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
#endif
            MaximumAttachmentBytes = maximumAttachmentBytes;
            MaximumPageEncodedCharacters = maximumPageEncodedCharacters;
            MaximumRows = maximumRows;
        }
    }
}

```

## ToolUtility.Tests/DataOperations/AttachmentCharacterizationTests.cs
```
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AttachmentOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在原 host 鎖定兩個附件入口的完整 SDK 查詢／七欄寫入及 ref 參照語義。
    /// 全部內容為合成資料，不連 D365、不讀設定，也不把正常特性與新增安全拒絕混為一談。
    /// </summary>
    public sealed class AttachmentCharacterizationTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid FileId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>
        /// 下載保留 annotation AllColumns、objectid 的字串值型別及完整分頁；
        /// 純文字附註與同名不同 ID 都返回，實際只借用 ref SDK，不呼叫或替換 ctor SDK。
        /// </summary>
        [Fact]
        public void Download_PreservesGoldenQueryPageAndRefSdk()
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection { MoreRecords = true, PagingCookie = "synthetic-next", TotalRecordCount = 3 };
            var note = new Entity("annotation", NoteId)
                { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名" };
            var file = new Entity("annotation", FileId)
                { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名", ["documentbody"] = "AAEC" };
            page.Entities.Add(note);
            page.Entities.Add(file);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Callback<QueryBase>(input =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                var condition = Assert.Single(query.Criteria.Conditions);
                var xml = new XElement("query", new XAttribute("entity", query.EntityName),
                    new XAttribute("all-columns", query.ColumnSet.AllColumns), new XAttribute("columns", query.ColumnSet.Columns.Count),
                    new XAttribute("criteria", query.Criteria.FilterOperator), new XAttribute("orders", query.Orders.Count),
                    new XAttribute("links", query.LinkEntities.Count), new XAttribute("top", query.TopCount?.ToString() ?? "none"),
                    new XElement("condition", new XAttribute("field", condition.AttributeName),
                        new XAttribute("operator", condition.Operator),
                        new XAttribute("type", Assert.Single(condition.Values).GetType().FullName!),
                        condition.Values[0]));
                GoldenQueryAssertions.Equal("attachment-download.xml", xml.ToString());
            }).Returns(page);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var borrowed = sdk.Object;
            var result = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Same(sdk.Object, borrowed);
            Assert.Same(page, result);
            Assert.Same(note, result.Entities[0]);
            Assert.Same(file, result.Entities[1]);
            Assert.False(note.Contains("documentbody"));
            Assert.Equal("AAEC", file["documentbody"]);
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-next", result.PagingCookie);
            Assert.Equal(3, result.TotalRecordCount);
            ctorSdk.VerifyNoOtherCalls();
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 空檔案保持空 Base64；非空檔案及特殊文字完整原樣寫入七欄，不作 XML／檔名或MIME內容轉換。
        /// 黃金檔保護實際 SDK Entity，而非只驗證模擬服務有被呼叫。
        /// </summary>
        /// <param name="empty">是否使用合法零位元組檔案。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Upload_PreservesGoldenSevenFieldsAndRefSdk(bool empty)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var bytes = empty ? Array.Empty<byte>() : new byte[] { 0, 1, 2, 127, 255 };
            var original = bytes.ToArray();
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Callback<Entity>(entity =>
            {
                Assert.Equal("annotation", entity.LogicalName);
                Assert.Equal(Guid.Empty, entity.Id);
                Assert.Equal(7, entity.Attributes.Count);
                var parent = Assert.IsType<EntityReference>(entity["objectid"]);
                var xml = new XElement("annotation",
                    new XElement("objectid", new XAttribute("name", parent.LogicalName), new XAttribute("id", parent.Id)));
                foreach (var key in new[] { "objecttypecode", "subject", "notetext", "documentbody", "mimetype", "filename" })
                    xml.Add(new XElement(key, entity[key]));
                GoldenQueryAssertions.Equal(empty ? "attachment-upload-empty.xml" : "attachment-upload-bytes.xml", xml.ToString());
                Assert.Equal(bytes, Convert.FromBase64String(Assert.IsType<string>(entity["documentbody"])));
            }).Returns(NoteId);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var borrowed = sdk.Object;
            service.UploadAttachment(ref borrowed, "contact", "合成<&\"", "附註<&\"", "合成<&\".bin",
                "application/x-synthetic", bytes, ParentId);
            Assert.Same(sdk.Object, borrowed);
            Assert.Equal(original, bytes);
            ctorSdk.VerifyNoOtherCalls();
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }
    }
}

```

## ToolUtility.Tests/DataOperations/AttachmentSafetyTests.cs
```
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttachmentOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 附件固定錯誤、正式識別／parent、容量與生命週期的安全契約。
    /// 所有資料為合成值；只替代 SDK I/O，不讀組織容量或假稱這是 D365 實機／授權證據。
    /// </summary>
    public sealed class AttachmentSafetyTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OtherId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>SDK自身參數例外或逾時均保存同一InnerException，固定外層不回顯名稱、ID、時間或原訊息。</summary>
        /// <param name="upload">是否測上傳。</param>
        /// <param name="argument">是否注入SDK自身參數例外。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void SdkFailure_PreservesSameInnerAndFixedMessage(bool upload, bool argument)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Exception original = argument ? new ArgumentException("synthetic_private_fault") : new TimeoutException("synthetic_private_fault");
            if (upload) sdk.Setup(s => s.Create(It.IsAny<Entity>())).Throws(original);
            else sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(original);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(service, ref borrowed, upload));
            Assert.Equal(upload ? "The attachment could not be uploaded." : "The attachments could not be downloaded.", error.Message);
            Assert.Same(original, error.InnerException);
            Assert.Same(sdk.Object, borrowed);
            Assert.Single(sdk.Invocations);
        }

        /// <summary>無法確認輸入scope或內容的請求在SDK前拒絕，沿用舊操作包裝但保存真正參數例外。</summary>
        /// <param name="shape">明列的新安全邊界。</param>
        [Theory]
        [InlineData("download-sdk")]
        [InlineData("upload-sdk")]
        [InlineData("download-id")]
        [InlineData("upload-id")]
        [InlineData("upload-name")]
        [InlineData("upload-body")]
        public void InvalidInput_RejectsWithoutSdk(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = shape.EndsWith("sdk", StringComparison.Ordinal) ? null! : sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (shape.StartsWith("download", StringComparison.Ordinal))
                    service.DownloadAttachment(ref borrowed, shape.EndsWith("id", StringComparison.Ordinal) ? Guid.Empty : ParentId);
                else
                    service.UploadAttachment(ref borrowed, shape == "upload-name" ? " " : "contact", "合成", "", "", "",
                        shape == "upload-body" ? null! : Array.Empty<byte>(), shape == "upload-id" ? Guid.Empty : ParentId);
            });
            Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只在完整頁的正式annotation ID與parent都有效時發布；衝突不裁剪、不補值或生成ID。</summary>
        /// <param name="shape">合成SDK損壞回應。</param>
        [Theory]
        [InlineData("null")]
        [InlineData("empty-id")]
        [InlineData("duplicate")]
        [InlineData("wrong-table")]
        [InlineData("missing-parent")]
        [InlineData("wrong-parent")]
        [InlineData("wrong-parent-type")]
        [InlineData("wrong-body-type")]
        public void Download_InvalidCandidate_RejectsWholePage(string shape)
        {
            var page = new EntityCollection(new[] { Row(NoteId), Row(OtherId) });
            var bad = page.Entities[1];
            if (shape == "empty-id") bad.Id = Guid.Empty;
            else if (shape == "duplicate") bad.Id = NoteId;
            else if (shape == "wrong-table") bad.LogicalName = "contact";
            else if (shape == "missing-parent") bad.Attributes.Remove("objectid");
            else if (shape == "wrong-parent") bad["objectid"] = new EntityReference("contact", OtherId);
            else if (shape == "wrong-parent-type") bad["objectid"] = ParentId;
            else if (shape == "wrong-body-type") bad["documentbody"] = new byte[] { 1 };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(shape == "null" ? null! : page);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() =>
                new AttachmentService(new object(), sdk.Object).DownloadAttachment(ref borrowed, ParentId));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>寫入狀態不確定的空Create ID必須拒絕，不盲目重試或回報void成功。</summary>
        [Fact]
        public void Upload_EmptySdkIdentity_RejectsWithoutRetry()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(new AttachmentService(new object(), sdk.Object), ref borrowed, true));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>舊建構式也必須套用預設5MiB界線；在Base64與SDK前拒絕，不能保留無界後門。</summary>
        [Fact]
        public void DefaultUploadLimit_RejectsBeforeSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => service.UploadAttachment(ref borrowed,
                "contact", "", "", "", "", new byte[5 * 1024 * 1024 + 1], ParentId));
            Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 以極小限制實際執行有界API：單檔／單頁本文／列數超界皆整體拒絕，
        /// 恰好在邊界的合法檔案則成功；容量設定不可變，不代表組織實際上限。
        /// </summary>
        /// <param name="shape">容量場景。</param>
        [Theory]
        [InlineData("upload-over")]
        [InlineData("upload-exact")]
        [InlineData("page-total")]
        [InlineData("page-rows")]
        [InlineData("page-single")]
        [InlineData("page-exact")]
        public void ExplicitLimits_EnforceAllBoundaries(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = CreateWithLimits(sdk.Object, 4, shape == "page-exact" ? 16 : 8, 2);
            var borrowed = sdk.Object;
            if (shape.StartsWith("upload", StringComparison.Ordinal))
            {
                sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(NoteId);
                if (shape == "upload-over")
                {
                    var error = Assert.Throws<InvalidOperationException>(() => service.UploadAttachment(ref borrowed,
                        "contact", "", "", "", "", new byte[5], ParentId));
                    Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
                    sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Never);
                }
                else
                {
                    service.UploadAttachment(ref borrowed, "contact", "", "", "", "", new byte[4], ParentId);
                    sdk.Verify(s => s.Create(It.Is<Entity>(e => (string)e["documentbody"] == "AAAAAA==")), Times.Once);
                }
            }
            else
            {
                var page = new EntityCollection(new[] { Row(NoteId, "AAAAAA=="), Row(OtherId, "AAAAAA==") });
                if (shape == "page-rows") page.Entities.Add(Row(ParentId));
                if (shape == "page-single") page.Entities[0]["documentbody"] = "AAAAAAAAAAAA";
                sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
                if (shape == "page-exact") Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
                else Assert.IsType<InvalidOperationException>(Assert.Throws<InvalidOperationException>(() =>
                    service.DownloadAttachment(ref borrowed, ParentId)).InnerException);
                Assert.Equal(shape == "page-rows" ? 3 : 2, page.Entities.Count);
                sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            }
        }

        /// <summary>容量物件只接受正值，getter不得有setter；null設定不能悄悄降級成無界模式。</summary>
        [Fact]
        public void Limits_RejectInvalidConfigurationAndCannotMutate()
        {
            var type = LimitsType();
            foreach (var property in type.GetProperties()) Assert.Null(property.SetMethod);
            foreach (var values in new[] { new[] { 0, 8, 2 }, new[] { 4, 0, 2 }, new[] { 4, 8, 0 } })
            {
                var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => Activator.CreateInstance(type, values.Cast<object>().ToArray()));
                Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
            }
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var constructor = typeof(AttachmentService).GetConstructor(new[] { typeof(object), typeof(IOrganizationService), type });
            Assert.NotNull(constructor);
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => constructor!.Invoke(new object?[] { new object(), sdk.Object, null }));
            Assert.IsType<ArgumentNullException>(error.InnerException);
        }

        /// <summary>
        /// Base64 字元數不能代替解碼長度：四、五、六位元組都可能佔八字元。
        /// 以四位元組限制測試 padding、非法字元與 .NET 接受的空白；只掃描，不配置解碼副本。
        /// </summary>
        /// <param name="body">合成 Base64 或損壞內容。</param>
        /// <param name="accepted">是否應保留完整原字串並發布。</param>
        [Theory]
        [InlineData("AAAAAA==", true)]
        [InlineData("AAAAAAA=", false)]
        [InlineData("AAAAAAAA", false)]
        [InlineData("AA \t\r\nEC", true)]
        [InlineData("", true)]
        [InlineData(" \t\r\n", true)]
        [InlineData("abc", false)]
        [InlineData("A===", false)]
        [InlineData("AA=A", false)]
        [InlineData("AA?=", false)]
        [InlineData("=AAA", false)]
        [InlineData("AA\u00a0==", false)]
        public void Download_Base64PaddingAndRawLength_AreValidated(string body, bool accepted)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var page = new EntityCollection(new[] { Row(NoteId, body) });
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
            var service = CreateWithLimits(sdk.Object, 4, 64, 2);
            var borrowed = sdk.Object;
            if (accepted)
            {
                Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
                Assert.Equal(body, Assert.Single(page.Entities)["documentbody"]);
            }
            else
            {
                var failure = Assert.Throws<InvalidOperationException>(() => service.DownloadAttachment(ref borrowed, ParentId));
                Assert.IsType<InvalidOperationException>(failure.InnerException);
                Assert.Equal(body, Assert.Single(page.Entities)["documentbody"]);
            }
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// null 本文仍是合法文字附註；反之 parent 名稱缺失不能視為有效關聯。
        /// 防止新增 parent 防護誤刪合法附註或只驗 GUID 而接受無表名關聯。
        /// </summary>
        /// <param name="validParent">是否提供完整 parent。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Download_NullBodyAndParentName_RespectPublicationContract(bool validParent)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var row = Row(NoteId);
            row["documentbody"] = null;
            if (!validParent) row["objectid"] = new EntityReference(" ", ParentId);
            var page = new EntityCollection(new[] { row });
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
            var borrowed = sdk.Object;
            var service = new AttachmentService(new object(), sdk.Object);
            if (validParent) Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
            else Assert.IsType<InvalidOperationException>(Assert.Throws<InvalidOperationException>(() =>
                service.DownloadAttachment(ref borrowed, ParentId)).InnerException);
        }

        /// <summary>
        /// 共用無狀態服務但每次借入已授權A/B SDK，1000次操作驗證回應／候選不串資料。
        /// 所有有限Task全await，活動計數回零，ref不改、借入SDK不Dispose；不建立或保存背景工作。
        /// </summary>
        /// <returns>測試擁有且完整排空的有限工作。</returns>
        [Fact]
        public async Task ConcurrentRefScopes_NoCrossTalkAndDrain()
        {
            var active = 0;
            var completed = 0;
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < 2; scope++)
            {
                var parent = scope == 0 ? ParentId : OtherId;
                clients[scope].As<IDisposable>();
                clients[scope].Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryExpression>(input);
                        Assert.Equal(parent.ToString(), Assert.Single(Assert.Single(query.Criteria.Conditions).Values));
                        var row = Row(NoteId);
                        row["objectid"] = new EntityReference("contact", parent);
                        return new EntityCollection(new[] { row });
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
                clients[scope].Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        Assert.Equal(parent, Assert.IsType<EntityReference>(entity["objectid"]).Id);
                        Assert.Equal(parent.ToString(), entity["subject"]);
                        Assert.Equal("AQID", entity["documentbody"]);
                        return NoteId;
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            // Moq 的 Object 首次建立不是受測並行契約；先在單執行緒完成proxy初始化，避免fixture自己競態。
            var sdkObjects = clients.Select(client => client.Object).ToArray();
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var parent = scope == 0 ? ParentId : OtherId;
                var borrowed = sdkObjects[scope];
                var row = Assert.Single(service.DownloadAttachment(ref borrowed, parent).Entities);
                Assert.Equal(parent, Assert.IsType<EntityReference>(row["objectid"]).Id);
                row["subject"] = "caller-mutated";
                service.UploadAttachment(ref borrowed, "contact", parent.ToString(), "", "", "", new byte[] { 1, 2, 3 }, parent);
                Assert.Same(sdkObjects[scope], borrowed);
                Interlocked.Increment(ref completed);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.Equal(1000, Volatile.Read(ref completed));
            Assert.Equal(0, Volatile.Read(ref active));
            ctorSdk.VerifyNoOtherCalls();
            foreach (var client in clients) client.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>
        /// 原SDK故障或頁面組裝部分成功後的錯parent都不得發布；caller明確下一次呼叫才重試。
        /// 成功後caller可修改自己的頁面，第三次SDK結果不受影響；所有借入SDK仍由原owner處置。
        /// </summary>
        [Fact]
        public void FailureRetryAndCallerMutation_DoNotPublishPartialOrRetainResults()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var failure = new TimeoutException("synthetic_private_transport");
            sdk.SetupSequence(s => s.Create(It.IsAny<Entity>())).Throws(failure).Returns(NoteId);
            var partial = new EntityCollection(new[] { Row(NoteId), Row(OtherId) });
            partial.Entities[1]["objectid"] = new EntityReference("contact", OtherId);
            sdk.SetupSequence(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(partial)
                .Returns(new EntityCollection(new[] { Row(NoteId) })).Returns(new EntityCollection(new[] { Row(NoteId) }));
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Invoke(service, ref borrowed, true)).InnerException);
            Invoke(service, ref borrowed, true);
            Assert.Throws<InvalidOperationException>(() => service.DownloadAttachment(ref borrowed, ParentId));
            Assert.Equal(2, partial.Entities.Count);
            var first = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Single(first.Entities)["subject"] = "caller-mutated";
            var next = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Equal("合成同名", Assert.Single(next.Entities)["subject"]);
            Assert.NotSame(first, next);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Exactly(2));
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(3));
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>ctor的logger與未使用SDK不得延長生命；保持產品服務存活時，兩個原輸入都應可回收。</summary>
        [Fact]
        public void Constructor_DoesNotRetainUnusedInputs()
        {
            var service = CreateWithWeakInputs(out var logger, out var sdk);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(logger.IsAlive);
            Assert.False(sdk.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>產生單次合成附註；沒有本文仍是合法附註，不需要補造空檔案。</summary>
        private static Entity Row(Guid id, string? body = null)
        {
            var row = new Entity("annotation", id) { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名" };
            if (body != null) row["documentbody"] = body;
            return row;
        }

        /// <summary>呼叫真實上傳／下載入口，不在fixture模擬錯誤或容量判斷。</summary>
        private static void Invoke(AttachmentService service, ref IOrganizationService sdk, bool upload)
        {
            if (upload) service.UploadAttachment(ref sdk, "contact", "合成", "", "", "", new byte[] { 1 }, ParentId);
            else service.DownloadAttachment(ref sdk, ParentId);
        }

        /// <summary>反射讓新增API不存在時也能執行紅測，而不是把編譯錯誤當作驗收證據。</summary>
        private static Type LimitsType()
        {
            var type = typeof(AttachmentService).Assembly.GetType("ToolUtilityNameSpace.AttachmentOperations.AttachmentTransferLimits");
            Assert.NotNull(type);
            return type!;
        }

        /// <summary>只經公開constructor建立產品，不在fixture覆寫限制或提供測試專用產品入口。</summary>
        private static AttachmentService CreateWithLimits(IOrganizationService sdk, int bytes, int characters, int rows)
        {
            var type = LimitsType();
            var limits = Activator.CreateInstance(type, bytes, characters, rows);
            var constructor = typeof(AttachmentService).GetConstructor(new[] { typeof(object), typeof(IOrganizationService), type });
            Assert.NotNull(constructor);
            return Assert.IsType<AttachmentService>(constructor!.Invoke(new[] { new object(), sdk, limits }));
        }

        /// <summary>隔離JIT區域及Moq局部變數；沒有方法呼叫紀錄會保存SDK。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AttachmentService CreateWithWeakInputs(out WeakReference logger, out WeakReference sdk)
        {
            var log = new object();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            var service = new AttachmentService(log, client);
            logger = new WeakReference(log);
            sdk = new WeakReference(client);
            return service;
        }
    }
}

```

## ToolUtility.Tests/AttachmentOperations/AttachmentCallerRouteTests.cs
```
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Tests.AttachmentOperations
{
    /// <summary>
    /// 實際建立 ToolUtilityClass 並執行公開入口→Facade→Lazy→Core 的完整附件委派鏈。
    /// 僅替代 SDK I/O，使用空設定與無輸出 tracer，不讀憑證、不連線或發 LINE；
    /// 此證據保護跨組件路徑，不冒充 D365 實機或 host 的授權驗收。
    /// </summary>
    public sealed class AttachmentCallerRouteTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 下載必須抵達 ref SDK 而非建構 SDK，正常頁保留；錯 parent 整頁拒絕並保留故障。
        /// using 排空 host 工具後仍不得處置任一借入 SDK，無快取或背景資源殘留。
        /// </summary>
        /// <param name="invalid">是否注入錯 parent。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicDownload_RoutesRefSdkAndRejectsWrongParent(bool invalid)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var ctorDisposable = ctorSdk.As<IDisposable>();
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection(new[]
            {
                new Entity("annotation", NoteId)
                {
                    ["objectid"] = new EntityReference("contact", invalid ? NoteId : ParentId),
                    ["documentbody"] = "AQID"
                }
            });
            sdk.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q =>
                q is QueryExpression && ((QueryExpression)q).EntityName == "annotation"))).Returns(page);
            using (var utility = new ToolUtilityClass(ctorSdk.Object, new NullToolUtilityTracer(),
                new ConfigurationBuilder().Build()))
            {
                var borrowed = sdk.Object;
                if (invalid)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => utility.DownloadAnAttachment(ref borrowed, ParentId));
                    Assert.Equal("The attachments could not be downloaded.", error.Message);
                    Assert.IsType<InvalidOperationException>(error.InnerException);
                }
                else Assert.Same(page, utility.DownloadAnAttachment(ref borrowed, ParentId));
                Assert.Same(sdk.Object, borrowed);
            }
            ctorSdk.VerifyNoOtherCalls();
            ctorDisposable.Verify(d => d.Dispose(), Times.Never);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 上傳在真實公開委派鏈保持七欄及 ref SDK；注入 SDK 參數例外仍保存同一 InnerException。
        /// Dispose 只結束工具自有生命週期，不擁有借入 SDK，不把故障偽裝成功或做隱藏重試。
        /// </summary>
        /// <param name="fault">是否注入 SDK 故障。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicUpload_RoutesSevenFieldsAndPreservesFault(bool fault)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var ctorDisposable = ctorSdk.As<IDisposable>();
            var disposable = sdk.As<IDisposable>();
            var original = new ArgumentException("synthetic_private_fault");
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity row) =>
            {
                Assert.Equal("annotation", row.LogicalName);
                Assert.Equal(7, row.Attributes.Count);
                Assert.Equal(ParentId, Assert.IsType<EntityReference>(row["objectid"]).Id);
                Assert.Equal("AQID", row["documentbody"]);
                if (fault) throw original;
                return NoteId;
            });
            using (var utility = new ToolUtilityClass(ctorSdk.Object, new NullToolUtilityTracer(),
                new ConfigurationBuilder().Build()))
            {
                var borrowed = sdk.Object;
                if (fault)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => utility.UploadAnAttachment(ref borrowed,
                        "contact", "合成", "", "", "", new byte[] { 1, 2, 3 }, ParentId));
                    Assert.Equal("The attachment could not be uploaded.", error.Message);
                    Assert.Same(original, error.InnerException);
                }
                else utility.UploadAnAttachment(ref borrowed, "contact", "合成", "", "", "", new byte[] { 1, 2, 3 }, ParentId);
                Assert.Same(sdk.Object, borrowed);
            }
            ctorSdk.VerifyNoOtherCalls();
            ctorDisposable.Verify(d => d.Dispose(), Times.Never);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }
    }
}

```

## ToolUtility.Tests/CoreTargetFrameworkTests.cs
```
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using ToolUtilityNameSpace.OwnerOperations;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using ToolUtilityNameSpace.Constants;
using ToolUtilityNameSpace.AttachmentOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Same(assembly, typeof(EntityRepository).Assembly);
            Assert.Same(assembly, typeof(IEntityRepository).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Same(assembly, typeof(EntityCrudService).Assembly);
            Assert.Same(assembly, typeof(OwnerManagementService).Assembly);
            Assert.Same(assembly, typeof(EntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(IEntityAttributeUtilityService).Assembly);
            Assert.Same(assembly, typeof(OrganizationServiceExtensions).Assembly);
            Assert.Same(assembly, typeof(CrmEntityColumns).Assembly);
            Assert.Same(assembly, typeof(AttachmentService).Assembly);
            Assert.Same(assembly, typeof(IAttachmentService).Assembly);
            Assert.Same(assembly, typeof(AttachmentTransferLimits).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 實際反射抽取介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 屬性讀取的 attributeName 依舊允許 null，其餘字串必須維持非 null 契約。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        [InlineData(typeof(IEntityCrudService))]
        [InlineData(typeof(IEntityRepository))]
        [InlineData(typeof(IOwnerManagementService))]
        [InlineData(typeof(IEntityAttributeUtilityService))]
        [InlineData(typeof(IAttachmentService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        var expected = (contract == typeof(IEntityAttributeUtilityService) && parameter.Name == "attributeName")
                            || (contract == typeof(IAttachmentService) && parameter.Name != "entityName")
                            ? NullabilityState.Nullable : NullabilityState.NotNull;
                        Assert.Equal(expected, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }
#endif
    }
}

```

## wp-reports/P1-04-attachment.md
```
# P1-04 Attachment 切片（整包未完成）

## 需求 → 實作 → 驗證

|需求|實作位置|測試／證據|狀態|
|---|---|---|---|
|BCL／SDK 操作移至雙目標、原 namespace／API|Core/AttachmentOperations 三型別；兩原檔 git mv|CoreTargetFrameworkTests 實載 net462；兩宿主410／509綠|局部通過|
|先鎖 download query、七欄 upload／合法空檔|AttachmentCharacterizationTests、三份 Golden XML|artifacts/p1-04-attachment-before/before.trx，3/3|通過|
|無效輸入、整頁ID／parent、空Create ID fail closed|AttachmentService；重用 EntityQueryService|AttachmentSafetyTests；28紅/2綠→修後搬前50/50|局部通過|
|單檔／單頁／列數有限，舊建構式也有界|AttachmentTransferLimits；raw5MiB／page32Mi字元／5000列|exact／over／padding、非法Base64、空body；補測13紅/1綠|局部通過|
|不同ref SDK不串、無延長生命／外部Dispose|服務只保存不可變limits|1000 A/B並行、active=0、同InnerException、weak ctor輸入可回收|局部通過|
|真實公開委派鏈|ToolUtilityClass→Facade→lazy→Core|AttachmentCallerRouteTests四案，只替代SDK；空設定／無輸出tracer|局部通過|
|完整00§8、coverage與雙模型複核|本輪獨立全新目錄|完整回歸／coverage／review尚在取得|待補|

## 保留與明列安全差異

- 原 namespace `ToolUtilityNameSpace.AttachmentOperations`，IAttachmentService不換到Interfaces。
  舊二參數建構式與兩個ref入口完整保留，不替換或Dispose SDK；實際只用ref SDK。
  移除未用ctor logger／SDK保存，沒有新IDisposable義務。
- annotation AllColumns、objectid Equal值型別string、單頁、paging及七欄值保留，
  合法空bytes、無／null本文的文字附註合法。同名不同資料庫ID不合併。
- 新安全差異：無效SDK／parent／名稱／nullbytes、錯表／空或重複ID、parent缺失／錯型／錯ID／缺表名，
  超容量或非法Base64整體拒絕；不裁剪、不補ID、不重試狀態不確定的Create。
- 固定英文包裝不含ID、名稱、時間、原訊息；同一真實InnerException上傳host共用通知邊界。
  Core不是最終失敗邊界，不引入未接入notifier／LINE或寫檔。所有SDK、連線、授權、逾時及drain由host擁有。
- limits只接受正int、getter無setter。舊ctor亦套預設，額外三參數ctor允許host顯式調整；
  本地5MiB／32Mi字元／5000列**不是已查證的D365組織上限**，未連線取得組織metadata。
- 下載掃描現有Base64，允許.NET標準四種空白、正確padding；以long算確切rawbytes，
  不用編碼字元數誤放過五／六bytes共用八字元，不配置解碼副本。
  SDK反序列化配置無法事後撤銷；傳輸大小／逾時仍需host限制。Query完整識別先驗，再附件parent／容量，
  候選只屬於此操作，不保存在服務或共享快照。
- 初次紅測29/1含Moq初次proxy建立競態；先單執行緒初始化Object後重跑28/2才列產品紅測。
  首次Core嚴格建置捕捉CA1510/CA1512，按既有Framework條件改ThrowIf API後0警告/錯誤，未放寬檢查。

## 分析、範圍與未結事項

分析run `20261105-200333-integration-p1-04-attachment-analysis-analyzer` 的
summary明確dualModelComplete=true、quotaBlocked=false、degradedFallback=false。
不採分析的「舊ctor可無界」建議；原碼確實保存ctor SDK，不沿用分析錯述為早已丟棄。

這是離線SDK邊界與公開委派驗證，不冒充實機／授權／Hosted CI。
本輪未操作VM、D365、LINE、正式金鑰或部署。正式計畫沒有修改，不需Word修正報告。
P1仍2/12，Activity／其他候選及P1-05～12待完成，不封存主P1。

```

## ToolUtility.Tests\Golden\ToolUtility\attachment-download.xml
```xml
<query entity="annotation" all-columns="true" columns="0" criteria="And" orders="0" links="0" top="none"><condition field="objectid" operator="Equal" type="System.String">11111111-1111-1111-1111-111111111111</condition></query>

```

## ToolUtility.Tests\Golden\ToolUtility\attachment-upload-bytes.xml
```xml
<annotation><objectid name="contact" id="11111111-1111-1111-1111-111111111111" /><objecttypecode>contact</objecttypecode><subject>合成&lt;&amp;"</subject><notetext>附註&lt;&amp;"</notetext><documentbody>AAECf/8=</documentbody><mimetype>application/x-synthetic</mimetype><filename>合成&lt;&amp;".bin</filename></annotation>

```

## ToolUtility.Tests\Golden\ToolUtility\attachment-upload-empty.xml
```xml
<annotation><objectid name="contact" id="11111111-1111-1111-1111-111111111111" /><objecttypecode>contact</objecttypecode><subject>合成&lt;&amp;"</subject><notetext>附註&lt;&amp;"</notetext><documentbody></documentbody><mimetype>application/x-synthetic</mimetype><filename>合成&lt;&amp;".bin</filename></annotation>

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
