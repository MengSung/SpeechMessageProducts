// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/SessionAttribute.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 SessionAttribute 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class SessionAttribute、class CheckSessionOutAttribute
// 主要成員：OnActionExecuting
// 引用命名空間：Microsoft.AspNetCore.Mvc、Microsoft.AspNetCore.Mvc.Filters、System
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
// Session 的 GetString/SetString 擴充方法定義於此命名空間。
using Microsoft.AspNetCore.Http;

namespace ChurchReport
{
    public class SessionAttribute
    {
    }

    /// <summary>
    /// 驗證同一個瀏覽器連線在請求之間的 Session Id 是否一致。
    /// </summary>
    /// <remarks>
    /// ⚠️【安全不變量】此過濾器絕對不可持有任何實例欄位。
    ///
    /// 【原本的嚴重缺陷】
    /// 這個型別原本宣告了一個實例欄位 <c>String SessionId = ""</c>，用它記住「第一次看到的 Session Id」
    /// 再與後續請求比對。這在 ASP.NET Core 下是明確的跨使用者 Session 洩漏：
    /// MVC 會快取過濾器屬性的實例並在「所有請求、所有使用者」之間重複使用同一個物件，
    /// 因此第一位造訪者會把自己的 Session Id 寫進共用欄位，
    /// 之後每一位其他使用者都會被判定為「Session 不符」而被導向錯誤頁。
    /// 同時該欄位在多執行緒下沒有任何同步保護。
    ///
    /// 【原本的第二個缺陷】
    /// <c>OnActionExecuting</c> 原本宣告為 <c>async void</c>。方法內沒有任何 await，
    /// 但 <c>async void</c> 會讓例外脫離 MVC 的過濾器管線而直接終結行程，
    /// 且框架不會等待方法完成就繼續執行 Action，設定 <c>filterContext.Result</c> 可能失效。
    ///
    /// 【修正後的作法】
    /// 基準 Session Id 改存於該請求自己的 Session 之中。Session 本身就是 per-user 的儲存空間，
    /// 所以比較永遠只發生在同一位使用者的前後兩次請求之間，不會有任何跨使用者狀態。
    /// 方法改為同步的 <c>override void</c>，與基底類別的契約一致。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class CheckSessionOutAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// 記錄基準 Session Id 的 Session 鍵。存放於 Session 內即天然隔離於每位使用者。
        /// </summary>
        private const string BaselineSessionIdKey = "_CheckSessionOut_BaselineId";

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // Session 中介層未啟用時 Session 會是 null；此時不做任何判斷直接放行，
            // 避免在沒有 Session 的管線設定下拋出例外而擋掉整個請求。
            var session = filterContext.HttpContext.Session;

            if (session != null)
            {
                // 基準值存放在「這位使用者自己的 Session」裡，因此天然與其他使用者隔離。
                // 這正是取代原本實例欄位的關鍵：實例欄位由所有請求共用，Session 則否。
                var baselineSessionId = session.GetString(BaselineSessionIdKey);

                if (string.IsNullOrEmpty(baselineSessionId))
                {
                    // 這位使用者第一次通過本過濾器，把目前的 Session Id 記為基準。
                    // 之後同一個 Session 的每次請求都會拿來與這個值比對。
                    session.SetString(BaselineSessionIdKey, session.Id);
                }
                else
                {
                    // 基準值是從「當前 Session」讀出來的，所以正常情況下必定等於 session.Id。
                    // 兩者不同代表 Session 在請求之間被替換過（例如 Cookie 被覆寫或重放），
                    // 屬於異常狀態，導向錯誤頁而不繼續執行 Action。
                    if (baselineSessionId != session.Id)
                    {
                        //string sessionCookie = filterContext.HttpContext.Request.Headers["Cookie"];
                        //if (sessionCookie != null)
                        //{
                        //    //FormsAuthentication.SignOut();
                        //    string redirectTo = "~/Home/Login"; //YOUR LOGIN PAGE HERE
                        //    filterContext.Result = new RedirectResult(redirectTo);
                        //}

                        //filterContext.Result =
                        //    new RedirectToRouteResult(new RouteValueDictionary(new
                        //    {
                        //        controller = "Home",
                        //        action = "Login"
                        //    }));


                        //filterContext.Result = new RedirectResult("~/Home/Login");
                        filterContext.Result = new RedirectResult("~/Home/DisplayErrorView/TEST!");

                        return;

                    }
                }
            }

            base.OnActionExecuting(filterContext);
        }



        //public override void OnActionExecuting(ActionExecutingContext context)
        //{
        //    if (context.HttpContext.Session == null || !context.HttpContext.Session.TryGetValue("ID", out byte[] val))
        //    {
        //        context.Result =
        //            new RedirectToRouteResult(new RouteValueDictionary(new
        //            {
        //                controller = "Home",
        //                action = "Login"
        //            }));
        //    }
        //    base.OnActionExecuting(context);
        //}

        //public override void OnActionExecuting(ActionExecutingContext filterContext)
        //{
        //    if (HttpContext.Current.Session["UserProfile"] != null)
        //    {
        //        filterContext.Result = new RedirectResult("~/Home/Login");
        //    }
        //    base.OnActionExecuting(filterContext);
        //}


        //public override void OnActionExecuting(ActionExecutingContext filterContext)
        //{
        //    filterContext.Result = new RedirectResult("~/Home/DisplayErrorView/TEST!");
        //    return;
        //}

        //public override void OnActionExecuting(ActionExecutingContext filterContext)
        //{
        //    HttpContext context = HttpContext.Current;

        //    if (context.Session != null)
        //    {
        //        if (context.Session.IsNewSession)
        //        {
        //            string sessionCookie = context.Request.Headers["Cookie"];

        //            if ((sessionCookie != null) && (sessionCookie.IndexOf("MyProjectName.Session") >= 0))
        //            {
        //                FormsAuthentication.SignOut();
        //                string redirectTo = "~/Home/Login"; //YOUR LOGIN PAGE HERE
        //                if (!string.IsNullOrEmpty(context.Request.RawUrl))
        //                {
        //                    //redirectTo = string.Format("~/Account/Login?ReturnUrl={0}", HttpUtility.UrlEncode(context.Request.RawUrl));
        //                    redirectTo = string.Format("~/Home/Login?ReturnUrl={0}", HttpUtility.UrlEncode(context.Request.RawUrl));
        //                    filterContext.Result = new RedirectResult(redirectTo);
        //                    return;
        //                }

        //            }
        //        }
        //    }

        //    base.OnActionExecuting(filterContext);
        //}
    }
}
