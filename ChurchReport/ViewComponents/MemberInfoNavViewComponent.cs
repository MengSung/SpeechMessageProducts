// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/ViewComponents/MemberInfoNavViewComponent.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 MemberInfoNavViewComponent 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class MemberInfoNavViewComponent
// 主要成員：Invoke、ResolveAccess
// 引用命名空間：ChurchReport.Models、ChurchReport.Services.MemberInfo、Microsoft.AspNetCore.Http、Microsoft.AspNetCore.Mvc、Microsoft.Xrm.Sdk、ToolUtilityNameSpace.DependencyInjection
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using ChurchReport.Services.MemberInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.ViewComponents
{
    /// <summary>
    /// 會友資訊導覽 ViewComponent。
    ///
    /// 為什麼用 ViewComponent：左側導覽在 _Layout（每頁都套用）。各 controller 設定 ViewBag 的流程不一致——
    /// 例如「回報統計／小組回報」走 SmallGroupController 自己的 ViewBag 流程，不會呼叫 SetupMemberInfoViewBag，
    /// 導致剛登入第一個畫面（回報統計）看不到「會友資訊」。本元件在每頁渲染時自行計算存取等級（必要時載入
    /// 登入連絡人，並把正向結果快取進 Session），確保按鈕在所有頁面、含首次登入畫面即正確顯示。
    ///
    /// IInMemoryDataContext 於 DI 註冊為 Scoped，本元件取得的即是該請求 controller 所用的同一實例。
    /// </summary>
    public class MemberInfoNavViewComponent : ViewComponent
    {
        private readonly IInMemoryDataContext _context;
        private readonly IToolUtilityProvider _toolUtilityProvider;

        public MemberInfoNavViewComponent(
            IInMemoryDataContext context,
            IToolUtilityProvider toolUtilityProvider)
        {
            _context = context;
            _toolUtilityProvider = toolUtilityProvider;
        }

        public IViewComponentResult Invoke()
        {
            return View(model: ResolveAccess());
        }

        private string ResolveAccess()
        {
            try
            {
                var session = HttpContext?.Session;

                // 1) 已有「正向」快取（全教會／牧養名單）→ 直接用。
                var cached = session?.GetString("_MemberInfoAccess");
                if (!string.IsNullOrEmpty(cached))
                {
                    return cached;
                }

                // 2) 已判定過「無權限」→ 不重算（避免一般會友每頁打 CRM）。
                if (session?.GetString("_MemberInfoAccessChecked") == "1")
                {
                    return null;
                }

                // 3) 取得登入連絡人（SmallGroup 頁面通常尚未載入，這裡補載入）。
                var personalModel = _context?.PersonalInfomationModel;
                if (personalModel != null && personalModel.m_LoginContact == null)
                {
                    try
                    {
                        personalModel.SetPersonalInfomationViewModel();
                    }
                    catch
                    {
                        // 載入失敗視為「尚無法判定」→ 不快取，下次再算。
                        return null;
                    }
                }

                var loginContact = personalModel?.m_LoginContact;
                if (loginContact == null)
                {
                    return null; // 尚無法判定，不快取。
                }

                // 4) 由教會職稱 + 登入型別解析存取等級。
                var toolUtility = _toolUtilityProvider.GetToolUtility();
                var jobTitle = toolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
                var loginType = _context?.ListManager?.LoginType ?? string.Empty;
                var access = MemberInfoAccessResolver.Resolve(jobTitle, loginType);

                // 5) 快取結果：正向存入 _MemberInfoAccess（與既有 controller 邏輯相容）；
                //    無權限只標記 _MemberInfoAccessChecked，不污染 _MemberInfoAccess。
                if (!string.IsNullOrEmpty(access))
                {
                    session?.SetString("_MemberInfoAccess", access);
                }
                else
                {
                    session?.SetString("_MemberInfoAccessChecked", "1");
                }

                return access;
            }
            catch
            {
                return null; // 任何意外都不擋頁面，只是不顯示按鈕。
            }
        }
    }
}
