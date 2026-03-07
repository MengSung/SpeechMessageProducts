using System;
using System.ServiceModel;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using ChurchReport.Models;
using ChurchReport.Models.CrmTransmitModule;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 出席記錄管理 (Partial)
    /// 包含：建立/更新 Present Record
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 建立出席記錄

        private EntityCollection CreatePresentRecordList(
            SmallGroupData aSmallGroupData, 
            String GroupName, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref int ValidSundayMemberNumber, 
            ref int ValidSmallGroupMemberNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            foreach (Member aMemberInfomation in aSmallGroupData.Members)
            {
                Entity aPresentRecord = CreatePresentRecord(
                    aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                    ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                    ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                    ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                if (aPresentRecord != null)
                    PresentRecordEntityCollection.Entities.Add(aPresentRecord);
            }

            return PresentRecordEntityCollection;
        }

        private object ConvertAttributeToExpectedTypeUsingMetadata(object value, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata meta)
        {
            if (value == null || meta == null) return null;

            try
            {
                // If the value is already one of the SDK types expected, keep it
                if (value is Microsoft.Xrm.Sdk.EntityReference && meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Lookup)
                    return value;
                if (value is Microsoft.Xrm.Sdk.OptionSetValue && (meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Picklist || meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Status || meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.State))
                    return value;
                if (value is Microsoft.Xrm.Sdk.Money && (meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Money || meta.AttributeType == Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Decimal))
                    return value;

                switch (meta.AttributeType)
                {
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Boolean:
                        if (value is bool b) return b;
                        if (value is string s && bool.TryParse(s, out var rb)) return rb;
                        if (value is int i) return i != 0;
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Integer:
                        if (value is int ii) return ii;
                        if (value is long l) return (int)l;
                        if (value is string ss && int.TryParse(ss, out var rint)) return rint;
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Double:
                        if (value is double d) return d;
                        if (value is float f) return (double)f;
                        if (value is string sd && double.TryParse(sd, out var rd)) return rd;
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Decimal:
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Money:
                        if (value is Microsoft.Xrm.Sdk.Money m) return m;
                        if (value is decimal dec) return new Microsoft.Xrm.Sdk.Money(dec);
                        if (value is double dd) return new Microsoft.Xrm.Sdk.Money((decimal)dd);
                        if (value is string sdec && decimal.TryParse(sdec, out var rdec)) return new Microsoft.Xrm.Sdk.Money(rdec);
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.String:
                        return value.ToString();
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.DateTime:
                        if (value is DateTime dt) return dt;
                        if (value is string sdt && DateTime.TryParse(sdt, out var rdt)) return rdt;
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Lookup:
                        if (value is Microsoft.Xrm.Sdk.EntityReference er) return er;
                        if (value is Guid g) return new Microsoft.Xrm.Sdk.EntityReference("contact", g);
                        if (value is string sg && Guid.TryParse(sg, out var rg)) return new Microsoft.Xrm.Sdk.EntityReference("contact", rg);
                        break;
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Picklist:
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.State:
                    case Microsoft.Xrm.Sdk.Metadata.AttributeTypeCode.Status:
                        if (value is Microsoft.Xrm.Sdk.OptionSetValue osv) return osv;
                        if (value is int iv) return new Microsoft.Xrm.Sdk.OptionSetValue(iv);
                        if (value is string sopt && int.TryParse(sopt, out var iopt)) return new Microsoft.Xrm.Sdk.OptionSetValue(iopt);
                        break;
                    default:
                        return value;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
        private EntityCollection CreatePresentRecordListByList(
            SmallGroupData aSmallGroupData, 
            SmallGroupData aSmallGroupDataFromList, 
            String GroupName, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref int ValidSundayMemberNumber, 
            ref int ValidSmallGroupMemberNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            EntityCollection PresentRecordEntityCollection = new EntityCollection();

            if (aSmallGroupData.LoginType == "小組長")
            {
                foreach (Member aMemberInfomation in aSmallGroupData.Members)
                {
                    Entity aPresentRecord = CreatePresentRecord(
                        aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                        ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                        ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                        ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

                    this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);

                    if (aPresentRecord != null)
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                }
            }
            else
            {
                foreach (Member aMemberInfomation in aSmallGroupDataFromList.Members)
                {
                    Entity aPresentRecord;
                    if (aSmallGroupData.Members[0].FullName != aMemberInfomation.FullName)
                    {
                        aPresentRecord = CreatePresentRecord(
                            aMemberInfomation, ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                            ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }
                    else
                    {
                        aPresentRecord = CreatePresentRecord(
                            aSmallGroupData.Members[0], ref aListEntity, ref aWeeklyReportId, ValidNumber, 
                            ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);
                    }

                    if (aPresentRecord != null)
                    {
                        this.m_ToolUtilityClass.AssignOwner("new_present_record", aPresentRecord, this.m_OwnerId);
                        PresentRecordEntityCollection.Entities.Add(aPresentRecord);
                    }
                }
            }

            return PresentRecordEntityCollection;
        }

        private Entity CreatePresentRecord(
            Member aMemberInfomation, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            Entity aContactEntity = UpdateContactInfomationFromList(aMemberInfomation.FullName, aListEntity.Id);

            if (aContactEntity == null)
                return null;

            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactEntity.Id);
            UpdateContactInfomation(aListEntity.Id, aMemberInfomation, ref aToUpdateContactEntity, HappyWeekTopic);

            Entity aPresentRecord = new Entity("new_present_record");

            SetupPresentRecordEntityAttributes(
                aPresentRecord, aMemberInfomation, ref aContactEntity, ref aListEntity, 
                ref aWeeklyReportId, ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                ref aGroupWeeklyReportGuid, HappyWeekIndex, HappyWeekTopic, PauseCheckBox);

            // Try to create present record robustly: if CRM reports missing attributes, remove them and retry
            Guid aPresentRecordId = Guid.Empty;
            int maxRetries = 5;
            int attempt = 0;

            // Snapshot of attributes we added so we can try removing candidates if CRM complains
            var addedAttrs = new List<string>(aPresentRecord.Attributes.Keys);

            // Preferred optional attributes to remove first when type/attribute errors occur
            var optionalAttrsPriority = new[] {
                "new_prayer_meeting_number",
                "new_child_number",
                "new_big_disciple_number",
                "new_leadership_small_lecture_number",
                "new_leaders_gather_number",
                "new_happy_present",
                "new_happy_decision",
                "new_spiritual_work",
                "new_morning_pray",
                "new_general_care"
            };

            // Before attempting creates, obtain metadata-supported attribute names and types and filter/convert
            var supportedAttrs = this.m_ToolUtilityClass.GetEntityAttributeNames("new_present_record");
            var attrTypes = this.m_ToolUtilityClass.GetEntityAttributeTypes("new_present_record");
            var attrMetadata = this.m_ToolUtilityClass.GetEntityAttributeMetadata("new_present_record");

            foreach (var key in new List<string>(aPresentRecord.Attributes.Keys))
            {
                if (!supportedAttrs.Contains(key))
                {
                    aPresentRecord.Attributes.Remove(key);
                    this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"[CreatePresentRecord] 於建立前移除不支援欄位 '{key}'");
                    continue;
                }

                // If metadata reports a specific type, attempt to convert value to expected type
                if (attrMetadata.TryGetValue(key, out var expectedMeta))
                {
                    var val = aPresentRecord.Attributes[key];
                    try
                    {
                        object converted = ConvertAttributeToExpectedTypeUsingMetadata(val, expectedMeta);
                        if (converted == null)
                        {
                            // If conversion failed, remove attribute to be safe
                            aPresentRecord.Attributes.Remove(key);
                            System.Diagnostics.Trace.WriteLine($"[CreatePresentRecord] 移除因型別不符的欄位 '{key}' (metadata)");
                        }
                        else
                        {
                            aPresentRecord.Attributes[key] = converted;
                        }
                    }
                    catch (Exception ex)
                    {
                        aPresentRecord.Attributes.Remove(key);
                        System.Diagnostics.Trace.WriteLine($"[CreatePresentRecord] 轉換欄位 '{key}' 型別失敗: {ex.Message}");
                    }
                }
            }

            while (attempt < maxRetries)
            {
                try
                {
                    aPresentRecordId = this.m_ToolUtilityClass.CreateEntity(aPresentRecord);
                    break;
                }
                catch (FaultException ex)
                {
                    attempt++;

                    // Try to extract attribute name from several possible message formats
                    string attrName = null;
                    try
                    {
                        var m1 = System.Text.RegularExpressions.Regex.Match(ex.Message, "Name = '(?<attr>[^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m1.Success) attrName = m1.Groups["attr"].Value;
                        if (string.IsNullOrEmpty(attrName))
                        {
                            var m2 = System.Text.RegularExpressions.Regex.Match(ex.Message, "attribute '(?<attr>[^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (m2.Success) attrName = m2.Groups["attr"].Value;
                        }
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(attrName) && aPresentRecord.Attributes.Contains(attrName))
                    {
                        aPresentRecord.Attributes.Remove(attrName);
                        this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"[CreatePresentRecord] 移除欄位 '{attrName}' 並重試 (嘗試 {attempt})");
                        continue;
                    }

                    // If attribute name not found in message, try removing from priority list
                    bool removed = false;
                    foreach (var candidate in optionalAttrsPriority)
                    {
                        if (aPresentRecord.Attributes.Contains(candidate))
                        {
                            aPresentRecord.Attributes.Remove(candidate);
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"[CreatePresentRecord] 移除優先候選欄位 '{candidate}' 並重試 (嘗試 {attempt})");
                            removed = true;
                            break;
                        }
                    }
                    if (removed) continue;

                    // Finally, try removing the first attribute whose value is a plain Int32 (common cause of type mismatch)
                    string removedAttr = null;
                    foreach (var key in new List<string>(aPresentRecord.Attributes.Keys))
                    {
                        var val = aPresentRecord.Attributes[key];
                        if (val is int || val is Int32)
                        {
                            removedAttr = key;
                            aPresentRecord.Attributes.Remove(key);
                            this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"[CreatePresentRecord] 移除 Int32 類型欄位 '{key}' 並重試 (嘗試 {attempt})");
                            break;
                        }
                    }
                    if (!string.IsNullOrEmpty(removedAttr)) continue;

                    // Could not resolve - log and rethrow
                    this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"[CreatePresentRecord] 無法處理 FaultException: {ex.Message}");
                    throw;
                }
            }

            if (aPresentRecordId == Guid.Empty)
            {
                // 最後仍無法建立，記錄並回傳 null
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "[CreatePresentRecord] 無法建立 new_present_record (重試次數達上限)");
                return null;
            }

            return this.m_ToolUtilityClass.RetrieveEntity("new_present_record", aPresentRecordId);
        }

        /// <summary>
        /// Convert attribute value to expected CRM attribute type. Returns null if cannot convert.
        /// expectedType is the string representation of AttributeTypeCode (e.g., "Integer", "Lookup", "Boolean", "String", "DateTime", "Money", "Picklist")
        /// </summary>
        private object ConvertAttributeToExpectedType(object value, string expectedType)
        {
            if (value == null) return null;

            try
            {
                switch (expectedType.ToLowerInvariant())
                {
                    case "boolean":
                    case "booleanattribute":
                        if (value is bool b) return b;
                        if (value is string s)
                        {
                            if (bool.TryParse(s, out var rb)) return rb;
                            if (int.TryParse(s, out var ri)) return ri != 0;
                        }
                        if (value is int i) return i != 0;
                        break;
                    case "integer":
                    case "integerattribute":
                        if (value is int ii) return ii;
                        if (value is long l) return (int)l;
                        if (value is string ss && int.TryParse(ss, out var rint)) return rint;
                        break;
                    case "double":
                    case "doubleattribute":
                        if (value is double d) return d;
                        if (value is float f) return (double)f;
                        if (value is string sd && double.TryParse(sd, out var rd)) return rd;
                        break;
                    case "decimal":
                    case "money":
                    case "moneyattribute":
                        if (value is decimal dec) return new Microsoft.Xrm.Sdk.Money((decimal)dec);
                        if (value is double dd) return new Microsoft.Xrm.Sdk.Money((decimal)dd);
                        if (value is string sdec && decimal.TryParse(sdec, out var rdec)) return new Microsoft.Xrm.Sdk.Money(rdec);
                        break;
                    case "string":
                    case "stringattribute":
                        return value.ToString();
                    case "datetime":
                    case "datetimeattribute":
                        if (value is DateTime dt) return dt;
                        if (value is string sdt && DateTime.TryParse(sdt, out var rdt)) return rdt;
                        break;
                    case "lookup":
                    case "lookupattribute":
                        // Expect value to be Guid or string Guid
                        if (value is Guid g) return new Microsoft.Xrm.Sdk.EntityReference("contact", g);
                        if (value is string sg && Guid.TryParse(sg, out var rg)) return new Microsoft.Xrm.Sdk.EntityReference("contact", rg);
                        break;
                    case "picklist":
                    case "optionset":
                    case "optionsetvalue":
                        if (value is int iv) return new Microsoft.Xrm.Sdk.OptionSetValue(iv);
                        if (value is string sopt && int.TryParse(sopt, out var iopt)) return new Microsoft.Xrm.Sdk.OptionSetValue(iopt);
                        break;
                    default:
                        // Unknown expected type - return original value
                        return value;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        #endregion

        #region 更新出席記錄

        private void UpdatePresentRecord(
            List<MemberInfomation> aGroupNamedListMemberInfomation, 
            EntityCollection PresentRecordCollection, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            SmallGroupData aSmallGroupData, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            try
            {
                foreach (Member aMember in aSmallGroupData.Members)
                {
                    Entity aMachedPresentRecordEntity = SearchPresentRecordByName(aMember.FullName, ref PresentRecordCollection);

                    if (aMachedPresentRecordEntity != null)
                    {
                        UpdateSinglePresentRecord(
                            aMember, aMachedPresentRecordEntity, ref aListEntity, 
                            ValidNumber, ref aWeeklySundayRate, ref aWeeklySmallGroupRate, 
                            ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                            HappyWeekTopic, PauseCheckBox);
                    }
                }

                // 移除已指派或轉介的成員
                for (int i = aSmallGroupData.Members.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(aSmallGroupData.Members[i].AssignedGroup) || 
                        aSmallGroupData.Members[i].FollowUpNextStep == "轉介")
                    {
                        aSmallGroupData.Members.RemoveAt(i);
                    }
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private void UpdateSinglePresentRecord(
            Member aMember,
            Entity aMachedPresentRecordEntity,
            ref Entity aListEntity,
            Double ValidNumber,
            ref Double aWeeklySundayRate,
            ref Double aWeeklySmallGroupRate,
            ref int aWeeklySundayNumber,
            ref int aWeeklySmallGroupNumber,
            String HappyWeekTopic,
            bool PauseCheckBox)
        {
            // 更新聯絡人資訊
            EntityReference aFullNameEntityReference = aMachedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record")
                ? (EntityReference)aMachedPresentRecordEntity.Attributes["new_contact_new_present_record"]
                : new EntityReference();

            Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
            Entity aToUpdateContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);
            UpdateContactInfomation(aListEntity.Id, aMember, ref aToUpdateContactEntity, HappyWeekTopic);

            // 取得委身類型
            String ClearIdentity = "";
            bool AccumulateFlag = this.IsValidMember(aMachedPresentRecordEntity, ref ClearIdentity);

            // 設定主日出席
            if (aMember.Sunday)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySundayNumber += 1;
                if (ValidNumber > 0 && AccumulateFlag)
                    aWeeklySundayRate += 1 / ValidNumber;
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_sunday_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_sunday_rate", 0.0);
            }

            // 設定小組出席
            if (aMember.SmallGroup)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySmallGroupNumber += 1;
                if (ValidNumber > 0 && AccumulateFlag)
                    aWeeklySmallGroupRate += 1 / ValidNumber;
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_group_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aMachedPresentRecordEntity, "new_small_group_rate", 0);
            }

            // 設定幸福小組與決志
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_present", aMember.SmallGroup ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_happy_decision", aMember.Decision ? 1 : 0);

            // 設定代禱事項與跟進
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_explanation", aMember.PrayItem);
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMember.FollowUpWeek));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMember.FollowUpResult));

            // 設定探訪 (new_visit OptionSet)
            int visitOptionValue = ConvertVisitToIndex(aMember.Visit);
            if (visitOptionValue != EMPTY_VALUE)
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_visit", visitOptionValue);

            if (!string.IsNullOrEmpty(aMember.FollowUpNextStep))
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMember.FollowUpNextStep));

            if (!string.IsNullOrEmpty(aMember.FollowUpOption))
            {
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aMachedPresentRecordEntity, "new_followup_ways", ConvertFollowUpOptionToIndex(aMember.FollowUpOption));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aMachedPresentRecordEntity, "new_follow_up", aMember.FollowUpOption);
            }

            AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMember);

            // 設定靈修次數
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_spiritual_work", aMember.SpiritualWork);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_morning_pray", aMember.MorningPray);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aMachedPresentRecordEntity, "new_general_care", aMember.GeneralCare);

            // 設定暫停與顯示
            this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_pause", PauseCheckBox);

            if (!string.IsNullOrEmpty(aMember.AssignedGroup) && !aMember.AssignedGroup.Contains("關懷"))
                m_ToolUtilityClass.SetEntityBoolAttribute(ref aMachedPresentRecordEntity, "new_not_display", true);

            this.m_ToolUtilityClass.AssignOwner("new_present_record", aMachedPresentRecordEntity, this.m_OwnerId);

            // 處理小組指派
            if (!string.IsNullOrEmpty(aMember.AssignedGroup))
                AssignNewSmallGroup(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);
            else if (aMember.FollowUpNextStep == "轉介")
                TerminateNewPersonFollowUp(aMachedPresentRecordEntity, aMember.AssignedGroup, aListEntity);

            this.m_ToolUtilityClass.UpdateEntity(ref aMachedPresentRecordEntity);
        }

        #endregion

        #region 設定出席記錄屬性

        private void SetupPresentRecordEntityAttributes(
            Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref Entity aContactEntity, 
            ref Entity aListEntity, 
            ref Guid aWeeklyReportId, 
            Double ValidNumber, 
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber, 
            ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, 
            String HappyWeekIndex, 
            String HappyWeekTopic, 
            bool PauseCheckBox)
        {
            try
            {
                // 設定名稱
                String PresentRecordName = $"{aMemberInfomation.FullName}-{this.m_Sunday:00}/{this.m_Sunday.Month:00}/{this.m_Sunday.Day:00} 出席紀錄";
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_name", PresentRecordName);

                // 設定聯絡人
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_new_present_record", "contact", aContactEntity.Id);

                // 關聯週報
                if (aWeeklyReportId != Guid.Empty)
                    this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_group_present_weekly_report_prese", "new_group_present_weekly_report", aWeeklyReportId);

                // 設定領袖關聯
                SetupLeaderReferences(ref aPresentRecord, ref aListEntity);

                // 設定日期與地點
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_sunday_date", this.m_Sunday);
                if (aGroupWeeklyReportGuid.SmallGroupDate.Year > 1)
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aPresentRecord, "new_group_date", aGroupWeeklyReportGuid.SmallGroupDate);

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_place", m_SmallGroupPlace);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_group_time", m_SmallGroupTime);

                // 取得委身類型
                int OptionSetNumber = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");
                String ClearIdentity = this.ConvertIndexToClearIdentity(OptionSetNumber);

                // 設定出席
                SetupAttendanceAttributes(ref aPresentRecord, aMemberInfomation, ref aListEntity, ValidNumber, 
                    ref aWeeklySundayRate, ref aWeeklySmallGroupRate, ref aWeeklySundayNumber, ref aWeeklySmallGroupNumber, 
                    ref ClearIdentity, ref aContactEntity);

                // 設定新人跟進
                SetupFollowUpAttributes(ref aPresentRecord, aMemberInfomation, ref ClearIdentity, ref aContactEntity);

                // 設定靈修與其他
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_spiritual_work", aMemberInfomation.SpiritualWork);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_morning_pray", aMemberInfomation.MorningPray);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_general_care", aMemberInfomation.GeneralCare);
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aPresentRecord, "new_pause", PauseCheckBox);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_cell_hpone", aMemberInfomation.Phone);
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        private void SetupLeaderReferences(ref Entity aPresentRecord, ref Entity aListEntity)
        {
            Guid aFamilyLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_familyhead_list");
            Guid aGroupLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_family_leader_list");
            Guid aRaceLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_race_leager_list");
            Guid aShepherdLeaderId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aListEntity, "new_contact_list_arealeader");

            if (aFamilyLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_familyhead_present_record", "contact", aFamilyLeaderId);
            if (aGroupLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_groupleader_present_record", "contact", aGroupLeaderId);
            if (aRaceLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_race_leader_present_record", "contact", aRaceLeaderId);
            if (aShepherdLeaderId != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_contact_arealeader_present_record", "contact", aShepherdLeaderId);
            if (aListEntity.Id != Guid.Empty)
                this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aPresentRecord, "new_list_new_present_record", "list", aListEntity.Id);
        }

        private void SetupAttendanceAttributes(
            ref Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref Entity aListEntity, 
            Double ValidNumber,
            ref Double aWeeklySundayRate, 
            ref Double aWeeklySmallGroupRate, 
            ref int aWeeklySundayNumber, 
            ref int aWeeklySmallGroupNumber,
            ref String ClearIdentity, 
            ref Entity aContactEntity)
        {
            // 主日出席
            if (aMemberInfomation.Sunday)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySundayNumber += 1;
                if (ValidNumber > 0 && IsValidContact(aContactEntity))
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 1 / ValidNumber);
                    aWeeklySundayRate += 1 / ValidNumber;
                }
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "主日", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_sunday_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_sunday_rate", 0);
            }

            // 小組出席
            if (aMemberInfomation.SmallGroup)
            {
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 1);
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, true);
                aWeeklySmallGroupNumber += 1;
                if (ValidNumber > 0 && IsValidContact(aContactEntity))
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 1 / ValidNumber);
                    aWeeklySmallGroupRate += 1 / ValidNumber;
                }
            }
            else
            {
                AddToDictionaryByIdentity(aListEntity.Id, "小組", ref ClearIdentity, ref aContactEntity, false);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_group_present_this_week", 0);
                this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aPresentRecord, "new_small_group_rate", 0);
            }

            // 其他聚會
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_prayer_meeting_number", aMemberInfomation.PrayerMeeting ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_child_number", aMemberInfomation.Child ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_big_disciple_number", aMemberInfomation.BigDisciple ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leadership_small_lecture_number", aMemberInfomation.LeadershipSmallLecture ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_leaders_gather_number", aMemberInfomation.Sunday ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_present", aMemberInfomation.SmallGroup ? 1 : 0);
            this.m_ToolUtilityClass.SetEntityIntAttribute(ref aPresentRecord, "new_happy_decision", aMemberInfomation.Decision ? 1 : 0);
        }

        private void SetupFollowUpAttributes(
            ref Entity aPresentRecord, 
            Member aMemberInfomation, 
            ref String ClearIdentity, 
            ref Entity aContactEntity)
        {
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_weeks", ConvertFollowUpWeekPickerToIndex(aMemberInfomation.FollowUpWeek));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_conclusion_choise", ConvertFollowUpResultPickerToIndex(aMemberInfomation.FollowUpResult));
            this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_next_step", ConvertFollowUpNextStepPickerToIndex(aMemberInfomation.FollowUpNextStep));

            int visitOptionValue = ConvertVisitToIndex(aMemberInfomation.Visit);
            if (visitOptionValue != EMPTY_VALUE)
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aPresentRecord, "new_visit", visitOptionValue);

            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_follow_up", aMemberInfomation.FollowUpOption);
            this.m_ToolUtilityClass.SetEntityStringAttribute(ref aPresentRecord, "new_explanation", aMemberInfomation.PrayItem);

            AddToDictionaryFollowByIdentity(ref ClearIdentity, ref aContactEntity, aMemberInfomation);
        }

        #endregion

        #region 輔助方法

        private Entity SearchPresentRecordByName(String Name, ref EntityCollection PresentRecordCollection)
        {
            foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
            {
                String aPresentRecordName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record");
                if (Name == aPresentRecordName)
                {
                    Entity aRetrievedPresentRecordEntity = this.m_ToolUtilityClass.RetrieveEntity("new_present_record", PresentRecordEntity.Id);

                    EntityReference aFullNameEntityReference = aRetrievedPresentRecordEntity.Attributes.Contains("new_contact_new_present_record")
                        ? (EntityReference)aRetrievedPresentRecordEntity.Attributes["new_contact_new_present_record"]
                        : new EntityReference();

                    Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aFullNameEntityReference.Id);

                    if (m_ToolUtilityClass.GetOptionSetAttribute(aContactEntity, "customertypecode") != 100000001 &&
                        m_ToolUtilityClass.GetEntityBoolAttribute(aRetrievedPresentRecordEntity, "new_not_display") == false)
                    {
                        return PresentRecordEntity;
                    }
                }
            }
            return null;
        }

        public Double GetValidMemberNumber(EntityCollection aPresentRecordCollection)
        {
            try
            {
                Double ValidMemberNumber = 0;
                foreach (Entity PresentRecordEntity in aPresentRecordCollection.Entities)
                {
                    String ClearIdentity = "";
                    if (this.IsValidMember(PresentRecordEntity, ref ClearIdentity))
                        ValidMemberNumber++;
                }
                return ValidMemberNumber;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool IsValidMember(Entity PresentRecordEntity, ref String ClearIdentity)
        {
            try
            {
                if (!PresentRecordEntity.Attributes.Contains("statecode"))
                    return false;

                OptionSetValue aOptionState = PresentRecordEntity.Attributes["statecode"] as OptionSetValue;
                if (aOptionState.Value != 0)
                    return false;

                if (!PresentRecordEntity.Attributes.Contains("new_contact_new_present_record"))
                    return false;

                EntityReference aEntityReference = (EntityReference)PresentRecordEntity.Attributes["new_contact_new_present_record"];
                Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aEntityReference.Id);

                if (!aContactEntity.Attributes.Contains("customertypecode"))
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aContactEntity, "customertypecode", 100000000);
                    this.m_ToolUtilityClass.UpdateEntity(ref aContactEntity);
                    return false;
                }

                OptionSetValue aCustomerTypeCode = aContactEntity.Attributes["customertypecode"] as OptionSetValue;
                ClearIdentity = this.ConvertIndexToClearIdentity(aCustomerTypeCode.Value);

                return aCustomerTypeCode.Value != 100000005 && 
                       aCustomerTypeCode.Value != 10000007 && 
                       aCustomerTypeCode.Value != 100000001;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        public bool IsValidContact(Entity aContactEntity)
        {
            try
            {
                int aCustomerTypeCodeValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode");

                return aCustomerTypeCodeValue != 100000004 && 
                       aCustomerTypeCodeValue != 100000000 && 
                       aCustomerTypeCodeValue != 100000007 && 
                       aCustomerTypeCodeValue != EMPTY_VALUE;
            }
            catch (System.Exception e)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {e}";
                throw;
            }
        }

        private double GetEffecttiveSmallGroupNumber(Guid ListEntityId)
        {
            Entity ListEntity = this.m_ToolUtilityClass.RetrieveEntity("list", ListEntityId);
            bool ListType = this.m_ToolUtilityClass.GetEntityBoolAttribute(ListEntity, "type");

            EntityCollection MemberCollection = ListType == false
                ? m_ToolUtilityClass.RetrieveMemberListCollectionByListId(ListEntityId)
                : m_ToolUtilityClass.RetrieveDynamicMemberList(ListEntityId);

            Double EffectiveNumber = 0.0;
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                Entity ContactEntity = ListType == false
                    ? m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id)
                    : m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);

                if (!ContactEntity.Attributes.Contains("statecode"))
                    continue;

                OptionSetValue aOptionState = ContactEntity.Attributes["statecode"] as OptionSetValue;
                if (aOptionState.Value != 0)
                    continue;

                if (ContactEntity.Attributes.Contains("customertypecode"))
                {
                    OptionSetValue aCustomerTypeCode = ContactEntity.Attributes["customertypecode"] as OptionSetValue;
                    if (aCustomerTypeCode.Value != 100000004 && 
                        aCustomerTypeCode.Value != 100000000 && 
                        aCustomerTypeCode.Value != 100000007)
                    {
                        EffectiveNumber++;
                    }
                }
                else
                {
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref ContactEntity, "customertypecode", 100000000);
                    this.m_ToolUtilityClass.UpdateEntity(ref ContactEntity);
                }
            }

            return EffectiveNumber;
        }

        #endregion
    }
}
