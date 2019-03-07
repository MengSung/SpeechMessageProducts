using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;


using Microsoft.AspNetCore;

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
                           }
                )
            .UseContentRoot(Directory.GetCurrentDirectory())
            .UseIISIntegration()
            .UseStartup<Startup>()
            .Build();

    }
}
