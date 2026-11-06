/*
 * 檔案整體說明
 *
 * 目的與定位：小組管理控制器 - CRUD 操作
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * - 新增出席記錄；先驗證目前 Session，再由模型在 instance lock 內以 PresentRecordId 防止重送。 相同 ID 會 fail closed 且不改動集合，不同 ID 即使姓名相同仍可加入。
 * - 更新小組出席記錄，並以資料圖同步根原子更新小組及全部成員資料。
 * 主要操作入口包含 InsertPresentRecord、UpdateSmallGroupPresentRecord、DeletePresentRecord。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
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
        /// 新增出席記錄；先驗證目前 Session，再由模型在 instance lock 內以 PresentRecordId 防止重送。
        /// 相同 ID 會 fail closed 且不改動集合，不同 ID 即使姓名相同仍可加入。
        /// </summary>
        [HttpPost]
        public IActionResult InsertPresentRecord(string values)
        {
            try
            {
                // 重送 POST 不能繞過 Session scope 驗證；InsertMember 會在所屬資料圖的
                // instance lock 內解析並檢查 PresentRecordId，避免同一資料庫記錄被重複 append。
                EnsureCorrectUserData();
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
