// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：CreatePushLineMessage
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
