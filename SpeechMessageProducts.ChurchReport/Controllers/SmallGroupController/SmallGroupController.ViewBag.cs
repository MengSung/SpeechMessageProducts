/*
 * 檔案整體說明
 *
 * 目的與定位：集中小組畫面需要的ViewBag、費用清單數量及名單ID選擇，讓partial入口共用一致呈現資料；ViewBag屬本次請求，不能成為跨Session資料容器或服務端授權來源。
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * 主要操作入口包含 SetupViewBagForSmallGroup、SetupFeeDataListCount、DetermineListId。
 *
 * 使用邊界與維護：
 * 控制器依賴及狀態限於其明確scope；採partial拆分時各檔共用同一實例，授權、Session及資料範圍須沿伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 */
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 小組管理控制器 - ViewBag 設定
    /// </summary>
    public partial class SmallGroupController
    {
        #region ViewBag 設定

        /// <summary>
        /// 設定小組頁面的 ViewBag 參數
        /// </summary>
        private void SetupViewBagForSmallGroup()
        {
            ViewBag.LoginType = InMemoryContext.ListManager.LoginType;
            ViewBag.LoginFullName = InMemoryContext.ListManager.LoginFullName;
            ViewBag.FeeType = InMemoryContext.FeeList.FeeType;
            ViewBag.HappyType = InMemoryContext.HappyGroupDataManager.HappyType;

            SetupFeeDataListCount();
            SetMultiGroupLayoutParameter();

            ViewBag.SchedulerView = InMemoryContext.ListManager.SchedulerView = "不是單純行事曆";
            ViewBag.DisplayNavigation = InMemoryContext.ListManager.DisplayNavigation = "顯示牧養回報項目";
            ViewBag.UserType = InMemoryContext.ListManager.UserType = InMemoryContext.AppointmentsListManager.UserType;
        }

        /// <summary>
        /// 設定繳費點名資料數量
        /// </summary>
        private void SetupFeeDataListCount()
        {
            if (InMemoryContext.FeeList.FeeDataList != null &&
                InMemoryContext.FeeList.FeeDataList.Count > 0)
            {
                ViewBag.FeeDataListCount = "繳費與點名已有資料";
            }
            else
            {
                ViewBag.FeeDataListCount = "繳費與點名尚無資料";
            }
        }

        /// <summary>
        /// 確定要載入的清單ID
        /// </summary>
        private string DetermineListId(string loginParameter)
        {
            if (loginParameter == "undefined" || loginParameter == "IntegrateView")
            {
                return InMemoryContext.ListManager.ActiveListId;
            }
            return loginParameter;
        }

        #endregion
    }
}
