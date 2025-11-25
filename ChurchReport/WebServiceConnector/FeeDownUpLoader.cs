using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ChurchReport.Models.CrmTransmitModule;
using ChurchReport.Models;

#region Dynamics 365 Microsoft.Xrm.Sdk.dll
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;
using System.Text.RegularExpressions;
#endregion

namespace ChurchReport.WebServiceConnector
{
    public class FeeDownUpLoader
    {
        #region 資料區
        #region 參數資料
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        //ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("CRM2011");

        private static Regex DigitsOnly = new Regex(@"[^\d]");

        private Dictionary<String, String> m_FeedBackReport = new Dictionary<string, string>();

        bool m_SetIdentityFlag = false;
        #endregion
        #region 常數參數

        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        //private const int MONTH_PERIOD = 2;      //幾個月內出席超過這次數就會改變委身類型=>小組組員
        private const int WEEK_PERIOD = 8;      //過去幾　WEEK_PERIOD　周內出席超過這次數就會改變委身類型=>小組組員
        private const int MINIMUM_THRESHOLD = 4;      //2個月內出席超過這次數就會改變委身類型=>小組組員

        #region 除錯用參數
        private const int TOTAL_LEVEL = 1;//改變這個值，就會改追蹤的階層，值越小越不會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        //private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，值越大越會追蹤，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
        // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
        // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
        //int TRACE_LEVEL = 5;
        //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #endregion
        #region 下載資料時所需要的參數

        List<Lesson> m_LessonList; // 需要點名的上課紀錄單名單
        List<Fee> m_FeeDataList = new List<Fee>(); // 需要點名的上課紀錄單名單

        Entity m_ContactEntity; //登入者在系統裡的實體
        Guid m_ContactId; //登入者在系統裡的ID
        EntityCollection m_Lists = new EntityCollection(); // 需要點名的名單
        EntityCollection m_PresentLists = new EntityCollection(); // 需要回報給區長/小家長的名單

        private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = true; // 族系組長能否幫小組長建立週報， true是可以
        //private const bool RACE_LEADER_CAN_CREATE_WEEKLYREPORT = false; // 族系組長能否幫小組長建立週報，false 不可以

        //List<Place2> m_GroupNamePlaces = new List<Place2>(); // 依據群組名稱過濾出來的會眾集合
        List<MemberInfomation> m_GroupNamedListMemberInfomation = new List<MemberInfomation>(); // 依據群組名稱過濾出來的會眾集合
        #endregion
        #region 真實運作區塊，並非模擬區塊
        #region 主程式區
        public List<Lesson> GetLessonList(String Account, String Password, ref String Result, ref ClassName aClassName)
        {
            // 取得登入者
            FindLoginUser(Account, Password);

            if (m_ContactEntity == null) return m_LessonList = new List<Lesson>(); // 沒找到就直接離開

            SetLessonList(ref Result, ref aClassName);

            return m_LessonList;
        }
        public List<Fee> GetFeeList(String Account, String Password, ref String Result, ref ClassName aClassName)
        {
            // 取得登入者
            FindLoginUser(Account, Password);

            if (m_ContactEntity == null) return m_FeeDataList = new List<Fee>(); // 沒找到就直接離開

            //實際要回傳，不是模擬
            SetFeeDataList(ref Result, ref aClassName);

            return m_FeeDataList;
        }
        public List<Fee> GetPresentFeeList(String DiscipleLessonsId, ref String Result, ref ClassName aClassName)
        {
            //實際要回傳，不是模擬
            SetPresentFeeList(DiscipleLessonsId, ref Result, ref aClassName);

            return m_FeeDataList;
        }
        public void SetGroupContent(String DiscipleLessonsName, String Result)
        {
            // 取得登入者
            foreach (Fee aFee in m_FeeDataList)
            {
                if (aFee.DiscipleLessonsName == DiscipleLessonsName)
                {
                    aFee.DiscipleLessonsName += ":" + Result;
                }
            }
        }
        #region 使用者登入
        private void FindLoginUser(String Account, String Password)
        {
            // 找登入使用者及其ID
            if (Account != "LineIdLogin")
            {
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByAccountNumber(Account, Password);
            }
            else
            {
                // 用 LINE 登入
                this.m_ContactEntity = this.m_ToolUtilityClass.RetrieveContactEntityByLineUserId(Password);
            }

            this.m_ContactId = m_ContactEntity.Id;
        }
        #endregion

