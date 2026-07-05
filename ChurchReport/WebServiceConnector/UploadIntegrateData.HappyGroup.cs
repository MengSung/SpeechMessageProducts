// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/UploadIntegrateData.HappyGroup.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class UploadIntegrateData
// 主要成員：ProcessHappyGroupMembers、ProcessCoreMembers、GetCoreMembers、ExtractBestList、CalculateWeeklyReportTotalNumber
// 引用命名空間：System、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using Microsoft.Xrm.Sdk;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 上傳整合資料 - 幸福小組處理 (Partial)
    /// 包含：幸福小組成員管理、週報統計
    /// </summary>
    public partial class UploadIntegrateData
    {
        #region 幸福小組處理

        private String ProcessHappyGroupMembers(ref Entity HappyGroupListEntity, Entity aWeeklyReportEntity, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                String CoreMembers = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members");

                if (HappyWeekIndex == "第一週" && string.IsNullOrEmpty(CoreMembers))
                {
                    ProcessCoreMembers(ref HappyGroupListEntity, HappyWeekIndex, HappyWeekTopic);
                    return "";
                }
                else
                {
                    String BestList = ExtractBestList(ref HappyGroupListEntity);
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aWeeklyReportEntity, "new_best_name_list", BestList);
                    return BestList;
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private void ProcessCoreMembers(ref Entity HappyGroupListEntity, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                if (!string.IsNullOrEmpty(HappyWeekTopic))
                {
                    String CoreMembers = GetCoreMembers(HappyGroupListEntity.Id, HappyWeekIndex, HappyWeekTopic);
                    if (!string.IsNullOrEmpty(CoreMembers))
                        this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members", CoreMembers);
                }
                this.m_ToolUtilityClass.UpdateEntity(ref HappyGroupListEntity);
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String GetCoreMembers(Guid ListEntityId, String HappyWeekIndex, String HappyWeekTopic)
        {
            try
            {
                if (HappyWeekIndex != "第一週")
                    return "";

                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(ListEntityId, ref ListType);

                String CoreMembers = "";
                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    Entity aContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);
                    String Identity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));

                    if (!Identity.Contains("幸福BEST") && !Identity.Contains("未入組") && !Identity.Contains("新朋友"))
                        CoreMembers += this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname") + ",";
                }

                return CoreMembers.TrimEnd(',');
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        private String ExtractBestList(ref Entity HappyGroupListEntity)
        {
            try
            {
                String CoreMembers = this.m_ToolUtilityClass.GetEntityStringAttribute(ref HappyGroupListEntity, "new_core_members");

                if (string.IsNullOrEmpty(CoreMembers))
                    return "";

                bool ListType = false;
                EntityCollection MemberCollection = GetPersonalSmallGroupLeaderMemberData(HappyGroupListEntity.Id, ref ListType);

                String BestList = "";
                foreach (Entity MemberEntity in MemberCollection.Entities)
                {
                    Entity aContactEntity = GetContactFromMemberEntity(MemberEntity, ListType);
                    String aContactFullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContactEntity, "fullname");

                    if (!CoreMembers.Contains(aContactFullName))
                        BestList += aContactFullName + ",";
                }

                return BestList.TrimEnd(',');
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"錯誤訊息 : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                this.m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);
                throw;
            }
        }

        #endregion

        #region 週報統計

        public void CalculateWeeklyReportTotalNumber(ref Entity HappyWeeklyReport)
        {
            try
            {
                EntityCollection PresentRecordCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship(
                    "new_group_present_weekly_report", "new_group_present_weekly_reportid",
                    HappyWeeklyReport.Id.ToString(), "new_group_present_weekly_report_prese", "new_present_record");

                int TotalHappyPresent = 0, TotalHappyDecision = 0;
                int BestPresentNumber = 0, BestDecisionNumber = 0, WrokerNumber = 0;
                String BestPresentList = "", BestDecisionList = "", WorkerList = "";

                foreach (Entity PresentRecordEntity in PresentRecordCollection.Entities)
                {
                    int HappyPresent = Math.Max(0, this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_present"));
                    int HappyDecision = Math.Max(0, this.m_ToolUtilityClass.GetEntityIntAttribute(PresentRecordEntity, "new_happy_decision"));

                    TotalHappyPresent += HappyPresent;
                    TotalHappyDecision += HappyDecision;

                    Guid aContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(PresentRecordEntity, "new_contact_new_present_record");
                    if (aContactId == Guid.Empty)
                        continue;

                    Entity aContactEntity = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);
                    String Identity = this.ConvertIndexToIdentity(this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContactEntity, "customertypecode"));
                    String contactName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(PresentRecordEntity, "new_contact_new_present_record");

                    if (Identity.Contains("幸福BEST") || Identity.Contains("未入組") || Identity.Contains("新朋友"))
                    {
                        BestPresentNumber += HappyPresent;
                        BestDecisionNumber += HappyDecision;
                        if (HappyPresent == 1) BestPresentList += contactName + ",";
                        if (HappyDecision == 1) BestDecisionList += contactName + ",";
                    }
                    else
                    {
                        WrokerNumber += HappyPresent;
                        if (HappyPresent == 1) WorkerList += contactName + ",";
                    }
                }

                // 更新週報
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_small_group_number", TotalHappyPresent);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_decision_number", TotalHappyDecision);
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_best_attend_number", BestPresentNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_best_attend_list", BestPresentList.TrimEnd(','));
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_best_decision_number", BestDecisionNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_best_decision_list", BestDecisionList.TrimEnd(','));
                this.m_ToolUtilityClass.SetEntityIntAttribute(ref HappyWeeklyReport, "new_worker_attend_number", WrokerNumber);
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref HappyWeeklyReport, "new_worker_attend_list", WorkerList.TrimEnd(','));
            }
            catch (System.Exception Exception)
            {
                String ErrorString = $"ERROR : FullName = {this.GetType().FullName}, Time = {DateTime.Now}, Description = {Exception}";
                throw;
            }
        }

        #endregion
    }
}
