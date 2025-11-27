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
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.ConnectionOperations;

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
            // 註冊 HttpClientFactory (修復記憶體洩漏)
            // ========================================
            services.AddHttpClient();

            // ========================================
            // 🔧 修復：MemoryCache 添加過期策略（不限制大小，避免登入卡住）
            // ========================================
            services.AddMemoryCache(options =>
            {
                // 不設定 SizeLimit，讓系統根據記憶體壓力自動管理
                // 這樣可以避免因快取大小限制導致的登入問題

                // 設定壓縮百分比（當記憶體壓力達到 90% 時才開始清理）
                options.CompactionPercentage = 0.10;

                // 設定掃描頻率（每 5 分鐘掃描一次過期項目）
                options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
            });

            services.AddDistributedMemoryCache();


            // ========================================
            // 註冊 CRM 連接池 (Singleton 模式)
            // ========================================
            services.AddSingleton<ICrmConnectionPool>(sp =>
            {
                var connectionService = new CrmConnectionService();
                var serverUrl = "https://sunnyvalech.speechmessage.com.tw/XRMServices/2011/Organization.svc";
                var username = @"SPEECHMESSAGE\Administrator";
                var password = "hu9840";

                return new CrmConnectionPool(
                    connectionService,
                    serverUrl,
                    username,
                    password,
                    minPoolSize: 3,      // 最小連接數：預先創建 3 個連接
                    maxPoolSize: 20,     // 最大連接數：最多支援 20 個並發連接
                    connectionTimeout: TimeSpan.FromSeconds(30),  // 連接超時：30 秒
                    idleTimeout: TimeSpan.FromMinutes(10)         // 閒置超時：10 分鐘
                );
            });

            // ========================================
            // 🆕 新增：Health Checks（健康檢查）
            // ========================================
            services.AddHealthChecks()
                .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Application is running"))
                .AddCheck("memory", () =>
                {
                    var process = Process.GetCurrentProcess();
                    var memoryMB = process.PrivateMemorySize64 / 1024 / 1024;
                    var maxMemoryMB = 2048; // 2 GB

                    if (memoryMB > maxMemoryMB)
                    {
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                            $"Memory usage too high: {memoryMB} MB");
                    }

                    return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        $"Memory usage normal: {memoryMB} MB");
                });

            services
                .AddMvc(options =>
                {
                    options.EnableEndpointRouting = false;
                })
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                });

            // ========================================
            // 註冊 ToolUtility 服務 (Singleton 模式)
            // ========================================
            services.AddToolUtility();

            // Add framework services.
            services
                .AddMvc(options =>
                {
                    // 使用舊版 UseMvc 路由時，需要禁用 Endpoint Routing 以避免 MVC1005 警告
                    options.EnableEndpointRouting = false;
                })
                .AddNewtonsoftJson(options =>
                {
                    // 保留原本的 Newtonsoft 序列化設定
                    options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                });

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

            // ========================================
            // 🔧 修復：Session 添加更完整的配置
            // ========================================
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.IOTimeout = TimeSpan.FromSeconds(30);
            });

            services
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                    options.LogoutPath = "/Logout";

                    // 新版 API：需要設定 options.Cookie.Expiration，但用 ExpireTimeSpan 即可替代
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

            // ========================================
            // 🆕 新增：Health Check 端點
            // ========================================
            app.UseHealthChecks("/health");

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
