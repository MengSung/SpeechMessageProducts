// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationFeeIdList.cs
// 所屬區塊：奉獻付款服務層；定義「同一筆付款包含哪些收費單」在永豐訂單參數 Param1 內的格式。
// 檔案責任：建單時把同一次付款建立的所有收費單 Id 編進 Param1；callback 時解析回來，並過濾不屬於本次付款的收費單。
// 外部契約（永豐豐收款 API 規格 v24，ChurchReport/文件/豐收款API開發規格書/spec_v24.txt）：
//   Param1 為 X(255)，不可有單引號、雙引號、百分比；付款完成後原樣回傳。
//   - 一張收費單：Param1＝標準格式 Guid（xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx），與改版前完全相同。
//   - 多張收費單：Param1＝32 碼 Guid（"N" 格式）以半形逗號串接，第一張是 primary。
//   - 每張 32 碼加 1 個逗號，255 字最多放 7 張，因此一次奉獻最多 7 個類別（MaxIds）。
// 使用者：DonationPaymentProcessor（建單）、DonationFeePaymentProcessor（RETURN_URL callback）、
//         外部 QPaybackend（ATM 入帳與 BACKEND_URL 通知，必須依相同格式解析）。
// 隔離與生命週期：純函式，不讀寫 CRM、Session、static 可變快取；不持有連線、timer 或背景工作。
// 編碼要求：本檔案維持 UTF-8 without BOM 與 CRLF。
// ============================================================================
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