        #endregion
        #endregion
        #region 下載資料
        public void SetLessonList(ref String Result, ref ClassName aClassName)
        {
            // 初始化收費與點名紀錄
            m_LessonList = new List<Lesson>();

            // 取得與登入者需要收費的課程，課程結束後7日還會出現讚點名繳費網站
            EntityCollection aDiscipleLessonsEntityCollection = GetDiscipleLessonsEntityCollection();

            if ( aDiscipleLessonsEntityCollection != null && aDiscipleLessonsEntityCollection.Entities.Count > 0)
            {
                //處理一個一個的課程
                ProcesseLessonsList(ref aDiscipleLessonsEntityCollection, ref Result, ref aClassName);
            }
        }

        public void SetFeeDataList(ref String Result, ref ClassName aClassName)
        {
            // 初始化收費與點名紀錄
            m_FeeDataList = new List<Fee>();

            // 取得與登入者需要收費的課程，課程結束後7日還會出現讚點名繳費網站
            EntityCollection aDiscipleLessonsEntityCollection = m_ToolUtilityClass.QueryEntityListByDate("contact", "contactid", this.m_ContactEntity.Id.ToString(), "new_contact_new_disciple_lessons_fee", "new_disciple_lessons");

            //處理一個一個的課程
            ProcesseDiscipleLessons(ref aDiscipleLessonsEntityCollection, ref Result, ref aClassName);

        }
        public EntityCollection GetDiscipleLessonsEntityCollection()
        {
            EntityCollection aDiscipleLessonsEntity = new EntityCollection();
            // 是否是講員
            EntityCollection aDiscipleLessonsEntityCollection = m_ToolUtilityClass.QueryEntityListByDate("contact", "contactid", this.m_ContactEntity.Id.ToString(), "new_contact_teacher_new_disciple_less", "new_disciple_lessons");
            for( int i =0; i < aDiscipleLessonsEntityCollection.Entities.Count;i ++)
            {
                aDiscipleLessonsEntity.Entities.Add(aDiscipleLessonsEntityCollection.Entities[i]);
            }
            // 是否是收費點名人員
            aDiscipleLessonsEntityCollection = m_ToolUtilityClass.QueryEntityListByDate("contact", "contactid", this.m_ContactEntity.Id.ToString(), "new_contact_new_disciple_lessons_fee", "new_disciple_lessons");
            for (int i = 0; i < aDiscipleLessonsEntityCollection.Entities.Count; i++)
            {
                aDiscipleLessonsEntity.Entities.Add(aDiscipleLessonsEntityCollection.Entities[i]);
            }

            // 是否是收費點名人員助理
            aDiscipleLessonsEntityCollection = m_ToolUtilityClass.QueryEntityListByDate("contact", "contactid", this.m_ContactEntity.Id.ToString(), "new_contact_new_disciple_lessons_assi", "new_disciple_lessons");
            for (int i = 0; i < aDiscipleLessonsEntityCollection.Entities.Count; i++)
            {
                aDiscipleLessonsEntity.Entities.Add(aDiscipleLessonsEntityCollection.Entities[i]);
            }

            return aDiscipleLessonsEntity;
            // 取得與登入者需要收費的課程，課程結束後7日還會出現讚點名繳費網站

        }
        public void SetPresentFeeList(String DiscipleLessonsId, ref String Result, ref ClassName aClassName)
        {
            // 初始化收費與點名紀錄
            m_FeeDataList = new List<Fee>();

            //處理一個課程
            ProcessDiscipleLesson(DiscipleLessonsId, ref Result, ref aClassName);
        }

        public void ProcessDiscipleLesson(String DiscipleLessonsId, ref String Result, ref ClassName aClassName)
        {
            Result = ""; // 上課人數及繳費結果歸零

            // 取得課程
            Entity aDiscipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", new Guid(DiscipleLessonsId));

            // 取得與課程相關的上課紀錄
            String DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessonsEntity, "new_name");
            EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.RetrieveStorLessonsByDiscipleLessonsFetchXml(m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessonsEntity, DiscipleLessonsName), DiscipleLessonsId);

            // 取得所有上課紀錄單
            ProcesseStorLessons(aDiscipleLessonsEntity, ref aStorLessonsEntityCollection, ref Result);

