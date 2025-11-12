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
            // 註冊 MyPay 相關服務（新增）
            // ========================================
            services.AddScoped<ChurchReport.Services.MyPayMessageBuilder>();
            services.AddScoped<ChurchReport.Services.MyPayStatusHelper>();
            services.AddScoped<ChurchReport.Services.MyPayFeeTypeHelper>();
            services.AddScoped<ChurchReport.Services.MyPayLogger>();
            services.AddScoped<ChurchReport.Services.MyPayCrmService>();
            services.AddScoped<ChurchReport.Services.MyPayNotificationService>();

            if (Configuration["PAY_PROVIDER"] == "國泰金流")
            {
                services.AddScoped<IPayment, QPayToolkitWrapper>();
            }
            else if (Configuration["PAY_PROVIDER"] == "高鋸金流")
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

            if (env.IsDevelopment()) { app.UseDeveloperExceptionPage(); app.UseBrowserLink(); }
            else { app.UseExceptionHandler("/Home/Error"); }

            app.UseStaticFiles();
            app.UseSession();
            app.UseAuthentication();
            app.UseMvc(routes => { routes.MapRoute("default", "{controller=Home}/{action=Login}/{id?}"); });
        }
    }
}
