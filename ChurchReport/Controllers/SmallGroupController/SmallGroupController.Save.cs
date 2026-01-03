using ChurchReport.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - 資料儲存與清理
    /// </summary>
    public partial class SmallGroupController
    {
        #region 資料儲存

        /// <summary>
        /// 儲存整合視圖週報資料（Fire-and-Forget 模式）
        /// </summary>
        [HttpPost]
        public IActionResult SaveIntegrate(
            string WeeklyReportData,
            string HappyWeekIndex,
            string HappyWeekTopic,
            string CheckBox,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.ListEntityName.Contains("快樂"))
                {
                    var validationResult = ValidateHappyGroupFields(HappyWeekIndex, HappyWeekTopic);
                    if (validationResult != null) return validationResult;
                }

                bool pauseCheckBox = CheckBox == "true";

                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 開始背景上傳...");
                        
                        InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.UploadIntegrateData(
                            InMemoryContext.ListManager.m_SelectDate,
                            InMemoryContext.ListManager.m_Account,
                            InMemoryContext.ListManager.m_Password,
                            InMemoryContext.ListManager.LoginType,
                            InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData,
                            WeeklyReportData,
                            HappyWeekIndex,
                            HappyWeekTopic,
                            pauseCheckBox
                        );

                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳完成");

                        CleanupTransferredMembers();
                        ClearMultiGroupCache();
                        ClearIntegrateCache(InMemoryContext.ListManager.ActiveListId);
                        
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 清理完成");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳失敗: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 錯誤堆疊:\n{ex.StackTrace}");
                        
                        try
                        {
                            ToolUtility?.TraceByLevel(1, 1, 
                                $"SaveIntegrate 背景上傳失敗: {ex.Message}\n{ex.StackTrace}");
                        }
                        catch
                        {
                            // 追蹤失敗不影響
                        }
                    }
                }, cancellationToken);

                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 立即回應使用者，背景處理中...");
                return Json(new { status = "1", message = "資料已送出，正在背景上傳中..." });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 啟動失敗: {e.Message}");
                return HandleError(e, "SaveIntegrate");
            }
        }

        /// <summary>
        /// 驗證幸福小組必填欄位
        /// </summary>
        private JsonResult ValidateHappyGroupFields(string weekIndex, string topic)
        {
            if (string.IsNullOrEmpty(weekIndex) && string.IsNullOrEmpty(topic))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週和主題" });
            }
            if (string.IsNullOrEmpty(weekIndex))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週" });
            }
            if (string.IsNullOrEmpty(topic))
            {
                return Json(new { status = "2", message = "幸福小組必須填寫主題" });
            }
            return null;
        }

        /// <summary>
        /// 清理已轉介或指派到其他小組的成員
        /// </summary>
        private void CleanupTransferredMembers()
        {
            try
            {
                var weeklyReport = InMemoryContext?.ListManager?.m_ListSmallGroupWeeklyReport;
                if (weeklyReport == null)
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupTransferredMembers] weeklyReport 為 null，跳過清理");
                    return;
                }

                var dataList = weeklyReport.m_SmallGroupDataList;
                if (dataList == null)
                {
                    System.Diagnostics.Debug.WriteLine("[CleanupTransferredMembers] m_SmallGroupDataList 為 null，跳過清理");
                    return;
                }

                var smallGroupData = dataList.m_SmallGroupData;
                if (smallGroupData?.Members != null)
                {
                    RemoveTransferredMembers(smallGroupData.Members);
                    System.Diagnostics.Debug.WriteLine($"[CleanupTransferredMembers] 已清理小組資料，剩餘 {smallGroupData.Members.Count} 筆");
                }

                var newPersonData = dataList.m_NewPersonFollowUpData;
                if (newPersonData?.Members != null)
                {
                    RemoveTransferredMembers(newPersonData.Members);
                    System.Diagnostics.Debug.WriteLine($"[CleanupTransferredMembers] 已清理新人跟進資料，剩餘 {newPersonData.Members.Count} 筆");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CleanupTransferredMembers] 清理失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 從清單中移除已轉介的成員
        /// </summary>
        private void RemoveTransferredMembers(List<Member> members)
        {
            if (members == null || members.Count == 0)
            {
                return;
            }

            int count = members.Count;
            int index = 0;

            for (int i = 0; i < count; i++)
            {
                if (index >= members.Count)
                {
                    break;
                }

                if (ShouldRemoveMember(members[index]))
                {
                    members.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }
        }

        /// <summary>
        /// 判斷成員是否應該被移除
        /// </summary>
        private bool ShouldRemoveMember(Member member)
        {
            if (member == null)
            {
                return false;
            }

            return (!string.IsNullOrEmpty(member.AssignedGroup)) ||
                   (member.FollowUpNextStep == "轉介");
        }

        #endregion
    }
}
