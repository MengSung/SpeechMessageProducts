using ChurchReport.WebServiceConnector;
using LineMessagingProcessor;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.

namespace ChurchReport.Models
{
    public class PollManager : Controller
    {
        #region 資料區
        private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365");

        public PollModel m_PollModel { get; set; } = new PollModel();


        public Entity m_Contact;
        private Entity m_Lesson = null;
        private String m_UserLineId = "";
        private String m_UserName = "";
        private String m_ClassName = "";

        // 神學生預設費用
        private const decimal GOD_STUDENT_FEE = 400;
        #endregion
        #region 電腦網頁登入
        public async Task<IActionResult> SavePoll(PollModel PollModel, String QrCodeIdString, String LineUserId)
        {
            try
            {
                m_UserLineId = LineUserId;
                // 取得掃描者全名
                m_Contact = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(LineUserId);
                if (m_Contact != null)
                {
                    m_UserName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "fullname");
                }

                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");

                // 取得上課紀錄單
                Entity StorLessonEntity = RetrieveStorLesson(m_Lesson, m_ClassName, m_UserName, m_Contact.Id.ToString());

                UpdateStorLesson(PollModel, StorLessonEntity);

                UpdateContactPollResult(PollModel);

                return Json(new { status = "1", message = "謝謝" + m_UserName + "的參與，牧區及事工單位的同工將主動與" + m_UserName + "接洽！" });

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        public Entity RetrieveStorLesson(Entity aLesson, String LessonName, String UserName, String UserId)
        {
            try
            {
                // 取得與課程相關的上課紀錄
                //EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aLesson.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByFetchXml(LessonName, aLesson.Id.ToString(), UserName, UserId);

                if (aStorLessonsEntityCollection.Entities.Count > 0)
                {
                    // 有找到上課紀錄單
                    return this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessonsEntityCollection.Entities[0].Id);
                }
                else
                {
                    // 沒找到上課紀錄單

                    // 建立一個上課紀錄單
                    return this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", CreateNewStorLesson(m_Contact, ref aLesson));
                }
            }
            catch (System.Exception Exception)
            {
                String ErrorString = "錯誤訊息 : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + Exception.ToString();

                throw Exception;
            }
        }

