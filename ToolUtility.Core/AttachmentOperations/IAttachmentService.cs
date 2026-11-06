/*
 * 檔案整體說明
 *
 * 目的與定位：既有同步附件契約；host 必須先驗證每次 SDK／parent 的組織及使用者權限。 ref 只是來源相容，不轉移 SDK 所有權；實作不得保存請求資料或自行建立連線、背景工作與重試。
 *
 * 主要功能與流程：
 * 本檔宣告 IAttachmentService。
 * - 讀取一頁 annotation 並完整驗證正式識別、parent 及容量後才發布；同名不同 ID 全部保留。 不解碼本文，不把缺本文的文字附註誤判失敗；分頁與 SDK 逾時／排空由 host 控制。
 * - 驗 raw 長度後寫入一次七欄附註，合法空 bytes 保留；故障／未確認 ID 不回報成功或隱藏重試。 固定訊息包裝保留真 InnerException，真正失敗交 host 共用通知邊界，不在 Core 
 * 記錄內容或憑證。
 * 資料／選項欄位包含 IAttachmentService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Core/AttachmentOperations/AttachmentService.cs 追查目前工作樹中的合作／使用位置。
 */
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
