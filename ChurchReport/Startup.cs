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
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using ToolUtilityNameSpace.DependencyInjection;

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
            // ========================================
            // 註冊 ToolUtility 服務 (Singleton 模式)
            // ========================================
            services.AddToolUtility();

            // Add framework services.
            services
                .AddMvc(options =>
                {
                    // 使用舊的 UseMvc 管線時，需關閉 Endpoint Routing 以消除 MVC1005 警告
                    options.EnableEndpointRouting = false;
                })
                .AddNewtonsoftJson(options =>
                {
                    // 沿用原本的 Newtonsoft 契約解析設定
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                });

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
            else if (Configuration["PAY_PROVIDER"] == "綠界金流")
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

            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Logout";

                    // 新版 API：不要設定 options.Cookie.Expiration，改用 ExpireTimeSpan 控制票證壽命
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

                    // 新版 API：CookieName -> Cookie.Name
                    options.Cookie.Name = ".ChurchReport.Session";
                    options.Cookie.SameSite = SameSiteMode.None;

                    options.AccessDeniedPath = "/Login";
                    options.ReturnUrlParameter = "returnUrl";
                });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            // 創建日誌目錄
            var logsDir = Path.Combine(env.ContentRootPath, "Logs");
            Directory.CreateDirectory(logsDir);
            var tracePath = Path.Combine(logsDir, "Trace.log");

            // 添加文件追蹤監聽器
            if (!Trace.Listeners.OfType<TextWriterTraceListener>().Any(l =>
                (l.Writer as StreamWriter)?.BaseStream is FileStream fs && fs.Name == tracePath))
            {
                Trace.Listeners.Add(new TextWriterTraceListener(tracePath));
                Trace.AutoFlush = true;
            }

            // 異常處理
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                // BrowserLink 在新版 ASP.NET Core 已不支援，移除 app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            // 中間件管道
            app.UseStaticFiles();
            app.UseSession();
            app.UseAuthentication();

            // 使用舊式路由 (已關閉 Endpoint Routing)
            app.UseMvc(routes =>
            {
                // 根路由
                routes.MapRoute(
                    name: "root",
                    template: string.Empty,
                    defaults: new { controller = "Authentication", action = "Login" });

                routes.MapRoute(
                    name: "login",
                    template: "Login",
                    defaults: new { controller = "Authentication", action = "Login" });

                routes.MapRoute(
                    name: "authlogin",
                    template: "Authentication/Login",
                    defaults: new { controller = "Authentication", action = "Login" });

                routes.MapRoute(
                    name: "logout",
                    template: "Logout",
                    defaults: new { controller = "Authentication", action = "Logout" });

                routes.MapRoute(
                    name: "linelogin",
                    template: "Authentication/LineIdLoginView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "Authentication", action = "LineIdLoginView" });

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

                routes.MapRoute(
                    name: "equipmentview",
                    template: "Equipment/EquipmentView",
                    defaults: new { controller = "Equipment", action = "EquipmentView" });

                routes.MapRoute(
                    name: "addnewperson",
                    template: "NewPerson/NewPerson",
                    defaults: new { controller = "NewPerson", action = "NewPerson" });

                routes.MapRoute(
                    name: "newpersonfollowup",
                    template: "NewPerson/FollowUpView",
                    defaults: new { controller = "NewPerson", action = "NewPersonFollowUpView" });

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

                routes.MapRoute(
                    name: "scheduler",
                    template: "Scheduler/{ScheduleType}",
                    defaults: new { controller = "Scheduler", action = "Scheduler" });

                routes.MapRoute(
                    name: "schedulerview",
                    template: "Scheduler/SchedulerView/{SchedulerViewPatameter}",
                    defaults: new { controller = "Scheduler", action = "SchedulerView" });

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

                routes.MapRoute(
                    name: "auditviewline",
                    template: "DedicationAudit/AuditViewLine",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewLine" });

                routes.MapRoute(
                    name: "auditviewweb",
                    template: "DedicationAudit/AuditViewWeb",
                    defaults: new { controller = "DedicationAudit", action = "DedicationFeeAuditViewWeb" });

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

                routes.MapRoute(
                    name: "churchroot",
                    template: "ListManagement/ChurchRoot",
                    defaults: new { controller = "ListManagement", action = "ChurchRoot" });

                routes.MapRoute(
                    name: "paymentsuccess",
                    template: "payment-success",
                    defaults: new { controller = "Home", action = "PaymentSuccess" });

                routes.MapRoute(
                    name: "paymentfailed",
                    template: "payment-failed",
                    defaults: new { controller = "Home", action = "PaymentError" });

                routes.MapRoute(
                    name: "errorview",
                    template: "Home/DisplayErrorView/{ErrorMessage}",
                    defaults: new { controller = "Home", action = "DisplayErrorView" });

                routes.MapRoute(
                    name: "changephone",
                    template: "Phone/ChangePhoneView/{LineIdLoginViewPatameter}",
                    defaults: new { controller = "PhoneBinding", action = "ChangePhoneView" });

                routes.MapRoute(
                    name: "phoneqrcode",
                    template: "Phone/PhoneQrCodeView/{QrCodeViewPatameter}",
                    defaults: new { controller = "PhoneBinding", action = "PhoneQrCodeView" });

                routes.MapRoute(
                    name: "default",
                    template: "{controller=Authentication}/{action=Login}/{id?}");
            });
        }
    }
}
