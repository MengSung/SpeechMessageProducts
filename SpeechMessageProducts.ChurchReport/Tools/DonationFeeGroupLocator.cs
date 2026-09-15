// ============================================================================
// AI-繁體中文檔案註解
// 檔案責任：以付款群組鍵查出同會友、同次建立的兄弟收費單。
// 隔離：訂單鍵以外必須限制 contact 與建立時間窗；結果不寫入 Session、static cache 或背景佇列。
// 生命週期：CRM 結果僅為本次 callback 的區域集合，方法結束後不再持有 Entity 參考。
// 編碼要求：UTF-8 without BOM、CRLF。
// ============================================================================
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
