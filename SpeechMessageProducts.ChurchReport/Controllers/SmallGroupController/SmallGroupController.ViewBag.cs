/*
 * 檔案整體說明
 *
 * 目的與定位：小組管理控制器 - ViewBag 設定
 *
 * 主要功能與流程：
 * 本檔宣告 SmallGroupController。
 * 主要操作入口包含 SetupViewBagForSmallGroup、SetupFeeDataListCount、DetermineListId。
 *
 * 使用邊界與維護：
 * 本檔與同名控制器的其他partial共用一次請求的依賴及狀態；授權、Session與資料範圍必須沿既有伺服器邊界驗證。
 * 外部連線及背景作業的owner依注入服務／明確工作入口決定；不可在控制器新增跨請求可變欄位或未追蹤的背景工作。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Cache.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.DataApi.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs 
 * 追查目前工作樹中的合作／使用位置。
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