        public PollModel SetDisplayFlag(String QrCodeIdString)
        {
            try
            {
                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                m_ClassName = this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");

                PollModel aPollModel = new PollModel();

                if (m_ClassName.Contains("成⾧班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = false;
                    aPollModel.DisplayLeaderFlag = false;
                }
                else if (m_ClassName.Contains("門徒班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = false;
                }
                else if (m_ClassName.Contains("小組長班"))
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = true;
                }
                else
                {
                    aPollModel.DisplayGrowFlag = true;
                    aPollModel.DisplayDecipleFlag = true;
                    aPollModel.DisplayLeaderFlag = true;
                }
                return aPollModel;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public String GetClassName(String QrCodeIdString)
        {
            try
            {
                // 取得課程
                string[] arr = QrCodeIdString.Split('_');
                Guid aGuid = new Guid(arr[0]);
                m_Lesson = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", aGuid);
                return this.m_ToolUtilityClass.GetEntityStringAttribute(m_Lesson, "new_name");
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public String GetPollResult(PollModel PollModel)
        {
            try
            {

                String PollResult = "";

                if (PollModel.SundayTreat == true)
                {
                    PollResult += "主日招待同工，";
                }
                if (PollModel.SaturdayChild == true)
                {
                    PollResult += "週六兒主服事同工，";
                }
                if (PollModel.SundaydayChild == true)
                {
                    PollResult += "主日兒主同工，";
                }
                if (PollModel.SundayNewFriend == true)
                {
                    PollResult += "主日新人接待同工，";
                }
                if (PollModel.DisplayPpt == true)
                {
                    PollResult += "主日控台同工，";
                }
                if (PollModel.WorshipVocal == true)
                {
                    PollResult += "主日敬拜團(人聲)，";
                }
                if (PollModel.WorshipInstrument == true)
                {
                    PollResult += "主日敬拜團(樂器)，";
                }
                if (PollModel.Instrument != "")
                {
                    PollResult += "樂器名稱=" + PollModel.Instrument + "，";
                }
                if (PollModel.CommunityProfit == true)
                {
                    PollResult += "社區福音行動(益人學苑)，";
                }
                if (PollModel.CommunityFlower == true)
                {
                    PollResult += "社區福音行動(恩朵協會)，";
                }
                if (PollModel.IncubateCampaign == true)
                {
                    PollResult += "培育營會行政同工，";
                }
                if (PollModel.SundayPrayer == true)
                {
                    PollResult += "主日禱告服事，";
                }
                if (PollModel.IncubateCampaignLeader == true)
                {
                    PollResult += "培育營會帶組同工，";
                }
                if (PollModel.Others != "")
                {
                    PollResult += "其他=" + PollModel.Others + "，";
                }
                return PollResult;
            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public void UpdateContactPollResult(PollModel PollModel)
        {
            try
            {
                String OrignalContactPollResult = m_ToolUtilityClass.GetEntityStringAttribute(ref m_Contact, "new_poll_result");

                String PollResult = "";
                if (PollModel.SundayTreat == true)
                {
                    if (!OrignalContactPollResult.Contains("主日招待同工"))
                    {
                        PollResult += "主日招待同工，";
                    }
                }
                if (PollModel.SaturdayChild == true)
                {
                    if (!OrignalContactPollResult.Contains("週六兒主服事同工"))
                    {
                        PollResult += "週六兒主服事同工，";
                    }
                }
                if (PollModel.SundaydayChild == true)
                {
                    if (!OrignalContactPollResult.Contains("主日兒主同工"))
                    {
                        PollResult += "主日兒主同工，";
                    }
                }
                if (PollModel.SundayNewFriend == true)
                {
                    if (!OrignalContactPollResult.Contains("主日新人接待同工"))
                    {
                        PollResult += "主日新人接待同工，";
                    }
                }
                if (PollModel.DisplayPpt == true)
                {
                    if (!OrignalContactPollResult.Contains("主日控台同工"))
                    {
                        PollResult += "主日控台同工，";
                    }
                }
                if (PollModel.WorshipVocal == true)
                {
                    if (!OrignalContactPollResult.Contains("主日敬拜團(人聲)"))
                    {
                        PollResult += "主日敬拜團(人聲)，";
                    }
                }
                if (PollModel.WorshipInstrument == true)
                {
                    if (!OrignalContactPollResult.Contains("主日敬拜團(樂器)"))
                    {
                        PollResult += "主日敬拜團(樂器)，";
                    }
                }
                if (PollModel.Instrument != "")
                {
                    if (!OrignalContactPollResult.Contains("樂器名稱=" + PollModel.Instrument))
                    {
                        PollResult += "樂器名稱=" + PollModel.Instrument + "，";
                    }
                }
                if (PollModel.CommunityProfit == true)
                {
                    if (!OrignalContactPollResult.Contains("社區福音行動(益人學苑)"))
                    {
                        PollResult += "社區福音行動(益人學苑)，";
                    }
                }
                if (PollModel.CommunityFlower == true)
                {
                    if (!OrignalContactPollResult.Contains("社區福音行動(恩朵協會)"))
                    {
                        PollResult += "社區福音行動(恩朵協會)，";
                    }
                }
                if (PollModel.IncubateCampaign == true)
                {
                    if (!OrignalContactPollResult.Contains("培育營會行政同工"))
                    {
                        PollResult += "培育營會行政同工，";
                    }
                }
                if (PollModel.SundayPrayer == true)
                {
                    if (!OrignalContactPollResult.Contains("主日禱告服事"))
                    {
                        PollResult += "主日禱告服事，";
                    }
                }
                if (PollModel.IncubateCampaignLeader == true)
                {
                    if (!OrignalContactPollResult.Contains("培育營會帶組同工"))
                    {
                        PollResult += "培育營會帶組同工，";
                    }
                }
                if (PollModel.Others != "")
                {
                    if (!OrignalContactPollResult.Contains("其他=" + PollModel.Others))
                    {
                        PollResult += "其他=" + PollModel.Others + "，";
                    }
                }

                m_ToolUtilityClass.SetEntityStringAttribute(ref m_Contact, "new_poll_result", OrignalContactPollResult + PollResult);

                m_ToolUtilityClass.UpdateEntity(ref m_Contact);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }
        public void UpdateStorLesson(PollModel PollModel, Entity StorLessonEntity)
        {
            try
            {
                m_ToolUtilityClass.SetEntityStringAttribute(ref StorLessonEntity, "new_poll_result", GetPollResult(PollModel));
                m_ToolUtilityClass.SetEntityStringAttribute(ref StorLessonEntity, "new_poll_content", PollModel.PollContent);

                m_ToolUtilityClass.UpdateEntity(ref StorLessonEntity);

            }
            catch (System.Exception e)
            {
                string ErrorString = "錯誤訊息 : FullName = " + GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                //m_ToolUtilityClass.TraceByLevel(TOTAL_LEVEL, LEVEL_1, ErrorString);

                LineMessagingProcessorClass aLineMessagingProcessorClass = new LineMessagingProcessorClass();

                aLineMessagingProcessorClass.SendMessage("U7638e4ed509708a3573ba6d69970583d", "天母豐盛靈糧堂: 錯誤 => " + ErrorString);

                //return RedirectToAction("DisplayErrorView", new { ErrorMessage = e.Message });

                throw e;
            }
        }

        #endregion
        #region 新增、修改課程記錄
        public Guid CreateNewStorLesson(Entity aContact, ref Entity aDiscepleLessons)
        {
            try
            {
                //// 新增課程記錄
                Entity aNewStorLessonsEntity = new Entity("new_stor_lessons");

                // 設定課程記錄相關屬性
                // 複製上層的欄位過來
                this.CopyDisceipleAttributes(ref aContact, ref aNewStorLessonsEntity, ref aDiscepleLessons);

                // 建立可編輯式檢視表所編輯的欄位值
                this.SetupNewStorLessonsEntityAttributes(ref aNewStorLessonsEntity, aContact, ref aDiscepleLessons);

                // 新增課程記錄
                Guid aNewStorLessonsEntityId = this.m_ToolUtilityClass.CreateEntity(aNewStorLessonsEntity);

                //指派負責人
                try
                {
                    this.m_ToolUtilityClass.AssignOwner("new_stor_lessons", this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aNewStorLessonsEntityId), this.m_ToolUtilityClass.GetOwnerId(aContact));
                }
                catch (System.Exception e)
                {
                    String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                }

                return aNewStorLessonsEntityId;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        private void SetupNewStorLessonsEntityAttributes(ref Entity aNewStorLessonsEntity, Entity aContactEntity, ref Entity aDiscepleLessons)
        {
            try
            {
                #region 關聯雙翼養育課程屬性
                if (aDiscepleLessons.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons", aDiscepleLessons.Id); }
                #endregion

                #region 關聯姓名屬性
                if (aContactEntity != null && aContactEntity.Id != Guid.Empty)
                { this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aNewStorLessonsEntity, "new_contact_new_stor_lessons", "contact", aContactEntity.Id); }
                else
                { }
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        private void CopyDisceipleAttributes(ref Entity aRetrievedContact, ref Entity aNewStorLessonsEntity, ref Entity aDiscipleLessons)
        {
            try
            {
                const int EMPTY_VALUE = -999999999;

                #region 是否是以利亞之家課程
                this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_elijah", this.m_ToolUtilityClass.GetEntityBoolAttribute(aDiscipleLessons, "new_elijah_class"));
                #endregion
                #region 是否是神學生資格

                // 學籍卡ID
                Guid aRollCardEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_roll_card_new_stor_lessons");
                // 註冊單ID
                Guid aRegistrationFormEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aNewStorLessonsEntity, "new_registration_form_new_stor_lesson");

                if (aRollCardEntityId != Guid.Empty && aRegistrationFormEntityId != Guid.Empty)
                {
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aNewStorLessonsEntity, "new_god_student", true);
                    #region 設定費用
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", new Money(GOD_STUDENT_FEE));
                    #endregion
                }
                else
                {
                    #region 設定費用
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", this.m_ToolUtilityClass.GetEntityMoneyAttribute(aDiscipleLessons, "new_lessons_fee"));
                    #endregion

                }

                #endregion
                #region 設定上課名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l1_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l1_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l2_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l2_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l3_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l3_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l4_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l4_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l5_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l5_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l6_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l6_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l7_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l7_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l8_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l8_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l9_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l9_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l10_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l10_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l11_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l11_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l12_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l12_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l13_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l13_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l14_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l14_name"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_l15_name", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_l15_name"));
                #endregion
                #region 設定作業特會名稱
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_a_homework1", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_a_homework1"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_b_homework2", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_b_homework2"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_c_homework3", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_c_homework3"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_d_homework4", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_d_homework4"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_e_homework5", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_e_homework5"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_f_homework6", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_f_homework6"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_g_homework7", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_g_homework7"));
                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_h_homework8", this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_h_homework8"));
                #endregion
                #region 課程類別
                int ClassificationValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_classification");

                if (ClassificationValue != EMPTY_VALUE)
                {
                    try { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_classification", ClassificationValue); }
                    catch (System.Exception e) { }
                }
                #endregion
                #region 學期

                int SemesterValue = this.m_ToolUtilityClass.GetOptionSetAttribute(ref aDiscipleLessons, "new_semester");
                if (SemesterValue != EMPTY_VALUE)
                {
                    try { this.m_ToolUtilityClass.SetOptionSetAttribute(ref aNewStorLessonsEntity, "new_semester", SemesterValue); }
                    catch (System.Exception e) { }
                }

                #endregion
                #region 學分
                if (this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_credit", this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_credit"));
                }
                #endregion
                #region 上課(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_present", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_present"));
                }
                #endregion
                #region 作業(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_homework", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_homework"));
                }
                //this.m_ToolUtilityClass.SetEntityDoubleAttribute(ref aNewStorLessonsEntity, "new_homework", this.m_ToolUtilityClass.GetEntityDoubleAttribute(ref aDiscipleLessons, "new_homework"));
                #endregion
                #region 實習(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_practice", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_practice"));
                }
                #endregion
                #region 考試(%)
                if (this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam") >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityIntAttribute(ref aNewStorLessonsEntity, "new_exam", this.m_ToolUtilityClass.GetEntityIntAttribute(ref aDiscipleLessons, "new_exam"));
                }
                #endregion

                #region 課程名稱

                // 取得課程名稱
                String LessonDisplayName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_name");

                // 取得報名者的全名
                String FullName = "";
                FullName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "fullname");

                this.m_ToolUtilityClass.SetEntityStringAttribute(ref aNewStorLessonsEntity, "new_name", LessonDisplayName + "_" + FullName);

                #endregion

                #region 費用
                if (m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee").Value >= 0)
                {
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_fee", this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessons, "new_lessons_fee"));
                }

                // 繳費金額預設為0
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_paid_amount", new Money(0));

                // 總計收費預設為0
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aNewStorLessonsEntity, "new_rollup_fee", new Money(0));

                #endregion

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString() + Environment.NewLine;

                throw e;
            }
        }
        #endregion
    }
}
