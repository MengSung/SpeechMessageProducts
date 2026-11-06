/*
 * 檔案整體說明
 *
 * 目的與定位：永豐訂單 Param1「收費單 Id 清單」的編碼、解析與過濾規則。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeParamMember、DonationFeeIdList。
 * - 多張收費單時每個 Id 的長度（Guid "N" 格式，32 個十六進位字元，大小寫不敏感）。
 * - 多張收費單之間的分隔字元；永豐允許，且不會出現在 Guid 內。
 * - Param1 最多可放入的收費單張數：(255 + 1) / (32 + 1) = 7。
 * - 把收費單 Id 編成 Param1。清單順序就是 callback 的處理順序，第一張是 primary （付款人、LINE 通知、課程報名與信用卡資料都以 primary 為準）。
 * 主要操作入口包含 DonationFeeParamMember、Encode、Parse、SelectGroupMembers。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Dedication/DonationFeeIdListTests.cs、
 * ChurchReport.MemberInfo.Tests/Dedication/DonationMultiCategoryTests.cs、
 * SpeechMessageProducts.ChurchReport/Services/DonationLineItemNormalizer.cs、
 * SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs、
 * SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Fee
 * Management.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace ChurchReport.Services
{
    /// <summary>callback 過濾收費單時使用的最小快照；只有 Id、會友 Id 與信用卡訂單編號，不含姓名或金額。</summary>
    public sealed record DonationFeeParamMember(Guid FeeId, Guid ContactId, string CardOrderNo);

    /// <summary>永豐訂單 Param1「收費單 Id 清單」的編碼、解析與過濾規則。</summary>
    public static class DonationFeeIdList
    {
        /// <summary>永豐規格 Param1 最大長度 X(255)。</summary>
        public const int ProviderParamMaxLength = 255;

        /// <summary>多張收費單時每個 Id 的長度（Guid "N" 格式，32 個十六進位字元，大小寫不敏感）。</summary>
        public const int CompactIdLength = 32;

        /// <summary>多張收費單之間的分隔字元；永豐允許，且不會出現在 Guid 內。</summary>
        public const char Separator = ',';

        /// <summary>Param1 最多可放入的收費單張數：(255 + 1) / (32 + 1) = 7。</summary>
        public const int MaxIds = (ProviderParamMaxLength + 1) / (CompactIdLength + 1);

        /// <summary>
        /// 把收費單 Id 編成 Param1。清單順序就是 callback 的處理順序，第一張是 primary
        /// （付款人、LINE 通知、課程報名與信用卡資料都以 primary 為準）。
        /// </summary>
        /// <exception cref="ArgumentException">清單為空、含空 Guid、有重複，或超過 <see cref="MaxIds"/> 張。</exception>
        public static string Encode(IReadOnlyList<Guid> feeIds)
        {
            if (feeIds == null || feeIds.Count == 0)
            {
                throw new ArgumentException("至少需要一張收費單 Id。", nameof(feeIds));
            }

            if (feeIds.Count > MaxIds)
            {
                throw new ArgumentException(
                    "永豐 Param1 最多只能放 " + MaxIds + " 張收費單 Id，實際 " + feeIds.Count + " 張。",
                    nameof(feeIds));
            }

            var seen = new HashSet<Guid>();
            foreach (var feeId in feeIds)
            {
                if (feeId == Guid.Empty)
                {
                    throw new ArgumentException("收費單 Id 不可為空 Guid。", nameof(feeIds));
                }

                if (!seen.Add(feeId))
                {
                    throw new ArgumentException("收費單 Id 不可重複。", nameof(feeIds));
                }
            }

            // 單一收費單維持改版前的標準格式：舊版回呼程式與 QPaybackend 仍可用 new Guid(Param1) 解析。
            if (feeIds.Count == 1)
            {
                return feeIds[0].ToString();
            }

            var parts = new string[feeIds.Count];
            for (var index = 0; index < feeIds.Count; index++)
            {
                parts[index] = feeIds[index].ToString("N");
            }

            return string.Join(Separator, parts);
        }

        /// <summary>
        /// 解析永豐回傳的 Param1。接受單一 Guid（任何標準格式）或逗號分隔的多個 Guid，維持原順序。
        /// 任何一段不是 Guid 時回傳空清單：整筆視為無法辨識，呼叫端不可只入帳其中幾張。
        /// 重複的 Id 與空 Guid 會被略過。
        /// </summary>
        public static IReadOnlyList<Guid> Parse(string param1)
        {
            if (string.IsNullOrWhiteSpace(param1))
            {
                return Array.Empty<Guid>();
            }

            var result = new List<Guid>();
            var parts = param1.Split(Separator);
            if (parts.Length > MaxIds)
            {
                return Array.Empty<Guid>();
            }

            foreach (var part in parts)
            {
                var text = part.Trim();
                if (text.Length == 0)
                {
                    return Array.Empty<Guid>();
                }

                if (!Guid.TryParse(text, out var feeId) || feeId == Guid.Empty)
                {
                    return Array.Empty<Guid>();
                }

                if (result.Contains(feeId))
                {
                    return Array.Empty<Guid>();
                }

                result.Add(feeId);
            }

            return result;
        }

        /// <summary>
        /// 決定 callback 可以入帳的收費單 Id，維持 Param1 順序。
        /// 第一個成員是 primary，一定保留；其餘收費單必須與 primary 屬於同一位會友，
        /// 而且若已記錄信用卡訂單編號，必須等於本次付款的訂單編號，避免寫到別筆訂單的收費單。
        /// primary 沒有會友時只處理 primary。
        /// </summary>
        public static IReadOnlyList<Guid> SelectGroupMembers(IReadOnlyList<DonationFeeParamMember> members, string orderNo)
        {
            var result = new List<Guid>();
            if (members == null || members.Count == 0 || members[0] == null || members[0].FeeId == Guid.Empty)
            {
                return result;
            }

            var primary = members[0];
            result.Add(primary.FeeId);
            if (primary.ContactId == Guid.Empty)
            {
                return result;
            }

            var normalizedOrderNo = (orderNo ?? string.Empty).Trim();
            for (var index = 1; index < members.Count; index++)
            {
                var member = members[index];
                if (member == null || member.FeeId == Guid.Empty || result.Contains(member.FeeId))
                {
                    continue;
                }

                if (member.ContactId != primary.ContactId)
                {
                    continue;
                }

                var memberOrderNo = (member.CardOrderNo ?? string.Empty).Trim();
                if (memberOrderNo.Length > 0
                    && normalizedOrderNo.Length > 0
                    && !string.Equals(memberOrderNo, normalizedOrderNo, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(member.FeeId);
            }

            return result;
        }
    }
}
