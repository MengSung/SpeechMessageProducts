// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：SaveIntegrate、ValidateHappyGroupFields、RemoveTransferredMembers、ShouldRemoveMember
// 引用命名空間：ChurchReport.Models、Microsoft.AspNetCore.Mvc、System、System.Collections.Generic、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
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

                // 背景工作絕不可持有 Session 快取物件圖或 HttpContext。此區只在 request 還活著時
                // 讀取必要純量並建立深拷貝；背景 lambda 的唯一可變業務資料是 backgroundCopy，
                // 因此不會把 A 使用者的 Session、profile 或可變 Members 留給後續背景執行緒。
                var selectDate = InMemoryContext.ListManager.m_SelectDate;
                var account = InMemoryContext.ListManager.m_Account;
                // TODO(credential-lifecycle): 此 legacy 上傳契約仍要求明文 password 在背景作業期間存活；
                // 不得寫入 Debug、Trace 或例外訊息。請在既有 appsettings.json 明文密碼與
                // ToolUtilityClass legacy credential fallback 技術債完成時，改為可撤銷的受保護憑證流程。
                var password = InMemoryContext.ListManager.m_Password;
                var loginType = InMemoryContext.ListManager.LoginType;
                var weeklyReportRef = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
                var backgroundCopy = weeklyReportRef?.CreateBackgroundUploadCopy();
                var weeklyReportData = WeeklyReportData;
                var happyWeekIndex = HappyWeekIndex;
                var happyWeekTopic = HappyWeekTopic;
                var scopeFactory = _scopeFactory;

                // 補充說明：在此使用 Task.Run 啟動背景工作。
                // - request cancellation 不會取消已接受的上傳；背景工作以 CancellationToken.None 完整結束。
                // - scopeFactory 是此工作建立之 DI scope 的唯一 owner；using 會在上傳成功、失敗或取消時釋放它。
                // - 不捕獲 Controller、InMemoryContext、weeklyReportRef 或 allMemberData，避免 request 結束後
                //   Session 物件圖仍被背景 closure 保留。F4 的 ExecutionContext 只供 trace 關聯，並不授權讀取 Session。
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 背景 Trace 範圍必須在建立 DI scope 前最外層持有：它只複製父 request 的假名與
                        // 關聯識別，改用新統計物件累計 CRM 耗時，並在工作 finally 離開時確定寫出 bg.end
                        // 及還原 AsyncLocal。此處絕不保存 HttpContext、Session、lease 或 request scope；
                        // 固定 op 名稱是受信任的程式碼 metadata，不能改為使用者輸入，避免診斷檔保留身分資料。
                        using var traceScope = DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload");

                        // 背景工作不得捕獲 request scope 的 ToolUtility。此 scope 由背景工作
                        // 擁有，並在工作完成時釋放，確保 CRM lease 不會跨 request 存活。
                        using var scope = scopeFactory.CreateScope();
                        // IHttpContextAccessor 的 AsyncLocal 會隨 Task.Run 繼承原 request 的
                        // RequestServices；若不先設定 override，legacy UploadIntegrateData 內的
                        // ToolUtilityFactory 會誤用已結束的 request scope。此 override 流入上傳器
                        // 的第二層 Task.Run，並在 scope Dispose 前還原，故背景 scope 是唯一 CRM owner。
                        using var ambientScope = ToolUtilityFactory.BeginBackgroundScope(scope.ServiceProvider);
                        var toolUtilityProvider = scope.ServiceProvider.GetRequiredService<IToolUtilityProvider>();
                        _ = toolUtilityProvider.GetToolUtility();

                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 開始背景上傳...");
                        // 開始背景上傳的調試訊息

                        // 只把背景專屬副本交給上傳器。UploadIntegrateDataAsync 及其第二層非同步 wrapper
                        // 可能改寫 all-member 集合；該集合已屬於副本，絕不會是前景 Session 快取的 Members。
                        if (backgroundCopy != null)
                        {
                            await backgroundCopy.UploadIntegrateDataAsync(
                                selectDate,
                                account,
                                password,
                                loginType,
                                backgroundCopy.m_SmallGroupDataList.m_AllMemeberData,
                                weeklyReportData,
                                happyWeekIndex,
                                happyWeekTopic,
                                pauseCheckBox,
                                CancellationToken.None
                            ).ConfigureAwait(false);
                        }

                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳完成");
                        // 補充說明：背景上傳完成的調試訊息

                        // 使用點盤點超過 30 處，因此採唯讀退路：清理只能修改 backgroundCopy，絕不回寫
                        // Session 快取圖。這避免 14 秒上傳期間的舊快照覆蓋前景 CRUD，也避免 Clear/AddRange
                        // 讓前景讀到半清空集合。副本在 lambda 結束時失去唯一持有者，無跨 request 保留路徑。
                        try
                        {
                            if (backgroundCopy != null)
                            {
                                var dataList = backgroundCopy.m_SmallGroupDataList;
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
                            // 清理的是背景副本；失敗不得回寫前景資料，但 Release 仍須可觀測。只記錄
                            // 例外型別，避免例外訊息把成員資料、帳號或其他受保護內容寫入診斷檔。
                            System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景清理失敗: {ex.GetType().Name}");
                            try
                            {
                                ToolUtilityClass.TraceByLevelStatic(1, 1,
                                    $"SaveIntegrate 背景清理失敗: {ex.GetType().Name}");
                            }
                            catch
                            {
                                // 診斷系統本身失敗不可中斷已隔離的背景收尾；scope 與 trace scope 仍由 using 釋放。
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 背景上傳失敗: {ex.GetType().Name}");

                        try
                        {
                            // catch 位於 using scope 之外；不可再使用已釋放的 Scoped ToolUtility。
                            ToolUtilityClass.TraceByLevelStatic(1, 1,
                                $"SaveIntegrate 背景上傳失敗: {ex.GetType().Name}");
                        }
                        catch
                        {
                            // 追蹤失敗不影響
                        }
                    }
                });

                System.Diagnostics.Debug.WriteLine($"[SaveIntegrate] 立即回應使用者，背景處理中...");
                // 相容欄位 status/message 維持不變；唯讀退路不把背景清理結果寫回目前 Session 快取，
                // 前端收到標記後應重新載入資料，以 CRM／下一次受隔離的 request 為準。
                return Json(new { status = "1", message = "資料已送出，正在背景上傳中...", requiresRefresh = true });
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
        /// 從清單中移除已轉介的成員
        /// </summary>
        private static void RemoveTransferredMembers(List<Member> members)
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
        private static bool ShouldRemoveMember(Member member)
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
