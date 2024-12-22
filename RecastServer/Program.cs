using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ModelExporter
{
    public class Program
    {
        public static async Task Main(string[] args)
        {

            // test var md = (new RecastBuilder()).Build(args[0]);
            if (args.Length > 0)
            {
                Storage.VoxelBase = args[0];
            }
            if (args.Length > 1)
            {
                Storage.OrleansBase = args[1];
            }

            // Set the current path to the assembly location if env != dev.
            // The views are not at the same path between dev and prod.
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            const string devEnvironment = "Development";
            var isDev = environment == devEnvironment;
            if (!isDev)
            {
                var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                Directory.SetCurrentDirectory(dir!);
            }

            using var host = CreateWebHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            string listenUrls = "http://0.0.0.0:8879";
            if (args.Length > 2)
                listenUrls = args[2];

            return WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>()
                .UseKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = null;
                    options.Limits.KeepAliveTimeout = TimeSpan.FromHours(1);
                })
                .UseUrls(listenUrls)
                ;
        }
    }
}
