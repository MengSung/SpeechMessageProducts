/*
 * 檔案整體說明
 *
 * 目的與定位：正規化多類別奉獻明細的純函式工具。 它只做裁切、移除完全空白列及舊欄位回退，不負責判斷金額上限、 類別是否存在或付款方式限制；那些規則由 SubmissionService 統一處理。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationLineItemNormalizer。
 * - 一次奉獻最多幾個類別。每個類別建立一張收費單，所有收費單 Id 都要放進永豐 Param1（X(255)）， 因此上限直接等於 DonationFeeIdList.MaxIds（7 張）；要調高必須先改 Param1 
 * 的編碼方式。
 * 主要操作入口包含 Normalize。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationFeeIdListTests.cs、
 * ChurchReport.MemberInfo.Tests/Dedication/DonationMultiCategoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationGivingHistory.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs、
 * SpeechMessageProducts.ChurchReport/Views/Dedication/DonationPaymentView.cshtml 追查目前工作樹中的合作／使用位置。
 */
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
