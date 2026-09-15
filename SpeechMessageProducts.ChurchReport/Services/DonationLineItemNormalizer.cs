// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationLineItemNormalizer.cs
// 所屬區塊：ChurchReport 奉獻付款產品服務層。
// 檔案責任：把表單明細轉成可供驗證、建單及通知使用的乾淨明細快照。
// 主要型別：DonationLineItemNormalizer
// 主要成員：MaxLines、Normalize
// 外部依賴：僅依賴 DonationPaymentFormModel 與 DonationLineItemInput，沒有 CRM、金流、Session 或背景工作。
// 隔離要求：Normalize 每次都建立新的 DTO，不回傳或修改表單內的可變物件，避免跨流程互相污染。
// 生命週期：回傳集合由呼叫端持有至本次流程結束；不使用 static cache，因此不會保留會友資料。
// 相容性：Lines 沒有有效資料時回退至舊 Category／Amount／Others，保護既有後台與舊版呼叫端。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// 正規化多類別奉獻明細的純函式工具。
    /// 它只做裁切、移除完全空白列及舊欄位回退，不負責判斷金額上限、
    /// 類別是否存在或付款方式限制；那些規則由 SubmissionService 統一處理。
    /// </summary>
    public static class DonationLineItemNormalizer
    {
        /// <summary>
        /// 一次奉獻最多幾個類別。每個類別建立一張收費單，所有收費單 Id 都要放進永豐 Param1（X(255)），
        /// 因此上限直接等於 <see cref="DonationFeeIdList.MaxIds"/>（7 張）；要調高必須先改 Param1 的編碼方式。
        /// </summary>
        public const int MaxLines = DonationFeeIdList.MaxIds;

        public static IReadOnlyList<DonationLineItemInput> Normalize(DonationPaymentFormModel model)
        {
            // null 表單可能來自未成功 model binding 的請求；回傳空集合讓上層產生一致的驗證訊息，
            // 不在此處拋出例外，也不讀取任何 Session 或共享狀態。
            if (model == null) return Array.Empty<DonationLineItemInput>();
            var lines = (model.Lines ?? new List<DonationLineItemInput>())
                .Where(x => x != null)
                .Select(x => new DonationLineItemInput
                {
                    Category = (x.Category ?? string.Empty).Trim(),
                    Amount = x.Amount,
                    Others = (x.Others ?? string.Empty).Trim()
                })
                .Where(x => x.Amount != 0 || x.Category.Length > 0)
                .ToList();
            // 只要存在一列有效資料，就以 Lines 為準；不可再混入舊欄位，避免重複建單。
            if (lines.Count > 0) return lines;
            // Lines 全空且舊欄位也未填寫時，代表沒有任何可建單明細。
            if (model.Amount == 0 && string.IsNullOrWhiteSpace(model.Category)) return Array.Empty<DonationLineItemInput>();
            return new[] { new DonationLineItemInput
            {
                Category = (model.Category ?? string.Empty).Trim(), Amount = model.Amount,
                Others = (model.Others ?? string.Empty).Trim()
            }};
        }
    }
}
