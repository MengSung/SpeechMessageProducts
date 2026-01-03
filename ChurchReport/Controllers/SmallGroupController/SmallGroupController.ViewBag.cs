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
