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
