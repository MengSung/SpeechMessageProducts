using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Routing;
using System.Web.Security;
using System.Web.SessionState;
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
