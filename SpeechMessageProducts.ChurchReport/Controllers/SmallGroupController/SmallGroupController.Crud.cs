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
        /// 更新小組出席記錄，並以資料圖同步根原子更新小組及全部成員資料。
        /// </summary>
        [HttpPut]
        public IActionResult UpdateSmallGroupPresentRecord(string key, string values)
        {
            try
            {
                // ========================================
                // ✅ 診斷日誌：記錄所有傳入參數
                // ========================================
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] ===== 開始處理更新請求 =====");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Session ID: {HttpContext.Session.Id}");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Key: {key ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Values: {values ?? "(null)"}");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Request Method: {HttpContext.Request.Method}");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Request Path: {HttpContext.Request.Path}");
                System.Diagnostics.Debug.WriteLine($"[UpdateSmallGroupPresentRecord] Content-Type: {HttpContext.Request.ContentType ?? "(null)"}");

                // ========================================
                // ✅ 關鍵修復：驗證 Session 並確保資料正確
                // ========================================
                EnsureCorrectUserData();

                var dataList = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
                    .m_SmallGroupDataList;

                // 同一份 Session 資料圖不得用兩條 Task.Run 平行原地修改。此方法只在短暫
                // 記憶體臨界區完成兩組資料更新；CRM 與其他 I/O 不在鎖內執行。
                dataList.UpdateSmallGroupAndAllMember(key, values);

                return Ok();
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

                // 先在單一同步區間取得及移除前景資料，再於鎖外進行 CRM 刪除，避免網路 I/O
                // 延長快照鎖持有時間或阻塞同一使用者的其他前景更新。
                Member deletedMember = dataList.DeleteMemberFromAllGroups(key);

                if (deletedMember != null)
                {
                    InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.DeleteMemberData(
                        InMemoryContext.ListManager.m_Account,
                        InMemoryContext.ListManager.m_Password,
                        deletedMember
                    );
                }

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
