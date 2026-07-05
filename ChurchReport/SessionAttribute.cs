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
//using Microsoft.AspNetCore.Http;

namespace ChurchReport
{
    public class SessionAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class CheckSessionOutAttribute : ActionFilterAttribute
    {
        String SessionId = "";
        public async override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            //HttpContext context = HttpContext.Current;

            if (filterContext.HttpContext.Session != null)
            {
                if (SessionId == "")
                {
                    SessionId = filterContext.HttpContext.Session.Id;
                    //filterContext.Result = new RedirectResult("~/Home/DisplayErrorView/TEST!");
                    //return;
                }
                else
                {
                    if (SessionId != filterContext.HttpContext.Session.Id)
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
