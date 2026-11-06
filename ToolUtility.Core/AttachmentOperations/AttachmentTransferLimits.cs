/*
 * 檔案整體說明
 *
 * 目的與定位：不含使用者／租戶狀態的不可變附件安全預算；預設單檔 5 MiB、單頁 32 Mi 字元及 5000 列。 這是本地資源界線而非 D365 組織上限，host 可按經核實的政策顯式提供更小或更大正值。 
 * 設定只含三個有限整數，沒有 disposable、快取或需排空的工作，可安全跨操作共用。
 *
 * 主要功能與流程：
 * 本檔宣告 AttachmentTransferLimits。
 * - 單檔解碼後位元組上限；上傳於 Base64 配置與 I/O 前檢查，下載只掃描不配置解碼陣列。
 * - 一頁所有非 null documentbody 的字元總上限，包含 Base64 空白，禁止裁剪後冒充完整成功。
 * - 一次回應的最大列數，文字附註也計入；超限整頁拒絕，不自行無界分頁。
 * - 所有值必須為正，沒有無界或停用限制的 sentinel；由服務只讀保存而不複製請求狀態。
 * 資料／選項欄位包含 AttachmentTransferLimits、MaximumAttachmentBytes、MaximumPageEncodedCharacters、MaximumRows，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Tests/CoreTargetFrameworkTests.cs、ToolUtility.Tests/DataOperations/AttachmentSafetyTests.cs、
 * ToolUtility.Core/AttachmentOperations/AttachmentService.cs 追查目前工作樹中的合作／使用位置。
 */
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
