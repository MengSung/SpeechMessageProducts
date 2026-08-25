// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
// 檔案責任：在 HTTP request 仍有效時建立隔離快照、接受背景上傳，並維持前景 Session 圖不被背景工作保存或回寫。
// 隔離與生命週期：Task 只捕獲 scalar、背景副本與 IServiceScopeFactory；背景 runner 唯一擁有 DI、ambient 與 trace scope，
// 在每一條成功、失敗或取消路徑釋放資源，不得重用 request scope 或記錄敏感資料。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using ChurchReport.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Factory;

namespace ChurchReport.Controllers
{
    /// <summary>小組管理控制器的儲存與背景清理 HTTP 入口。</summary>
    public partial class SmallGroupController
    {
        #region 資料儲存

        /// <summary>
        /// 接受整合週報上傳並排程只持有隔離副本的背景作業。
        /// </summary>
        /// <remarks>
        /// accepted 只表示已建立快照並交給背景工作，絕不代表 CRM 成功。前景 Session 圖不會由
        /// 背景清理回寫；客戶端應依 requiresRefresh 重新讀取權威資料。背景工作忽略 client disconnect，
        /// 但其 scope 和副本都由 runner 在完成時確定釋放。
        /// </remarks>
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
                    if (validationResult != null)
                    {
                        return validationResult;
                    }
                }

                var pauseCheckBox = CheckBox == "true";
                var selectDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account;
                // 此 legacy 契約暫時要求密碼在背景工作生命週期存活；不得寫入任何一般 trace、Debug 或例外文字。
                var password = InMemoryContext.ListManager.m_Password;
                var loginType = InMemoryContext.ListManager.LoginType;
                var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
                var backgroundCopy = weeklyReportRef?.CreateBackgroundUploadCopy();
                if (backgroundCopy == null)
                {
                    return Json(new { status = "0", message = "無法建立背景上傳快照，請重新整理資料後再試" });
                }

                var operationId = Guid.NewGuid().ToString("N");
                DataverseTrace.Current?.RecordBackgroundAccepted(operationId);
                var weeklyReportData = WeeklyReportData;
                var happyWeekIndex = HappyWeekIndex;
                var happyWeekTopic = HappyWeekTopic;
                var scopeFactory = _scopeFactory;

                _ = Task.Run(async () =>
                {
                    await SaveIntegrateBackgroundUploadRunner.RunAsync(
                        operationId,
                        DataverseTrace.Current,
                        scopeFactory.CreateScope,
                        ToolUtilityFactory.BeginBackgroundScope,
                        serviceProvider => serviceProvider.GetRequiredService<IToolUtilityProvider>(),
                        () => backgroundCopy.UploadIntegrateDataAsync(
                            selectDate,
                            account,
                            password,
                            loginType,
                            backgroundCopy.m_SmallGroupDataList.m_AllMemeberData,
                            weeklyReportData,
                            happyWeekIndex,
                            happyWeekTopic,
                            pauseCheckBox,
                            CancellationToken.None),
                        () => RemoveTransferredMembersFromBackgroundCopy(backgroundCopy),
                        RecordSafeBackgroundFailure).ConfigureAwait(false);
                });

                return Json(new { status = "1", message = "資料已送出，正在背景上傳中...", requiresRefresh = true });
            }
            catch (OperationCanceledException)
            {
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 啟動失敗: {exception.GetType().Name}");
                return HandleError(exception, "SaveIntegrate");
            }
        }

        /// <summary>
        /// 清理背景專屬快照中的已轉介成員。
        /// </summary>
        /// <remarks>
        /// 此方法絕不接受或存取來源 Session 圖；它只在 runner 的 upload 成功後操作已隔離副本，
        /// 因此不需要、也不得取得前景資料圖同步鎖。
        /// </remarks>
        /// <param name="backgroundCopy">本次背景工作唯一擁有的週報快照。</param>
        private static void RemoveTransferredMembersFromBackgroundCopy(ListSmallGroupWeeklyReport backgroundCopy)
        {
            var dataList = backgroundCopy?.m_SmallGroupDataList;
            if (dataList?.m_SmallGroupData?.Members != null)
            {
                RemoveTransferredMembers(dataList.m_SmallGroupData.Members);
            }

            if (dataList?.m_NewPersonFollowUpData?.Members != null)
            {
                RemoveTransferredMembers(dataList.m_NewPersonFollowUpData.Members);
            }
        }

        /// <summary>
        /// 將背景 runner 提供的固定例外型別名稱寫入既有低階診斷管道。
        /// </summary>
        /// <param name="errorType">僅能是例外型別名稱，不得是訊息、stack、帳密或 CRM payload。</param>
        private static void RecordSafeBackgroundFailure(string errorType)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景階段失敗: {errorType}");
            try
            {
                ToolUtilityClass.TraceByLevelStatic(1, 1, $"SaveIntegrate 背景階段失敗: {errorType}");
            }
            catch
            {
                // 診斷故障不得中斷 runner 的 finally/using 清理。
            }
        }

        /// <summary>驗證幸福小組上傳所需的週次與主題欄位。</summary>
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

        /// <summary>從背景副本清單移除已指派或已轉介的成員。</summary>
        private static void RemoveTransferredMembers(List<Member> members)
        {
            if (members == null || members.Count == 0)
            {
                return;
            }

            for (var index = members.Count - 1; index >= 0; index--)
            {
                if (ShouldRemoveMember(members[index]))
                {
                    members.RemoveAt(index);
                }
            }
        }

        /// <summary>判斷背景副本中的成員是否已指派或轉介而應從目前名單移除。</summary>
        private static bool ShouldRemoveMember(Member member)
        {
            return member != null
                && (!string.IsNullOrEmpty(member.AssignedGroup) || member.FollowUpNextStep == "轉介");
        }

        #endregion
    }
}
