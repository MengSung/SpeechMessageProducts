using ChurchReport.Tools;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using System;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ChurchReport
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.

            services.AddMvc().AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // ========================================
            // 註冊 MyPay 相關服務
            // ========================================
            services.AddScoped<ChurchReport.Services.MyPayMessageBuilder>();
            services.AddScoped<ChurchReport.Services.MyPayStatusHelper>();
            services.AddScoped<ChurchReport.Services.MyPayFeeTypeHelper>();
            services.AddScoped<ChurchReport.Services.MyPayLogger>();
            services.AddScoped<ChurchReport.Services.MyPayCrmService>();
            services.AddScoped<ChurchReport.Services.MyPayNotificationService>();

            if (Configuration["PAY_PROVIDER"] == "永豐金流")
            {
                services.AddScoped<IPayment, QPayToolkitWrapper>();
            }
            else if (Configuration["PAY_PROVIDER"] == "高鉅金流")
            {
                services.AddScoped<IPayment, MyPayToolkitWrapper>();
            }
            else if (Configuration["PAY_PROVIDER"] == "台新金流")
            {
                services.AddScoped<IPayment, TspgToolkitWrapper>();
                services.AddScoped<TSPGWebhookHandler>();
            }
            else
            { 
                services.AddScoped<IPayment, TspgToolkitWrapper>();
                services.AddScoped<TSPGWebhookHandler>();
            }

            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Home/Login";
                options.LogoutPath = "/Home/Login";
                options.Cookie.Expiration = TimeSpan.FromMinutes(30);
                options.CookieName = ".ChurchReport.Session";
                options.Cookie.SameSite = SameSiteMode.None;
                options.LoginPath = new Microsoft.AspNetCore.Http.PathString("/Home/Login");
                options.AccessDeniedPath = "/Home/Login";
                options.ReturnUrlParameter = "/Home/Login";
                options.LogoutPath = "/Home/Login";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            // 建立 Logs/Trace.log
            var logsDir = Path.Combine(env.ContentRootPath, "Logs");
            Directory.CreateDirectory(logsDir);
            var tracePath = Path.Combine(logsDir, "Trace.log");
            if (!Trace.Listeners.OfType<TextWriterTraceListener>().Any(l =>
                (l.Writer as StreamWriter)?.BaseStream is FileStream fs && fs.Name == tracePath))
            {
                Trace.Listeners.Add(new TextWriterTraceListener(tracePath));
                Trace.AutoFlush = true;
            }

            if (env.IsDevelopment()) 
            { 
                app.UseDeveloperExceptionPage(); 
                app.UseBrowserLink(); 
            }
            else 
            { 
                app.UseExceptionHandler("/Home/Error"); 
            }

            app.UseStaticFiles();
            app.UseSession();
            app.UseAuthentication();
            
            app.UseMvc(routes => 
            { 
                // ========================================
                // 登入相關路由
                // ========================================
                routes.MapRoute(
                    name: "login",
                    template: "{controller=Home}/{action=Login}");

                routes.MapRoute(
                    name: "linelogin",
                    template: "Home/LineIdLoginView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "Home", action = "LineIdLoginView" });

                // ========================================
                // 小組管理路由
                // ========================================
                routes.MapRoute(
                    name: "multigroup",
                    template: "SmallGroup/MultiGroupView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "MultiGroupView" });

                routes.MapRoute(
                    name: "integrate",
                    template: "SmallGroup/IntegrateView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "IntegrateView" });

                routes.MapRoute(
                    name: "smallgroupreport",
                    template: "SmallGroup/SmallGroupReportView/{LoginParameter?}",
                    defaults: new { controller = "SmallGroup", action = "SmallGroupReportView" });

                // ========================================
                // 裝備狀態管理路由
                // ========================================
                routes.MapRoute(
                    name: "equipmentview",
                    template: "Equipment/EquipmentView",
                    defaults: new { controller = "Equipment", action = "EquipmentView" });

                // ========================================
                // 新人管理路由
                // ========================================
                routes.MapRoute(
                    name: "addnewperson",
                    template: "NewPerson/NewPerson",
                    defaults: new { controller = "NewPerson", action = "NewPerson" });

                routes.MapRoute(
                    name: "newpersonfollowup",
                    template: "NewPerson/FollowUpView",
                    defaults: new { controller = "NewPerson", action = "NewPersonFollowUpView" });

                // ========================================
                // 個人資訊路由
                // ========================================
                routes.MapRoute(
                    name: "personalreport",
                    template: "Personal/Report",
                    defaults: new { controller = "Personal", action = "PersonalReport" });

                routes.MapRoute(
                    name: "personalinfo",
                    template: "Personal/InfomationView",
                    defaults: new { controller = "Personal", action = "PersonalInfomationView" });

                routes.MapRoute(
                    name: "maintainpersonalinfo",
                    template: "Personal/MaintainInfomationView",
                    defaults: new { controller = "Personal", action = "MaintainPersonInfomationView" });

                // ========================================
                // 行事曆路由
                // ========================================
                routes.MapRoute(
                    name: "scheduler",
                    template: "Scheduler/{ScheduleType}",
                    defaults: new { controller = "Scheduler", action = "Scheduler" });

                routes.MapRoute(
                    name: "schedulerview",
                    template: "Scheduler/SchedulerView/{SchedulerViewPatameter}",
                    defaults: new { controller = "Scheduler", action = "SchedulerView" });

                // ========================================
                // 奉獻管理路由
                // ========================================
                routes.MapRoute(
                    name: "qpayview",
                    template: "Dedication/QPayView/{LineId?}",
                    defaults: new { controller = "Dedication", action = "QPayView" });

                routes.MapRoute(
                    name: "dedicationfeeview",
                    template: "Dedication/DedicationFeeView",
                    defaults: new { controller = "Dedication", action = "DedicationFeeView" });

                routes.MapRoute(
                    name: "dedicationfeeviewweb",
                    template: "Dedication/DedicationFeeViewWeb",
                    defaults: new { controller = "Dedication", action = "DedicationFeeViewWeb" });

                routes.MapRoute(
                    name: "keyindedicationfeeview",
                    template: "Dedication/KeyInDedicationFeeView",
                    defaults: new { controller = "Dedication", action = "KeyInDedicationFeeView" });

                routes.MapRoute(
                    name: "dedicationlinelogin",
                    template: "Dedication/DediationLineLoginView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "Dedication", action = "DediationLineLoginView" });

                // ========================================
                // 奉獻稽核路由
                // ========================================
                routes.MapRoute(
                    name: "auditviewline",
                    template: "DedicationAudit/AuditViewLine",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewLine" });

                routes.MapRoute(
                    name: "auditviewweb",
                    template: "DedicationAudit/AuditViewWeb",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewWeb" });

                // ========================================
                // QR Code 路由
                // ========================================
                routes.MapRoute(
                    name: "qrcodeview",
                    template: "QrCode/CourseView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "QrCodeView" });

                routes.MapRoute(
                    name: "pollqrcodeview",
                    template: "QrCode/PollView/{PollQrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PollQrCodeView" });

                routes.MapRoute(
                    name: "smallgroupqrcodeview",
                    template: "QrCode/SmallGroupView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SmallGroupQrCodeView" });

                routes.MapRoute(
                    name: "sundayqrcodeview",
                    template: "QrCode/SundayView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "SundayQrCodeView" });

                routes.MapRoute(
                    name: "personalqrcodeview",
                    template: "QrCode/PersonalView/{QrCodeViewPatameter}",
                    defaults: new { controller = "QrCode", action = "PersonalQrCodeView" });

                // ========================================
                // 名單管理路由
                // ========================================
                routes.MapRoute(
                    name: "churchroot",
                    template: "ListManagement/ChurchRoot",
                    defaults: new { controller = "ListManagement", action = "ChurchRoot" });

                // ========================================
                // 付款結果路由
                // ========================================
                routes.MapRoute(
                    name: "paymentsuccess",
                    template: "payment-success",
                    defaults: new { controller = "Home", action = "PaymentSuccess" });

                routes.MapRoute(
                    name: "paymentfailed",
                    template: "payment-failed",
                    defaults: new { controller = "Home", action = "PaymentError" });

                // ========================================
                // 錯誤頁面路由
                // ========================================
                routes.MapRoute(
                    name: "errorview",
                    template: "Home/DisplayErrorView/{ErrorMessage}",
                    defaults: new { controller = "Home", action = "DisplayErrorView" });

                // ========================================
                // 預設路由 (必須放在最後)
                // ========================================
                routes.MapRoute(
                    name: "default", 
                    template: "{controller=Home}/{action=Login}/{id?}");
            });
        }
    }
}
