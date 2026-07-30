// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於控制器層，註解重點在說明 HTTP 入口、產品流程邊界、輸入輸出與外部副作用。
// 主要型別：class SmallGroupController
// 主要成員：InsertPresentRecord、UpdateSmallGroupPresentRecord、DeletePresentRecord
// 引用命名空間：ChurchReport.Models、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、System、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 action 的路由來源、權限/Session 前置條件、呼叫的服務，以及回傳 View、JSON 或 redirect 時對使用者流程的影響。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - CRUD 操作
    /// </summary>
    public partial class SmallGroupController
    {
        #region CRUD 操作

        /// <summary>
        /// 新增出席記錄
        /// </summary>
        [HttpPost]
        public IActionResult InsertPresentRecord(string values)
        {
            try
            {
                InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList.m_SmallGroupData.InsertMember(values);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "InsertPresentRecord");
            }
        }

        /// <summary>
        /// 更新小組出席記錄（並行更新兩個資料集）
        /// </summary>
        [HttpPut]
        public async Task<IActionResult> UpdateSmallGroupPresentRecord(
            string key,
            string values,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ========================================
                // ✅ 診斷日誌：記錄所有傳入參數
                // ========================================
                // Session ID、member key 與 update values 都跨越瀏覽器 Session／產品資料信任邊界，
                // 絕不可進入 Debug、Trace 或例外文字。只保留固定事件分類，讓診斷仍可確認 action 已進入，
                // 同時避免 request 結束後由 IDE output、log buffer 或外部 sink 長期保留敏感資料。
                System.Diagnostics.Debug.WriteLine("[UpdateSmallGroupPresentRecord] 已接收更新要求。");

                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                var task1 = Task.Run(() =>
                    dataList.m_SmallGroupData.UpdateMember(key, values),
                    cancellationToken);

                var task2 = Task.Run(() =>
                    dataList.m_AllMemeberData.UpdateMember(key, values),
                    cancellationToken);

                await Task.WhenAll(task1, task2).ConfigureAwait(false);

                return Ok();
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception e)
            {
                return HandleError(e, "UpdateSmallGroupPresentRecord");
            }
        }

        /// <summary>
        /// 刪除出席記錄
        /// </summary>
        [HttpDelete]
        public IActionResult DeletePresentRecord(string key)
        {
            try
            {
                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                Member deletedMember = dataList.m_AllMemeberData.DeleteMember(key);

                if (deletedMember != null)
                {
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        deletedMember
                    );
                }

                dataList.m_SmallGroupData.DeleteMember(key);
                dataList.m_NewPersonFollowUpData.DeleteMember(key);
                dataList.m_HappyGroup.DeleteMember(key);

                return Ok();
            }
            catch (Exception e)
            {
                return HandleError(e, "DeletePresentRecord");
            }
        }

        #endregion
    }
}
