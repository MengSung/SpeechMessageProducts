using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;


using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;

namespace ChurchReport
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BuildWebHost(args).Run();
        }
        public static IWebHost BuildWebHost(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .UseKestrel(o =>
                           {
                               o.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
                               o.Limits.MaxRequestBufferSize = null;
                               o.Limits.MaxConcurrentConnections = 1000;
                               o.Limits.MaxConcurrentUpgradedConnections = 1000;
                           }
                )
            .ConfigureKestrel((context, options) =>
                        {
                            options.Limits.MaxConcurrentConnections = 1000;
                            options.Limits.MaxConcurrentUpgradedConnections = 1000;
                            //options.Limits.MaxRequestBodySize = 10 * 1024;
                            //options.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
                            //options.Limits.MinResponseDataRate = new MinDataRate(bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
                            //options.Listen(IPAddress.Loopback, 5000);
                            //options.Listen(IPAddress.Loopback, 5001, listenOptions =>
                            //{
                            //    listenOptions.UseHttps("testCert.pfx", "testPassword");
                            //});
                        }
                )
            .UseContentRoot(Directory.GetCurrentDirectory())
            .UseIISIntegration()
            .UseStartup<Startup>()
            .Build();

    }
}
