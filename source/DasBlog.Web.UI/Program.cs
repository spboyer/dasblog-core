﻿using AppConstants = DasBlog.Core.Common.Constants;
using DasBlog.Services.ConfigFile.Interfaces;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;


namespace DasBlog.Web.UI
{
  public class Program
  {
    public static void Main(string[] args)
    {
      CreateWebHostBuilder(args).Build().Run();
    }

    private static IHostingEnvironment env;
    // this is fairly appalling - no doubt the correct way to do this will present itself given time
    // asp.net core mvc testing insists that a CreateWebHostBuild named method exists
    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
      WebHost.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((hostingContext, configBuilder) =>
        {
          env = hostingContext.HostingEnvironment;
          // tried setting the IConfigurationBuilder.BasePath to Startup.GetDataRoot
          // but this caused the view engine to go looking there for the views and fail to find them
          // Surely not what is supposed to happen?
          configBuilder.AddXmlFile(Path.Combine(Startup.GetDataRoot(env), @"Config/site.config"), optional: true, reloadOnChange: true)
            .AddXmlFile(Path.Combine(Startup.GetDataRoot(env), @"Config/metaConfig.xml"), optional: true, reloadOnChange: true)
            .AddJsonFile(Path.Combine(Startup.GetDataRoot(env), "appsettings.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(Path.Combine(Startup.GetDataRoot(env), $"appsettings.{env.EnvironmentName}.json"), optional: true)
            .AddEnvironmentVariables()
            ;
          MaybeOverrideRootUrl(configBuilder);
          configBuilder.Build();
        })
        .ConfigureLogging(loggingBuilder =>
        {
          loggingBuilder.AddFile(opts => opts.LogDirectory = Path.Combine(Startup.GetDataRoot(env), "Logs"));
          // there is magic afoot:
          //§ inclusion of NetEscapades.Extensions.Logging.RollingFile (which provides the file logger)
          // is sufficent for the logging builder to do the ncessary plumbing.
          // Why can't we at least have a type parameter to give thowe who
          // come after a fighting chance of knowing what's going on.  It would
          // save me having to type this explanation!
        })
        .UseStartup<Startup>();

    /**
		 * usage: set DAS_BLOG_OVERRIDE_ROOT_URL=1 in the environment where the app server runs.
		 *
		 * In production the application is configured via site.config with the url for permalinks .e.g.
		 * https://mikemay.com:8080/.  This is inconvenient during dev and test
		 * if not using iis xpress or some other port forwarding when we mostly want the published
		 * link to be whatever port the app is listening on on local host, e.g. http://localhost:50432/.
		 *
		 * You can avoid this malarkey and achieve the same result by simply changing the Root entry in site.config
		 */
    private static void MaybeOverrideRootUrl(IConfigurationBuilder configBuilder)
    {
      if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppConstants.DasBlogOverrideRootUrl))
        || Environment.GetEnvironmentVariable(AppConstants.DasBlogOverrideRootUrl)?.Trim() != "1")
        return;
      var urlsEnvVar = System.Environment.GetEnvironmentVariable(AppConstants.AspNetCoreUrls);
      if (string.IsNullOrWhiteSpace(urlsEnvVar))
      {
        urlsEnvVar = "http://*:5000/";
      }

      configBuilder.AddInMemoryCollection(new KeyValuePair<string, string>[]
        {new KeyValuePair<string, string>(nameof(ISiteConfig.Root), urlsEnvVar)});
    }
  }
}
