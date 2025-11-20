using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 測試用的 Entity 工廠
    /// 快速建立測試用的 Dynamics 365 Entity 物件
    /// </summary>
    public static class TestEntityFactory
    {
        /// <summary>
        /// 建立一個測試用的 Contact Entity
        /// </summary>
        /// <param name="lineId">Line ID</param>
        /// <param name="fullName">聯絡人全名</param>
        /// <returns>Contact Entity</returns>
        public static Entity CreateContact(string lineId, string fullName)
        {
            var entity = new Entity("contact")
            {
                Id = Guid.NewGuid(),
                ["new_lineid"] = lineId,
                ["fullname"] = fullName,
                ["statecode"] = 0  // Active
            };
            
            return entity;
        }

        /// <summary>
        /// 建立一個測試用的 Contact Entity（含完整欄位）
        /// </summary>
        public static Entity CreateContactFull(
            string lineId,
            string fullName,
            string customerId = null,
            string accountNumber = null,
            string mobilePhone = null,
            string nationId = null)
        {
            var entity = CreateContact(lineId, fullName);
            
            if (!string.IsNullOrEmpty(customerId))
                entity["build_customer_id"] = customerId;
            
            if (!string.IsNullOrEmpty(accountNumber))
                entity["new_accountnumber"] = accountNumber;
            
            if (!string.IsNullOrEmpty(mobilePhone))
                entity["mobilephone"] = mobilePhone;
            
            if (!string.IsNullOrEmpty(nationId))
                entity["new_nationid"] = nationId;
            
            return entity;
        }

        /// <summary>
        /// 建立一個測試用的 List Entity（名單）
        /// </summary>
        public static Entity CreateList(string listName, Guid? listId = null)
        {
            return new Entity("list")
            {
                Id = listId ?? Guid.NewGuid(),
                ["listname"] = listName,
                ["statecode"] = 0
            };
        }

        /// <summary>
        /// 建立一個測試用的 Annotation Entity（附件）
        /// </summary>
        public static Entity CreateAnnotation(string subject, string fileName, byte[] documentBody)
        {
            return new Entity("annotation")
            {
                Id = Guid.NewGuid(),
                ["subject"] = subject,
                ["filename"] = fileName,
                ["documentbody"] = Convert.ToBase64String(documentBody),
                ["mimetype"] = "application/octet-stream"
            };
        }

        /// <summary>
        /// 建立一個空的 Entity（指定類型）
        /// </summary>
        public static Entity CreateEmpty(string entityName)
        {
            return new Entity(entityName)
            {
                Id = Guid.NewGuid()
            };
        }
    }
}
