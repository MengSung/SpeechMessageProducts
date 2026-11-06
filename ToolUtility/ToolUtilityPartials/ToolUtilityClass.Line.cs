/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - Line 訊息操作 (Partial Class 9/10) 包含：Line 推播訊息建立
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * 主要操作入口包含 CreatePushLineMessage。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - Line 訊息操作 (Partial Class 9/10)
    /// 包含：Line 推播訊息建立
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region Line 訊息操作
        public void CreatePushLineMessage(string UserId, string Subject, string Message)
        {
            try
            {
                if (!EXCUTION_TRACE_LINE) return;

                EntityCollection contactCollection = _facade.RetrieveContactCollectionByLineId(UserId);
                Entity aContact = (contactCollection != null && contactCollection.Entities.Count > 0)
                    ? contactCollection.Entities[0]
                    : null;

                if (aContact == null) return;

                Entity aEntity = new Entity("letter");
                _facade.SetEntityStringAttribute(ref aEntity, "subject", Subject);
                _facade.SetEntityStringAttribute(ref aEntity, "description", Message);
                _facade.SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);

                EntityReference regardingRef = new EntityReference("contact", aContact.Id);
                _facade.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", ref regardingRef);
                _facade.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);
                _facade.SetEntityBoolAttribute(ref aEntity, "directioncode", true);
                _facade.SetEntityIntAttribute(ref aEntity, "new_count", 1);
                _facade.SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                Entity Fromparty = new Entity("activityparty");
                Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                aEntity["from"] = new Entity[] { Fromparty };
                aEntity["to"] = new Entity[] { Fromparty };

                _facade.CreateEntity(aEntity);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void CreatePushLineMessage(IList<string> To, string Subject, string Message)
        {
            try
            {
                if (!EXCUTION_TRACE_LINE) return;

                foreach (String UserId in To)
                {
                    EntityCollection contactCollection = _facade.RetrieveContactCollectionByLineId(UserId);
                    Entity aContact = (contactCollection != null && contactCollection.Entities.Count > 0)
                        ? contactCollection.Entities[0]
                        : null;

                    if (aContact == null) continue;

                    Entity aEntity = new Entity("letter");
                    _facade.SetEntityStringAttribute(ref aEntity, "subject", Subject);
                    _facade.SetEntityStringAttribute(ref aEntity, "description", Message);
                    _facade.SetEntityStringAttribute(ref aEntity, "new_displayed_lineid", UserId);

                    EntityReference regardingRef = new EntityReference("contact", aContact.Id);
                    _facade.SetEntityLookUpAttribute(ref aEntity, "regardingobjectid", ref regardingRef);
                    _facade.SetEntityDateTimeAttribute(ref aEntity, "scheduledend", DateTime.Now);
                    _facade.SetEntityBoolAttribute(ref aEntity, "directioncode", true);
                    _facade.SetEntityIntAttribute(ref aEntity, "new_count", 1);
                    _facade.SetOptionSetAttribute(ref aEntity, "new_message_category", 100000000);

                    Entity Fromparty = new Entity("activityparty");
                    Fromparty["partyid"] = new EntityReference("contact", aContact.Id);

                    aEntity["from"] = new Entity[] { Fromparty };
                    aEntity["to"] = new Entity[] { Fromparty };

                    _facade.CreateEntity(aEntity);
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}