            // 設定課程名稱
            ProcesseClassName(aDiscipleLessonsEntity, ref aClassName);

            SetGroupContent(DiscipleLessonsName, Result);
            //Result += Environment.NewLine;
        }

        public void ProcesseLessonsList(ref EntityCollection aDiscipleLessonsEntityCollection, ref String Result, ref ClassName aClassName)
        {
            foreach (Entity aDiscipleLessons in aDiscipleLessonsEntityCollection.Entities)
            {
                Lesson aLesson = new Lesson();

                // 課程名稱
                aLesson.DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_name");
                // 課程 ID
                aLesson.DiscipleLessonsId = aDiscipleLessons.Id.ToString();

                // 報名開始日期
                aLesson.EnrollStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_join_start_date");
                // 報名結束日期
                aLesson.EnrollEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_join_end_date");
                // 課程開始日期
                aLesson.LessonStartDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_class_start_date");
                // 課程結束日期
                aLesson.LessonEndDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aDiscipleLessons, "new_class_end_date");

                // 報名人數
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aDiscipleLessons.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");
                aLesson.EnrolledNumber = aStorLessonsEntityCollection.Entities.Count;

                // 把課程加入到清單中
                this.m_LessonList.Add(aLesson);
            }
        }
        public void ProcesseDiscipleLessons(ref EntityCollection aDiscipleLessonsEntityCollection, ref String Result, ref ClassName aClassName)
        {
            foreach (Entity aDiscipleLessons in aDiscipleLessonsEntityCollection.Entities)
            {
                Result = ""; // 上課人數及繳費結果歸零

                // 處理一個一個的課程
                //Result += "課程名稱" + this.m_ToolUtilityClass.GetEntityStringAttribute( aDiscipleLessons, "new_name");

                // 設定每節課的名稱及作業名稱，因為如果一個人點名一門課程以上就會無法正確顯示表頭的課程名稱，
                // 為避免造成混淆，乾脆直接恢復到預設值，等將來MASTER-DETAIL版本時再來改
                if (aDiscipleLessonsEntityCollection.Entities.Count == 1)
                { ProcesseClassName(aDiscipleLessons, ref aClassName); }

                // 取得與課程相關的上課紀錄
                EntityCollection aStorLessonsEntityCollection = m_ToolUtilityClass.QueryEntityList("new_disciple_lessons", "new_disciple_lessonsid", aDiscipleLessons.Id.ToString(), "new_new_disciple_lessons_new_stor_les", "new_stor_lessons");

                // 處理一個一個的上課紀錄
                ProcesseStorLessons(aDiscipleLessons, ref aStorLessonsEntityCollection, ref Result);

                String DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityStringAttribute(aDiscipleLessons, "new_name");
                SetGroupContent(DiscipleLessonsName, Result);
                //Result += Environment.NewLine;
            }
        }
        public void ProcesseClassName(Entity aDiscipleLessons, ref ClassName aClassName)
        {
            // 設定每節堂的名稱及作業名稱
            DateTime LessonDate;

            aClassName.Lesson1 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l1_name");
            if (aClassName.Lesson1 == "")
            {
                aClassName.Lesson1 = "第一課";
            }

            aClassName.Lesson2 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l2_name");
            if (aClassName.Lesson2 == "")
            {
                aClassName.Lesson2 = "第二課";
            }
            aClassName.Lesson3 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l3_name");
            if (aClassName.Lesson3 == "")
            {
                aClassName.Lesson3 = "第三課";
            }
            aClassName.Lesson4 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l4_name");
            if (aClassName.Lesson4 == "")
            {
                aClassName.Lesson4 = "第四課";
            }
            aClassName.Lesson5 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l5_name");
            if (aClassName.Lesson5 == "")
            {
                aClassName.Lesson5 = "第五課";
            }
            aClassName.Lesson6 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l6_name");
            if (aClassName.Lesson6 == "")
            {
                aClassName.Lesson6 = "第六課";
            }
            aClassName.Lesson7 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l7_name");
            if (aClassName.Lesson7 == "")
            {
                aClassName.Lesson7 = "第七課";
            }
            aClassName.Lesson8 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l8_name");
            if (aClassName.Lesson8 == "")
            {
                aClassName.Lesson8 = "第八課";
            }
            aClassName.Lesson9 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l9_name");
            if (aClassName.Lesson9 == "")
            {
                aClassName.Lesson9 = "第九課";
            }
            aClassName.Lesson10 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l10_name");
            if (aClassName.Lesson10 == "")
            {
                aClassName.Lesson10 = "第十課";
            }
            aClassName.Lesson11 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l11_name");
            if (aClassName.Lesson11 == "")
            {
                aClassName.Lesson11 = "第十一課";
            }
            aClassName.Lesson12 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l12_name");
            if (aClassName.Lesson12 == "")
            {
                aClassName.Lesson12 = "第十二課";
            }
            aClassName.Lesson13 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l13_name");
            if (aClassName.Lesson13 == "")
            {
                aClassName.Lesson13 = "第十三課";
            }
            aClassName.Lesson14 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l14_name");
            if (aClassName.Lesson14 == "")
            {
                aClassName.Lesson14 = "第十四課";
            }
            aClassName.Lesson15 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l15_name");
            if (aClassName.Lesson15 == "")
            {
                aClassName.Lesson15 = "第十五課";
            }
            aClassName.Lesson16 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l16_name");
            if (aClassName.Lesson16 == "")
            {
                aClassName.Lesson16 = "第十六課";
            }
            aClassName.Lesson17 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l17_name");
            if (aClassName.Lesson17 == "")
            {
                aClassName.Lesson17 = "第十七課";
            }
            aClassName.Lesson18 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l18_name");
            if (aClassName.Lesson18 == "")
            {
                aClassName.Lesson18 = "第十八課";
            }
            aClassName.Lesson19 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l19_name");
            if (aClassName.Lesson19 == "")
            {
                aClassName.Lesson19 = "第十九課";
            }
            aClassName.Lesson20 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l20_name");
            if (aClassName.Lesson20 == "")
            {
                aClassName.Lesson20 = "第二十課";
            }
            aClassName.Lesson21 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l21_name");
            if (aClassName.Lesson21 == "")
            {
                aClassName.Lesson21 = "第二十一課";
            }
            aClassName.Lesson22 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l22_name");
            if (aClassName.Lesson22 == "")
            {
                aClassName.Lesson22 = "第二十二課";
            }
            aClassName.Lesson23 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l23_name");
            if (aClassName.Lesson23 == "")
            {
                aClassName.Lesson23 = "第二十三課";
            }
            aClassName.Lesson24 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l24_name");
            if (aClassName.Lesson24 == "")
            {
                aClassName.Lesson24 = "第二十四課";
            }
            aClassName.Lesson25 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l25_name");
            if (aClassName.Lesson25 == "")
            {
                aClassName.Lesson25 = "第二十五課";
            }
            aClassName.Lesson26 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l26_name");
            if (aClassName.Lesson26 == "")
            {
                aClassName.Lesson26 = "第二十六課";
            }
            aClassName.Lesson27 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l27_name");
            if (aClassName.Lesson27 == "")
            {
                aClassName.Lesson27 = "第二十七課";
            }
            aClassName.Lesson28 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l28_name");
            if (aClassName.Lesson28 == "")
            {
                aClassName.Lesson28 = "第二十八課";
            }
            aClassName.Lesson29 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l29_name");
            if (aClassName.Lesson29 == "")
            {
                aClassName.Lesson29 = "第二十九課";
            }
            aClassName.Lesson30 = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_l30_name");
            if (aClassName.Lesson30 == "")
            {
                aClassName.Lesson30 = "第三十課";
            }


            aClassName.HomeWorkA = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_homework1");
            aClassName.HomeWorkB = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_homework2");
            aClassName.HomeWorkC = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_homework3");
            aClassName.HomeWorkD = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_homework4");
            aClassName.HomeWorkE = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_homework5");

        }
        public void ProcesseStorLessons(Entity aDiscipleLessons, ref EntityCollection aStorLessonsEntityCollection, ref String Result)
        {
            int TotalFeeAmount = 0;
            foreach (Entity aStorLessons in aStorLessonsEntityCollection.Entities)
            {
                Entity aRetruevedStorLessons = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", aStorLessons.Id);
                Fee aFee = new Fee
                {
                    DiscipleLessonsId = aDiscipleLessons.Id.ToString(),
                    DiscipleLessonsName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aDiscipleLessons, "new_name"),
                };

                // 處理一個一個的的上課紀錄
                ProcesseStorLessonsParameter(aRetruevedStorLessons, ref aFee);

                // 取得與上課紀錄相關的收費單
                EntityCollection aFeeEntityCollection = m_ToolUtilityClass.QueryEntityList("new_stor_lessons", "new_stor_lessonsid", aRetruevedStorLessons.Id.ToString(), "new_stor_lessons_new_fee", "new_fee");

                // 處理一個一個的收費單
                TotalFeeAmount += ProcesseFee(aRetruevedStorLessons, ref aFee, ref aFeeEntityCollection);
            }

            // 取得與登入者需要收費的課程
            Result += "報名人數: " + aStorLessonsEntityCollection.Entities.Count.ToString() + " 人，";
            Result += "已繳費金額: " + TotalFeeAmount.ToString() + " 元";

        }
        public void ProcesseStorLessonsParameter(Entity aStorLessons, ref Fee aFee)
        {
            aFee.FullName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aStorLessons, "new_contact_new_stor_lessons");

            Guid aContactId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessons, "new_contact_new_stor_lessons");

            if (aContactId != Guid.Empty)
            {
                // 取得上課紀錄單的學員連絡人紀錄
                Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);

                aFee.MobilePhone = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aContact, "mobilephone");
                if (this.m_ToolUtilityClass.GetOptionSetAttribute(ref aContact, "gendercode") == 200000)
                {
                    aFee.Gender = "男性";
                }
                else
                {
                    aFee.Gender = "女性";
                }
                aFee.Birthday = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aContact, "birthdate");
                aFee.SmallGroupName = this.m_ToolUtilityClass.GetEntityLookupDisplayName(ref aContact, "new_cell_list_contact");

                aFee.StorLessonsId = aStorLessons.Id.ToString();

                // 取得班別
                aFee.SubClass = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aStorLessons, "new_sub_class");

                aFee.Lesson1 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_1_present");
                aFee.Lesson2 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_2_present");
                aFee.Lesson3 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_3_present");
                aFee.Lesson4 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_4_present");
                aFee.Lesson5 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_5_present");
                aFee.Lesson6 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_6_present");
                aFee.Lesson7 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_7_present");
                aFee.Lesson8 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_8_present");
                aFee.Lesson9 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_9_present");
                aFee.Lesson10 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_10_present");
                aFee.Lesson11 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_11_present");
                aFee.Lesson12 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_12_present");
                aFee.Lesson13 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_13_present");
                aFee.Lesson14 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_14_present");
                aFee.Lesson15 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_15_present");

                aFee.Lesson16 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_16_present");
                aFee.Lesson17 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_17_present");
                aFee.Lesson18 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_18_present");
                aFee.Lesson19 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_19_present");
                aFee.Lesson20 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_20_present");
                aFee.Lesson21 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_21_present");
                aFee.Lesson22 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_22_present");
                aFee.Lesson23 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_23_present");
                aFee.Lesson24 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_24_present");
                aFee.Lesson25 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_25_present");
                aFee.Lesson26 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_26_present");
                aFee.Lesson27 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_27_present");
                aFee.Lesson28 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_28_present");
                aFee.Lesson29 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_29_present");
                aFee.Lesson30 = this.m_ToolUtilityClass.GetEntityBoolAttribute(ref aStorLessons, "new_30_present");

                aFee.HomeWorkA = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aStorLessons, "new_finishhomework_date1").ToLocalTime();
                aFee.HomeWorkB = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aStorLessons, "new_finishhomework_date2").ToLocalTime();
                aFee.HomeWorkC = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aStorLessons, "new_finishhomework_date3").ToLocalTime();
                aFee.HomeWorkD = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aStorLessons, "new_finishhomework_date4").ToLocalTime();
                aFee.HomeWorkE = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(ref aStorLessons, "new_finishhomework_date5").ToLocalTime();
            }
        }
        public int ProcesseFee(Entity aStorLessons, ref Fee aFee, ref EntityCollection aFeeEntityCollection)
        {
            // 取得與上課紀錄相關的收費單
            Money aTotalAmountPaid = new Money(0);
            aFee.RefundAmount = 0;
            foreach (Entity aFeeEntity in aFeeEntityCollection.Entities)
            {
                // 處理一個一個與上課紀錄相關的收費單

                // 應收金額
                Money aShouldMoney = this.m_ToolUtilityClass.GetEntityMoneyAttribute(aFeeEntity, "new_fee_shoud_pay");
                aFee.ShouldPayAmount = (int)aShouldMoney.Value > 0 ? (int)aShouldMoney.Value : 0; // 傳回負值就歸零

                // 繳費日期
                aFee.PayDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aFeeEntity, "new_pay_date").ToLocalTime();

                // 繳費金額
                Money aMoney = this.m_ToolUtilityClass.GetEntityMoneyAttribute(aFeeEntity, "new_fee_really_paid");
                aMoney.Value = aMoney.Value > 0 ? aMoney.Value : 0; // 傳回負值就歸零

                // 退費日期
                aFee.RefundDate = this.m_ToolUtilityClass.GetEntityDateTimeAttribute(aFeeEntity, "new_refund_date").ToLocalTime();

                // 退費金額
                Money aRefundMoney = this.m_ToolUtilityClass.GetEntityMoneyAttribute(aFeeEntity, "new_refund_amount");
                aRefundMoney.Value = aRefundMoney.Value > 0 ? aRefundMoney.Value : 0; // 傳回負值就歸零
                aFee.RefundAmount = aFee.RefundAmount + (int)aRefundMoney.Value;


                aTotalAmountPaid.Value = aTotalAmountPaid.Value + aMoney.Value;

                switch (this.m_ToolUtilityClass.GetOptionSetAttribute(aFeeEntity, "new_pay_way"))
                {
                    case 100000004:
                        aFee.PayWay = "未知";
                        break;
                    case 100000000:
                        aFee.PayWay = "現金";
                        break;
                    case 100000001:
                        aFee.PayWay = "信用卡";
                        break;
                    case 100000002:
                        aFee.PayWay = "ATM轉帳";
                        break;
                    case 100000003:
                        aFee.PayWay = "超商付款";
                        break;
                    default:
                        aFee.PayWay = "未知";
                        break;
                }

            }

            aFee.Amount = (int)aTotalAmountPaid.Value;

            m_FeeDataList.Add(aFee);

            return aFee.Amount;
        }
        public void SetSimulationFeeDataList()
        {
            m_FeeDataList = new List<Fee>();
            Fee aFee;

            aFee = new Fee()
            {
                DiscipleLessonsId = "001",
                StorLessonsId = "001-001",
                DiscipleLessonsName = "201803 - 04再生產的生活(16屆) (V1)",
                FullName = "胡夢嵩",
                MobilePhone = "0910391931",
                PayDate = DateTime.Now,
                Amount = 800,
                Lesson1 = true,
                Lesson3 = true,
                HomeWorkA = DateTime.Now.AddDays(6),
            };
            m_FeeDataList.Add(aFee);

            aFee = new Fee()
            {
                DiscipleLessonsId = "001",
                StorLessonsId = "001-002",
                DiscipleLessonsName = "201803 - 04再生產的生活(16屆) (V1)",
                FullName = "吳連碧",
                MobilePhone = "0921834289",
                PayDate = new DateTime(1, 1, 1),
                Amount = 600,
                Lesson5 = true,
                Lesson6 = true,
                HomeWorkC = DateTime.Now.AddDays(3),
            };
            m_FeeDataList.Add(aFee);

            aFee = new Fee()
            {
                DiscipleLessonsId = "002",
                StorLessonsId = "021-001",
                DiscipleLessonsName = "201803 - 01成長的喜悅(23屆)-A2 (V2)",
                FullName = "張全興",
                MobilePhone = "0921965325",
                PayDate = DateTime.Now.AddDays(-3),
                Amount = 900,
                Lesson5 = true,
                Lesson6 = true,
                HomeWorkA = DateTime.Now.AddDays(3),
            };
            m_FeeDataList.Add(aFee);
            aFee = new Fee()
            {
                DiscipleLessonsId = "002",
                StorLessonsId = "002-002",
                DiscipleLessonsName = "201803 - 01成長的喜悅(23屆)-A2 (V2)",
                FullName = "白碧娥",
                MobilePhone = "0921834289",
                PayDate = DateTime.Now.AddDays(-3),
                Amount = 500,
                Lesson1 = true,
                Lesson3 = true,
                HomeWorkD = DateTime.Now.AddDays(3),
            };
            m_FeeDataList.Add(aFee);
        }
        #endregion
        #region 上傳資料
        public void UpdateFeeDataList(String Key, String Value, String StorLessonsId, ref bool CreateFlag)
        {
            Entity aStorLessonEntity = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", new Guid(StorLessonsId));

            // 取得比較易懂的委身類型
            Entity Fee;
            switch (Key)
            {
                case "PayDate":

                    // 上課紀錄單也要記錄繳費日期，目前就只填入最後一次繳費為主，因為操作簡單，先假設不讓他多次繳費，或是分期
                    // 但是 LINE 那邊顯示的繳費情況是以此欄位為準的
                    if (Value != null)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_pay_date", DateTime.Parse(Value));
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref aStorLessonEntity, "new_pay_date");
                    }
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);

                    Fee = GetFeeOfStorLesson(StorLessonsId, Key, ref CreateFlag);
                    if (Value != null)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref Fee, "new_pay_date", DateTime.Parse(Value));
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref Fee, "new_pay_date");
                    }

                    this.m_ToolUtilityClass.UpdateEntity(ref Fee);
                    break;
                case "Amount":

                    // 上課紀錄單也要記錄繳費金額，目前就只填入最後一次繳費為主，因為操作簡單，先假設不讓他多次繳費，或是分期
                    // 但是 LINE 那邊顯示的繳費情況是以此欄位為準的
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aStorLessonEntity, "new_paid_amount", new Money(Convert.ToDecimal(Value)));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);

                    Fee = GetFeeOfStorLesson(StorLessonsId, Key, ref CreateFlag);
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref Fee, "new_fee_really_paid", new Money(Convert.ToDecimal(Value)));
                    this.m_ToolUtilityClass.UpdateEntity(ref Fee);
                    break;
                case "RefundDate":
                    // 上課紀錄單也要記錄繳費日期，目前就只填入最後一次繳費為主，因為操作簡單，先假設不讓他多次繳費，或是分期
                    // 但是 LINE 那邊顯示的繳費情況是以此欄位為準的
                    Fee = GetFeeOfStorLesson(StorLessonsId, Key, ref CreateFlag);
                    if (Value != null)
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref Fee, "new_refund_date", DateTime.Parse(Value));
                    }
                    else
                    {
                        this.m_ToolUtilityClass.SetEntityDateTimeAttributeToNull(ref Fee, "new_refund_date");
                    }

                    this.m_ToolUtilityClass.UpdateEntity(ref Fee);
                    break;
                case "RefundAmount":
                    Fee = GetFeeOfStorLesson(StorLessonsId, Key, ref CreateFlag);
                    // 取得目前的實收金額
                    Money ReallyPaidMoney = this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref Fee, "new_fee_really_paid");

                    // 設定實收金額 = 目前的實收金額 - 退費金額
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref Fee, "new_fee_really_paid", new Money(ReallyPaidMoney.Value - Convert.ToDecimal(Value)));

                    // 設定退費金額
                    this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref Fee, "new_refund_amount", new Money(Convert.ToDecimal(Value)));

                    // 設定收費單付款狀態=已退費
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref Fee, "new_pay_status", 100000005);

                    // 更新收費單
                    this.m_ToolUtilityClass.UpdateEntity(ref Fee);

                    // 設定上課紀錄單報名狀態=取消報名（已退費）
                    this.m_ToolUtilityClass.SetOptionSetAttribute(ref aStorLessonEntity, "new_enroll_status", 100000012);

                    // 更新上課紀錄單
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);

                    break;
                case "PayWay":
                    Fee = GetFeeOfStorLesson(StorLessonsId, Key, ref CreateFlag);
                    SetFeePayWay(Value, ref Fee);
                    this.m_ToolUtilityClass.UpdateEntity(ref Fee);
                    break;
                case "SubClass":
                    this.m_ToolUtilityClass.SetEntityStringAttribute(ref aStorLessonEntity, "new_sub_class", Value);
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson1":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_1_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson2":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_2_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson3":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_3_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson4":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_4_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson5":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_5_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson6":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_6_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson7":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_7_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson8":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_8_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson9":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_9_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson10":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_10_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson11":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_11_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson12":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_12_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson13":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_13_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson14":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_14_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson15":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_15_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson16":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_16_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson17":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_17_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson18":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_18_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson19":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_19_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson20":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_20_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson21":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_21_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson22":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_22_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson23":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_23_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson24":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_24_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson25":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_25_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson26":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_26_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson27":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_27_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson28":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_28_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson29":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_29_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "Lesson30":
                    this.m_ToolUtilityClass.SetEntityBoolAttribute(ref aStorLessonEntity, "new_30_present", Convert.ToBoolean(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "HomeWorkA":
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_finishhomework_date1", DateTime.Parse(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "HomeWorkB":
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_finishhomework_date2", DateTime.Parse(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "HomeWorkC":
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_finishhomework_date3", DateTime.Parse(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "HomeWorkD":
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_finishhomework_date4", DateTime.Parse(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                case "HomeWorkE":
                    this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aStorLessonEntity, "new_finishhomework_date5", DateTime.Parse(Value));
                    this.m_ToolUtilityClass.UpdateEntity(ref aStorLessonEntity);
                    break;
                default:
                    return;
            }

        }
        public Entity GetFeeOfStorLesson(String StorLessonsId, String Type, ref bool CreateFlag)
        {
            // 取得與上課紀錄相關的收費單
            EntityCollection aFeeEntityCollection = m_ToolUtilityClass.QueryEntityList("new_stor_lessons", "new_stor_lessonsid", StorLessonsId, "new_stor_lessons_new_fee", "new_fee");

            if (aFeeEntityCollection.Entities.Count > 0)
            {
                CreateFlag = false;
                return aFeeEntityCollection.Entities[0];
            }
            else
            {
                CreateFlag = true;
                return CreateFee(StorLessonsId, Type);
            }
        }
        public Entity CreateFee(String StorLessonsId, String Type)
        {
            // 取得與上課紀錄相關的收費單
            Entity aStorLessonEntity = this.m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", new Guid(StorLessonsId));

            Entity aFee = new Entity("new_fee");


            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_contact_new_fee", "new_fee", this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));

            Guid DiscipleLessonsEntityId = this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_new_disciple_lessons_new_stor_les");
            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_disciple_lessons_new_fee", "new_fee", DiscipleLessonsEntityId);

            this.m_ToolUtilityClass.SetEntityLookUpAttribute(ref aFee, "new_stor_lessons_new_fee", "new_fee", aStorLessonEntity.Id);

            Entity aDiscipleLessonsEntity = this.m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", DiscipleLessonsEntityId);

            Money MoneyShouldPay = this.m_ToolUtilityClass.GetEntityMoneyAttribute(ref aDiscipleLessonsEntity, "new_lessons_fee");

            if (MoneyShouldPay.Value >= 0)
            {
                this.m_ToolUtilityClass.SetEntityMoneyAttribute(ref aFee, "new_fee_shoud_pay", MoneyShouldPay);
            }

            if (Type == "Amount")
            {
                this.m_ToolUtilityClass.SetEntityDateTimeAttribute(ref aFee, "new_pay_date", DateTime.Now);
                //this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFee, "new_pay_way", 100000004); // 預設繳費是未知
                this.m_ToolUtilityClass.SetOptionSetAttribute(ref aFee, "new_pay_way", 100000000); // 預設繳費是現金
            }

            // 新增收費單
            Guid aFeeId = this.m_ToolUtilityClass.CreateEntity(aFee);
            Entity aRetrievedFee = this.m_ToolUtilityClass.RetrieveEntity("new_fee", aFeeId);

            //指派負責人
            Entity aContact = this.m_ToolUtilityClass.RetrieveEntity("contact", this.m_ToolUtilityClass.GetEntityLookupAttribute(ref aStorLessonEntity, "new_contact_new_stor_lessons"));
            this.m_ToolUtilityClass.AssignOwner("new_fee", aRetrievedFee, this.m_ToolUtilityClass.GetOwnerId(aContact));

            return aRetrievedFee;

        }

        public void SetFeePayWay(String Value, ref Entity aFeeEntity)
        {

            switch (Value)
            {
                case "未知":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;
                case "現金":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000000);
                    break;
                case "信用卡":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000001);
                    break;
                case "ATM轉帳":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000002);
                    break;
                case "超商付款":
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000003);
                    break;
                default:
                    this.m_ToolUtilityClass.SetOptionSetAttribute(aFeeEntity, "new_pay_way", 100000004);
                    break;

            }
        }

    }

    #endregion
}
