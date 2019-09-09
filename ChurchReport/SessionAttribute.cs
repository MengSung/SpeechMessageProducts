using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Security;

namespace ChurchReport
{
    public class SessionAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class CheckSessionOutAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            //HttpContext context = HttpContext.Current;

            if (filterContext.HttpContext.Session != null)
            {
                if (filterContext.HttpContext.Session.IsAvailable)
                {
                    string sessionCookie = filterContext.HttpContext.Request.Headers["Cookie"];

                    if ((sessionCookie != null) && (sessionCookie.IndexOf("ChurchReport.Session") >= 0))
                    {
                        FormsAuthentication.SignOut();
                        string redirectTo = "~/Home/Login"; //YOUR LOGIN PAGE HERE
                        if (!string.IsNullOrEmpty(filterContext.HttpContext.Request.ToString()))
                        {
                            //redirectTo = string.Format("~/Account/Login?ReturnUrl={0}", HttpUtility.UrlEncode(context.Request.RawUrl));
                            redirectTo = string.Format("~/Home/Login?ReturnUrl={0}", HttpUtility.UrlEncode(filterContext.HttpContext.Request.ToString()));
                            filterContext.Result = new RedirectResult(redirectTo);
                            return;
                        }

                    }
                }
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
