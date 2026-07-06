// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：SaveIntegrate、ValidateHappyGroupFields、CleanupTransferredMembers、RemoveTransferredMembers、ShouldRemoveMember
// 引用命名空間：ChurchReport.Models、Microsoft.AspNetCore.Mvc、System、System.Collections.Generic、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
                // 補充說明：此方法採用 Fire-and-Forget 模式，立即回應使用者請求，
                // 然後在背景執行資料上傳和清理，避免阻塞 UI 並提升使用者體驗。
                // 這種模式適合非關鍵性操作，但需注意錯誤處理和資源管理。

                // 如果是快樂小組，則進行欄位驗證
                if (InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.ListEntityName.Contains("快樂"))
                {
                    var validationResult = ValidateHappyGroupFields(HappyWeekIndex, HappyWeekTopic);
                    if (validationResult != null) return validationResult;
                }

                // 將 CheckBox 字串值轉換為布林值
                // 補充說明：
                // - CheckBox 參數從前端傳來時為字串型別，值為 "true" 或 "false"。
                // - 這裡的邏輯是將字串 "true" 轉換為布林值 true，
                //   任何其他值（包括字串 "false"）則轉換為布林值 false。
                // - 轉換後的布林值指示是否需要暫停上傳流程中的某些步驟。
                bool pauseCheckBox = CheckBox == "true";

                // 補充說明：這些變數在背景任務開始前就被捕獲（captured），
                // 避免在 Task.Run 內部存access HttpContext 或 Session，防止 Session Bleeding 問題。
                // Cause: 背景執行緒可能在請求結束後繼續執行，此時 HttpContext 已不可用。
                var selectDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account;
                var password = InMemoryContext.ListManager.m_Password;
                var loginType = InMemoryContext.ListManager.LoginType;
                var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
                var allMemberData = weeklyReportRef?.m_SmallGroupDataList?.m_AllMemeberData;
                var activeListId = InMemoryContext.ListManager.ActiveListId; // 捕獲當前活動名單 ID，背景任務中使用
                // 補充說明：
                // - 此行代碼從 InMemoryContext 捕獲當前活動名單的 ID，並賦值給本地變數 activeListId。
                // - 這樣做的目的是為了在隨後的背景任務中使用該 ID，而無需直接訪問 HttpContext 或 Session。
                // - 捕獲的值會在 Task.Run 的背景執行緒中使用，確保不會受到請求結束後 HttpContext 不可用的問題影響。
                // - 活動名單 ID 可能用於決定資料上傳的目標或篩選要處理的資料，具體取決於業務邏輯。

                // 補充說明：在此使用 Task.Run 啟動背景工作。
                // - 傳入的 cancellationToken 會傳遞到 Task.Run，以便在需要時嘗試取消背景作業。
                // - Task.Run 的 lambda 標示為 async，但內部呼叫的 UploadIntegrateData 可能為同步方法，
                //   因此該呼叫會在執行緒池執行緒上同步執行並可能阻塞，若 UploadIntegrateData 有 I/O 工作，
                //   建議改為真正的非同步實作以避免阻塞執行緒池。
                // - 不要在背景工作中存access HttpContext/Session：因此事先捕獲所需資料到區域變數（上方）。
                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 開始背景上傳...");
                        // 開始背景上傳的調試訊息

                        // Use captured references to avoid accessing HttpContext/Session inside background thread
                        // 呼叫上傳方法。
                        // 注意：如果 UploadIntegrateData 是同步方法，這會在背景執行緒上同步執行並佔用該執行緒。
                        // 若上傳流程包含網路或 I/O，請考慮將 UploadIntegrateData 改寫為 Task-based 非同步方法
                        //（例如 UploadIntegrateDataAsync）並在此使用 await，以提升可伸縮性與執行緒使用效率。
                        // 使用非同步版本以避免在執行緒池同步阻塞
                        if (weeklyReportRef != null)
                        {
                            // 背景任務使用 CancellationToken.None，避免 HTTP 請求結束後背景上傳被取消
                            await weeklyReportRef.UploadIntegrateDataAsync(
                                selectDate,
                                account,
                                password,
                                loginType,
                                allMemberData,
                                WeeklyReportData,
                                HappyWeekIndex,
                                HappyWeekTopic,
                                pauseCheckBox,
                                CancellationToken.None
                            ).ConfigureAwait(false);
                        }

                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳完成");
                        // 補充說明：背景上傳完成的調試訊息

                        // 補充說明：背景清理直接在本地 weeklyReportRef 上執行，避免再度透過 InMemoryContext 存access Session，
                        // 這樣可以減少跨執行緒對 HttpContext/Session 的存取風險。
                        // 清理邏輯會直接修改記憶體中的成員清單（RemoveTransferredMembers），
                        // 因此如果系統同時有其他執行緒也會修改同一集合，請確保有適當的同步機制（鎖定）或採用 thread-safe 的集合。
                        // 在目前設計中，我們假設背景任務為唯一在該時刻修改清單的程式，且後續會再由使用者主流程或定期機制
                        // 將變更持久化至資料庫（若有需要）。
                        try
                        {
                            if (weeklyReportRef != null)
                            {
                                var dataList = weeklyReportRef.m_SmallGroupDataList;
                                if (dataList != null)
                                {
                                    var smallGroupData = dataList.m_SmallGroupData;
                                    if (smallGroupData?.Members != null)
                                    {
                                        RemoveTransferredMembers(smallGroupData.Members);
                                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 已清理小組資料，剩餘 {smallGroupData.Members.Count} 筆");
                                    }

                                    var newPersonData = dataList.m_NewPersonFollowUpData;
                                    if (newPersonData?.Members != null)
                                    {
                                        RemoveTransferredMembers(newPersonData.Members);
                                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 已清理新人跟進資料，剩餘 {newPersonData.Members.Count} 筆");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景清理失敗: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳失敗: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 錯誤堆疊:\n{ex.StackTrace}");

                        try
                        {
                            ToolUtility?.TraceByLevel(1, 1,
                                $"SaveIntegrate 背景上傳失敗: {ex.Message}\n{ex.StackTrace}"); // 追蹤背景上傳失敗的細節
                        }
                        catch
                        {
                            // 追蹤失敗不影響
                        }
                    }
                });

                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 立即回應使用者，背景處理中...");
                return Json(new { status = "1", message = "資料已送出，正在背景上傳中..." });
            }
            catch (OperationCanceledException)
            {
                // 補充說明：當操作被取消時，會捕捉到此異常，
                // 返回表示操作已取消的 JSON 結果。
                return Json(new { status = "0", message = "操作已取消" });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 啟動失敗: {e.Message}");
                // 補充說明：處理其他異常，並返回錯誤處理結果。
                return HandleError(e, "SaveIntegrate"); // 通用錯誤處理
            }
        }

        /// <summary>
        /// 驗證幸福小組必填欄位
        /// </summary>
        private JsonResult ValidateHappyGroupFields(string weekIndex, string topic)
        {
            // 補充說明：此方法檢查幸福小組的必填欄位（第幾週和主題），
            // 如果任一欄位為空，則返回錯誤訊息的 JsonResult。
            // 返回 null 表示驗證通過。
            // 這種設計允許控制器根據驗證結果決定是否繼續處理請求。

            // 檢查 weekIndex 和 topic 是否皆為空
            if (string.IsNullOrEmpty(weekIndex) && string.IsNullOrEmpty(topic))
            {
                // 如果兩個欄位都沒有填寫，返回錯誤訊息，狀態碼為 2
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週和主題" });
            }

            // 檢查 weekIndex 是否為空
            if (string.IsNullOrEmpty(weekIndex))
            {
                // 如果 weekIndex 沒有填寫，返回錯誤訊息，狀態碼為 2
                return Json(new { status = "2", message = "幸福小組必須填寫第幾週" });
            }

            // 檢查 topic 是否為空
            if (string.IsNullOrEmpty(topic))
            {
                // 如果 topic 沒有填寫，返回錯誤訊息，狀態碼為 2
                return Json(new { status = "2", message = "幸福小組必須填寫主題" });
            }

            // 如果所有必填欄位皆已填寫，返回 null，表示驗證通過
            return null;
        }

        /// <summary>
        /// 清理已轉介或指派到其他小組的成員
        /// </summary>
        private void CleanupTransferredMembers()
        {
            // 補充說明：此方法用於清理已轉介或指派到其他小組的成員，
            // 確保資料清單只包含當前小組的有效成員。
            // 這個清理過程在 SaveIntegrate 的背景任務中執行，
            // 以避免阻塞使用者界面並提升整體效能。
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
            // 補充說明：此方法使用手動迴圈移除成員，而不是使用 LINQ 或其他方法，
            // 因為在移除過程中需要修改原始清單，且避免建立新的集合以節省記憶體。
            // 這種方式在清單較大時更有效率，並且在移除過程中保持索引的正確性。
            if (members == null || members.Count == 0)
            {
                return;
            }

            int count = members.Count;
            int index = 0;

            // 補充說明：使用手動迴圈而非 LINQ 等方法，因為要在迴圈中修改集合（移除成員），
            // 使用 foreach 會導致執行期錯誤，因為它也嘗試在迴圈中對集合進行迭代。
            // 此外，手動迴圈提供對索引的明確控制，便於在移除成員時調整。
            // 這段邏輯確保在移除程序中不會因為集合變動而導致的問題。
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
            // 補充說明：此方法根據業務邏輯判斷成員是否應該從清單中移除。
            // 條件1: AssignedGroup 不為空，表示成員已被指派到其他小組。
            // 條件2: FollowUpNextStep 為 "轉介"，表示成員已被轉介到其他地方跟進。
            // 這些條件確保只有當前小組的有效成員保留在清單中。
            if (member == null)
            {
                return false;
            }

            return (!string.IsNullOrEmpty(member.AssignedGroup)) || // 如果成員的 AssignedGroup 欄位有值，表示該成員已被指派到其他小組，應該被移除
                   (member.FollowUpNextStep == "轉介"); // FollowUpNextStep 為 "轉介" 表示成員已被轉介
        }

        #endregion
    }
}
