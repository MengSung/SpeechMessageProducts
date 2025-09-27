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
            //services.AddSingleton<ITempDataProvider, CookieTempDataProvider>();
            //services.AddCookieTempData();
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            if (Configuration["PAY_PROVIDER"] == "永豐金流")
            {
                // 註冊付款服務，可透過 DI 容器注入。當需要 IQPayToolkit 時，
                // 會提供一個 QPayToolkitWrapper 實作來滿足需求。
                services.AddScoped<IPayment, QPayToolkitWrapper>();
            }
            else if (Configuration["PAY_PROVIDER"] == "高鉅金流")
            {
                // 註冊高鉅金流服務
                services.AddScoped<IPayment, MyPayToolkitWrapper>();
            }
            else if (Configuration["PAY_PROVIDER"] == "台新金流")
            {
                // 註冊高鉅金流服務
                services.AddScoped<IPayment, TspgToolkitWrapper>();

                // 註冊 TSPG Webhook 處理器
                services.AddScoped<TSPGWebhookHandler>();
            }
            else
            { 
                // 預設使用高鉅金流
                services.AddScoped<IPayment, TspgToolkitWrapper>();
                services.AddScoped<TSPGWebhookHandler>();
            }
            //services.AddSession
            //(
            //    options => options.IdleTimeout = TimeSpan.FromMinutes(30)
            //);

            services.AddSession(options =>
            {
                // Set a short timeout for easy testing.
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                //options.IdleTimeout = TimeSpan.FromSeconds(30);
                options.Cookie.HttpOnly = true;
                // Make the session cookie essential
                options.Cookie.IsEssential = true;
            });

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Home/Login";
                //options.LogoutPath = "/Account/LogOff";
                options.LogoutPath = "/Home/Login";
                options.Cookie.Expiration = TimeSpan.FromMinutes(30);
                //options.Cookie.Expiration = TimeSpan.FromSeconds(30);
                options.CookieName = ".ChurchReport.Session";

                options.Cookie.SameSite = SameSiteMode.None;
                options.LoginPath = new Microsoft.AspNetCore.Http.PathString("/Home/Login");
                options.AccessDeniedPath = "/Home/Login";
                options.ReturnUrlParameter = "/Home/Login";
                //options.AccessDeniedPath = "/Home/AccessDenied";
                options.LogoutPath = "/Home/Login";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                //options.ExpireTimeSpan = TimeSpan.FromSeconds(30);//set it less for testing purpose
            });

            // options.Events.OnRedirectToLogin
            //services.ConfigureApplicationCookie(options =>
            //{
            //    options.Cookie.HttpOnly = true;
            //    options.Cookie.Expiration = TimeSpan.FromSeconds(60);
            //    options.LoginPath = "/Account/Login";
            //    options.LogoutPath = "/Account/Logout";
            //    options.AccessDeniedPath = "/Account/AccessDenied";
            //    options.SlidingExpiration = true;
            //});

            //services.AddAuthenticationCore(CookieAuthenticationDefaults.AuthenticationScheme)
            //    .AddCookie(options => {
            //        options.LoginPath = "/login";
            //        options.AccessDeniedPath = "/login";
            //    });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            // 配置中文語系支援
            var supportedCultures = new[]
            {
                new CultureInfo("zh-TW"),
                new CultureInfo("zh-CN"), 
                new CultureInfo("zh"),
                new CultureInfo("en"),
                new CultureInfo("en-US")
            };
            
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("zh-TW"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            });

            app.UseStaticFiles();
            app.UseSession();

            DevExtreme.AspNet.Mvc.Compatibility.Validation.IgnoreRequiredForBoolean = true;

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Login}/{id?}");
                //template: "{controller=Home}/{action=SmallGroupReportView}/{id?}"); 
            });
        }
    }
}
