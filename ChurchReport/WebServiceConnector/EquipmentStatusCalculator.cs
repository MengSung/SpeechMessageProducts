using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 裝備狀態計算器
    /// 負責計算每個成員的裝備狀態，顯示本階段可以結業的課程
    /// </summary>
    public class EquipmentStatusCalculator
    {
        // 透過 Factory 取得 ToolUtilityClass 單一實例
        private readonly ToolUtilityClass m_ToolUtilityClass;

        public EquipmentStatusCalculator()
        {
            m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
        }

        /// <summary>
        /// 計算並取得成員的裝備狀態
        /// 顯示「本階段可以結業」值為 TRUE 的上課紀錄單
        /// </summary>
        /// <param name="contactId">連絡人ID</param>
        /// <returns>裝備狀態文字（可結業的課程清單）</returns>
        public string CalculateEquipmentStatus(Guid contactId)
        {
            try
            {
                // 取得該連絡人的所有上課紀錄
                var storLessonsCollection = m_ToolUtilityClass.RetrieveManyToOneRelationship(
                    "contact", "contactid", contactId.ToString(), 
                    "new_contact_new_stor_lessons", "new_stor_lessons");

                if (storLessonsCollection == null || storLessonsCollection.Entities.Count == 0)
                {
                    return ""; // 沒有上課紀錄
                }

                // 收集可以結業的課程
                var graduatableCourses = new List<string>();

                foreach (var storLessonsEntity in storLessonsCollection.Entities)
                {
                    // 取得完整的上課紀錄實體
                    var storLesson = m_ToolUtilityClass.RetrieveEntity("new_stor_lessons", storLessonsEntity.Id);

                    // 檢查「本階段是否可以結業」
                    bool canGraduate = m_ToolUtilityClass.GetEntityBoolAttribute(storLesson, "new_current_complete");

                    if (canGraduate)
                    {
                        // 取得課程相關資訊
                        var discipleLessonId = m_ToolUtilityClass.GetEntityLookupAttribute(storLesson, "new_new_disciple_lessons_new_stor_les");
                        
                        if (discipleLessonId != null && discipleLessonId != Guid.Empty)
                        {
                            var discipleLessonEntity = m_ToolUtilityClass.RetrieveEntity("new_disciple_lessons", discipleLessonId);
                            
                            if (discipleLessonEntity != null)
                            {
                                string stageName = m_ToolUtilityClass.GetEntityStringAttribute(discipleLessonEntity, "new_now_stage_name");
                                string courseName = m_ToolUtilityClass.GetEntityLookupDisplayName(storLesson, "new_new_disciple_lessons_new_stor_les");
                                
                                // 只顯示關鍵階段的課程
                                if (IsKeyStage(stageName))
                                {
                                    graduatableCourses.Add($"{stageName}");
                                }
                            }
                        }
                    }
                }

                // 去除重複的階段並排序
                var distinctCourses = graduatableCourses.Distinct().ToList();

                // 組合成裝備狀態文字
                if (distinctCourses.Count == 0)
                {
                    return ""; // 沒有可結業的課程
                }
                else if (distinctCourses.Count <= 3)
                {
                    return string.Join("、", distinctCourses);
                }
                else
                {
                    // 如果課程太多，只顯示前3個並加上「等」
                    return string.Join("、", distinctCourses.Take(3)) + "等";
                }
            }
            catch (Exception ex)
            {
                // 發生錯誤時返回空字串
                return "";
            }
        }

        /// <summary>
        /// 判斷是否為關鍵階段
        /// </summary>
        /// <param name="stageName">階段名稱</param>
        /// <returns>是否為關鍵階段</returns>
        private bool IsKeyStage(string stageName)
        {
            if (string.IsNullOrEmpty(stageName))
                return false;

            // 關鍵課程階段清單
            string[] keyStages = new string[] 
            { 
                "E1", "E2", "E3", "成長班", "門徒班", "領袖班", 
                "福上", "福中", "福下" 
            };

            return keyStages.Any(stage => stageName.Contains(stage));
        }

        /// <summary>
        /// 批次計算多個成員的裝備狀態
        /// </summary>
        /// <param name="members">成員列表</param>
        public void CalculateEquipmentStatusForMembers(List<Member> members)
        {
            if (members == null || members.Count == 0)
                return;

            foreach (var member in members)
            {
                try
                {
                    // 從 PresentRecordId 取得連絡人ID需要額外查詢
                    // 這裡假設我們可以從 member.FullName 找到對應的連絡人
                    var contactEntity = m_ToolUtilityClass.RetrieveContactEntityByName(member.FullName);
                    
                    if (contactEntity != null)
                    {
                        // 計算裝備狀態
                        string calculatedStatus = CalculateEquipmentStatus(contactEntity.Id);
                        
                        // 更新 member 的 EquipmentStatus
                        if (!string.IsNullOrEmpty(calculatedStatus))
                        {
                            member.EquipmentStatus = calculatedStatus;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 單個成員計算失敗不影響其他成員
                    continue;
                }
            }
        }
    }
}
