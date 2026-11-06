/*
 * 檔案整體說明
 *
 * 目的與定位：同步附註附件操作；只借用每次 ref 傳入、由 host 先完成授權的 SDK。 服務只保存不可變容量，不保存建構時 SDK／logger、候選、本文或使用者資料，故可並行服務不同 scope。
 *
 * 主要功能與流程：
 * 本檔宣告 AttachmentService。
 * - 保留舊簽章及 null 拒絕，啟用有界預設；不保存或處置兩個相容參數，避免延長 request／連線生命。 方法實際使用的 SDK 仍由各次 ref 明確借入，不能回退使用此建構參數。
 * - 設定不可變的單檔／單頁安全預算；設定由 host 擁有，可並行讀取且不包含租戶資料。
 * - 保留 annotation AllColumns、objectid 字串條件與單頁分頁語義；不擅自抓完所有頁面。 重用 Core 的正式 ID／根表／重複識別驗證，再驗 parent 與容量，任何一列失敗則整頁不發布。
 * 缺少或 null documentbody 是合法文字附註；不補空檔、不解碼複製、不裁剪原結果。
 * - 保留原七欄、合法空 byte[] 及 metadata 原值；先驗 raw 長度再配置 Base64，僅執行一次 Create。 SDK 必須確認正式非空 ID；空 ID 或故障代表結果不確定，
 * 不盲目重試以免建立重複附註。
 * 主要操作入口包含 DownloadAttachment、UploadAttachment、ValidateBody。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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
