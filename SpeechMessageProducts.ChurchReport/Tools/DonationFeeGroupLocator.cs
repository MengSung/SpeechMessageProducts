/*
 * 檔案整體說明
 *
 * 目的與定位：建立有資料隔離邊界的多收費單群組 CRM 查詢。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationFeeGroupKey、DonationFeeGroupLocator。
 * - 讀取 primary 的既有群組鍵；遺失建立時間時採 UTC 現在時間以保守縮小查詢範圍。
 * - 依信用卡訂單或 ATM 虛擬帳號查詢，並強制限制 contact 與建立時間，防止跨會友誤入帳。
 * - primary 固定排第一，維持 Param1 舊流程與群組流程的處理順序。
 * - 查詢群組；任何群組鍵不足或 CRM 查詢失敗都安全退回 primary，不擴大付款影響範圍。
 * 主要操作入口包含 DonationFeeGroupKey、ReadKey、BuildSiblingQuery、OrderPrimaryFirst、Locate。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace;

namespace ChurchReport.Tools
{
    /// <summary>主收費單群組定位所需的不變資料快照，不保存 CRM Entity 或會友個資。</summary>
    public sealed record DonationFeeGroupKey(Guid PrimaryFeeId, Guid ContactId, DateTime CreatedOn, string CardOrderNo, string AtmPayNo);

    /// <summary>建立有資料隔離邊界的多收費單群組 CRM 查詢。</summary>
    public static class DonationFeeGroupLocator
    {
        public const int SiblingWindowMinutes = 10;

        /// <summary>讀取 primary 的既有群組鍵；遺失建立時間時採 UTC 現在時間以保守縮小查詢範圍。</summary>
        public static DonationFeeGroupKey ReadKey(ToolUtilityClass utility, Entity fee)
        {
            if (utility == null) throw new ArgumentNullException(nameof(utility));
            if (fee == null) throw new ArgumentNullException(nameof(fee));
            var created = fee.Contains("createdon") && fee["createdon"] is DateTime value ? value : DateTime.UtcNow;
            return new DonationFeeGroupKey(fee.Id, utility.GetEntityLookupAttribute(fee, "new_contact_new_fee"), created,
                (utility.GetEntityStringAttribute(fee, "new_q_pay_card_order_no") ?? string.Empty).Trim(),
                (utility.GetEntityStringAttribute(fee, "new_atm_pay_no") ?? string.Empty).Trim());
        }

        /// <summary>依信用卡訂單或 ATM 虛擬帳號查詢，並強制限制 contact 與建立時間，防止跨會友誤入帳。</summary>
        public static QueryExpression BuildSiblingQuery(DonationFeeGroupKey key)
        {
            if (key == null || key.ContactId == Guid.Empty) return null;
            var attribute = !string.IsNullOrWhiteSpace(key.CardOrderNo) ? "new_q_pay_card_order_no" : !string.IsNullOrWhiteSpace(key.AtmPayNo) ? "new_atm_pay_no" : null;
            if (attribute == null) return null;
            var value = attribute == "new_q_pay_card_order_no" ? key.CardOrderNo : key.AtmPayNo;
            var query = new QueryExpression("new_fee") { ColumnSet = new ColumnSet(true), Criteria = new FilterExpression(LogicalOperator.And) };
            query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
            query.Criteria.AddCondition("new_contact_new_fee", ConditionOperator.Equal, key.ContactId);
            query.Criteria.AddCondition("createdon", ConditionOperator.GreaterEqual, key.CreatedOn.AddMinutes(-SiblingWindowMinutes));
            query.Criteria.AddCondition("createdon", ConditionOperator.LessEqual, key.CreatedOn.AddMinutes(SiblingWindowMinutes));
            return query;
        }

        /// <summary>primary 固定排第一，維持 Param1 舊流程與群組流程的處理順序。</summary>
        public static List<Guid> OrderPrimaryFirst(Guid primaryId, IEnumerable<Guid> ids)
        {
            var result = new List<Guid> { primaryId };
            foreach (var id in ids ?? Enumerable.Empty<Guid>()) if (id != primaryId && !result.Contains(id)) result.Add(id);
            return result;
        }

        /// <summary>查詢群組；任何群組鍵不足或 CRM 查詢失敗都安全退回 primary，不擴大付款影響範圍。</summary>
        public static List<Entity> Locate(ToolUtilityClass utility, Entity primary)
        {
            var result = new List<Entity> { primary };
            var query = BuildSiblingQuery(ReadKey(utility, primary));
            if (query == null) return result;
            try
            {
                var entities = utility.m_Crm2011OrganizationService.RetrieveMultiple(query).Entities.ToDictionary(item => item.Id);
                foreach (var id in OrderPrimaryFirst(primary.Id, entities.Keys)) if (id != primary.Id) result.Add(entities[id]);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.WriteLine($"[DonationFeeGroupLocator] {exception}");
            }
            return result;
        }
    }
}
