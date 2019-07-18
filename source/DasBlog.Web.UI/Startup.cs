﻿using AutoMapper;
using DasBlog.Core.Common;
using DasBlog.Managers;
using DasBlog.Managers.Interfaces;
using DasBlog.Services.ActivityLogs;
using DasBlog.Services.ConfigFile.Interfaces;
using DasBlog.Web.Identity;
using DasBlog.Web.Settings;
using DasBlog.Web.Mappers;
using DasBlog.Web.Services;
using DasBlog.Web.Services.Interfaces;
using DasBlog.Web.TagHelpers.RichEdit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;
using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using DasBlog.Services.Site;
using DasBlog.Services.ConfigFile;
using DasBlog.Services.Users;
using DasBlog.Services;
using Microsoft.AspNetCore.HttpOverrides;
using System.Collections.Generic;

namespace DasBlog.Web
{
	public class Startup
	{
		public const string SITESECURITYCONFIG = @"Config/siteSecurity.config";
		private IHostingEnvironment hostingEnvironment;
		private string binariesPath;
		public static IServiceCollection DasBlogServices { get; private set; }
			// TODO find out how to access services from integration tests

		public Startup(IConfiguration configuration, IHostingEnvironment env)
		{
			Configuration = configuration;
			hostingEnvironment = env;
			binariesPath = Configuration.GetValue<string>("binariesDir", "/").TrimStart('~').TrimEnd('/');
		}

		public IConfiguration Configuration { get; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddOptions();
			//.NET Core 2.2 adds this, but we'll do a basic one for 2.1, below
			//services.AddHealthChecks();
			services.AddMemoryCache();

			services.Configure<BlogManagerOptions>(Configuration);
			services.Configure<BlogManagerModifiableOptions>(Configuration);
			services.Configure<BlogManagerExtraOptions>(opts =>
				opts.ContentRootPath = GetDataRoot(hostingEnvironment));
			services.Configure<TimeZoneProviderOptions>(Configuration);
			services.Configure<SiteConfig>(Configuration);
			services.Configure<MetaTags>(Configuration);
			services.Configure<LocalUserDataOptions>(options
			  => options.Path = Path.Combine(GetDataRoot(hostingEnvironment), SITESECURITYCONFIG));
			services.Configure<ActivityRepoOptions>(options
			  => options.Path = Path.Combine(GetDataRoot(hostingEnvironment), Constants.LogDirectory));

			//Important if you're using Azure, hosting on Nginx, or behind any reverse proxy
			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders = ForwardedHeaders.All;
				options.AllowedHosts = Configuration.GetValue<string>("AllowedHosts")?.Split(';').ToList<string>();
			});

			// Add identity types
			services
				.AddIdentity<DasBlogUser, DasBlogRole>()
				.AddDefaultTokenProviders();

			services.Configure<IdentityOptions>(options =>
			{
				// Password settings
				options.Password.RequireDigit = true;
				options.Password.RequiredLength = 8;
				options.Password.RequireNonAlphanumeric = false;
				options.Password.RequireUppercase = true;
				options.Password.RequireLowercase = false;
				options.Password.RequiredUniqueChars = 6;

				// Lockout settings
				options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
				options.Lockout.MaxFailedAccessAttempts = 10;
				options.Lockout.AllowedForNewUsers = true;

				// User settings
				options.User.RequireUniqueEmail = true;
			});

			services.ConfigureApplicationCookie(options =>
			{
				options.LoginPath = "/account/login"; // If the LoginPath is not set here, ASP.NET Core will default to /Account/Login
				options.LogoutPath = "/account/logout"; // If the LogoutPath is not set here, ASP.NET Core will default to /Account/Logout
				options.AccessDeniedPath = "/account/accessdenied"; // If the AccessDeniedPath is not set here, ASP.NET Core will default to /Account/AccessDenied
				options.SlidingExpiration = true;
				options.Cookie.Expiration = TimeSpan.FromSeconds(10000);
				options.Cookie = new CookieBuilder
				{
					HttpOnly = true,
					Expiration = TimeSpan.FromDays(30),
				};
			});

			services.AddResponseCaching();

			services.Configure<RazorViewEngineOptions>(rveo =>
			{
				rveo.ViewLocationExpanders.Add(new DasBlogLocationExpander(Configuration.GetSection("Theme").Value));
			});
			services.Configure<RouteOptions>(Configuration);
			
			services.AddSession(options =>
			{
				// Set a short timeout for easy testing.
				options.IdleTimeout = TimeSpan.FromSeconds(1000);
			});

			services
				.AddTransient<IDasBlogSettings, DasBlogSettings>()
				.AddTransient<IUserStore<DasBlogUser>, DasBlogUserStore>()
				.AddTransient<IRoleStore<DasBlogRole>, DasBlogUserRoleStore>()
				.AddTransient<IPrincipal>(provider => provider.GetService<IHttpContextAccessor>().HttpContext.User)
				.AddTransient<ISiteRepairer, SiteRepairer>();

			services.AddScoped<IRichEditBuilder>(SelectRichEditor)
				.AddScoped<IBlogPostViewModelCreator, BlogPostViewModelCreator>();

			services
				.AddSingleton(hostingEnvironment.ContentRootFileProvider)
				.AddSingleton<IBlogManager, BlogManager>()
				.AddSingleton<IArchiveManager, ArchiveManager>()
				.AddSingleton<ICategoryManager, CategoryManager>()
				.AddSingleton<ISiteSecurityManager, SiteSecurityManager>()
				.AddSingleton<IXmlRpcManager, XmlRpcManager>()
				.AddSingleton<ISiteManager, SiteManager>()
				.AddSingleton<IHttpContextAccessor, HttpContextAccessor>()
				.AddSingleton<IFileSystemBinaryManager, FileSystemBinaryManager>()
				.AddSingleton<IUserDataRepo, UserDataRepo>()
				.AddSingleton<ISiteSecurityConfig, SiteSecurityConfig>()
				.AddSingleton<IUserService, UserService>()
				.AddSingleton<IActivityService, ActivityService>()
				.AddSingleton<IActivityRepoFactory, ActivityRepoFactory>()
				.AddSingleton<IEventLineParser, EventLineParser>()
				.AddSingleton<ITimeZoneProvider, TimeZoneProvider>()
				.AddSingleton<ISubscriptionManager, SubscriptionManager>();

			services
				.AddAutoMapper(mapperConfig =>
				{
					var serviceProvider = services.BuildServiceProvider();
					mapperConfig.AddProfile(new ProfilePost(serviceProvider.GetService<IDasBlogSettings>()));
					mapperConfig.AddProfile(new ProfileDasBlogUser(serviceProvider.GetService<ISiteSecurityManager>()));
				})
				.AddMvc()
				.AddXmlSerializerFormatters();
			DasBlogServices = services;
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, IOptions<RouteOptions> routeOptionsAccessor, IDasBlogSettings dasBlogSettings)
		{
			(var siteOk, string siteError) = RepairSite(app);
			if (env.IsDevelopment() || env.IsStaging())
			{
				app.UseDeveloperExceptionPage();
				//app.UseBrowserLink();
			}
			else
			{
				app.UseExceptionHandler("/home/error");
			}

			if (!siteOk)
			{
				app.Run(async context => await context.Response.WriteAsync(siteError));
				return;
			}

			var options = new RewriteOptions()
				 .AddIISUrlRewrite(env.ContentRootFileProvider, @"Config/IISUrlRewrite.xml");

			app.UseRewriter(options);


			//if you've configured it at /blog or /whatever, set that pathbase so ~ will generate correctly
			Uri rootUri = new Uri(dasBlogSettings.SiteConfiguration.Root);
			string path = rootUri.AbsolutePath;

			//Deal with path base and proxies that change the request path
			//https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-2.2#deal-with-path-base-and-proxies-that-change-the-request-path
			if (path != "/")
			{
				app.Use((context, next) =>
				{
					context.Request.PathBase = new PathString(path);
					return next.Invoke();
				});
			}
			app.UseForwardedHeaders();

			app.UseStaticFiles();
			app.UseStaticFiles(new StaticFileOptions()
			{
				FileProvider = new PhysicalFileProvider(Path.Combine(GetDataRoot(env), binariesPath.TrimStart('/'))),
				RequestPath = binariesPath
			});

			app.UseStaticFiles(new StaticFileOptions
			{
				FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "Themes")),
				RequestPath = "/theme"
			});

			app.UseAuthentication();
			app.Use(PopulateThreadCurrentPrincipalForMvc);
			//We'll replace this when we move to ASP.NET Core 2.2+ LTS
			app.Map("/healthcheck", api =>
			{
				api.Run(async (context) =>
				{
					await context.Response.WriteAsync("Healthy");
				});
			});
			app.UseMvc(routes =>
			{
				if (routeOptionsAccessor.Value.EnableTitlePermaLinkUnique)
				{
					routes.MapRoute(
						"Original Post Format",
						"~/{year:int}/{month:int}/{day:int}/{posttitle}.aspx",
						new { controller = "BlogPost", action = "Post", posttitle = "" });

					routes.MapRoute(
						"New Post Format",
						"~/{year:int}/{month:int}/{day:int}/{posttitle}",
						new { controller = "BlogPost", action = "Post", postitle = ""  });

				}
				else
				{
					routes.MapRoute(
						"Original Post Format",
						"~/{posttitle}.aspx",
						new { controller = "BlogPost", action = "Post", posttitle = "" });

					routes.MapRoute(
						"New Post Format",
						"~/{posttitle}",
						new { controller = "BlogPost", action = "Post", postitle = ""  });

				}
				routes.MapRoute(
					name: "default",
					template: "~/{controller=Home}/{action=Index}/{id?}");
			});
		}
		/// <summary>
		/// BlogDataService and DayEntry rely on the thread's CurrentPrincipal and its role to determine if users
		/// should be allowed edit and add posts.
		/// Unfortunately the asp.net team no longer favour an approach involving the current thread so
		/// much as I am loath to stick values on globalish type stuff going up and down the stack
		/// this is a light touch way of including the functionality and actually looks fairly safe.
		/// Hopefully, in the fullness of time we will beautify the legacy code and this can go.
		/// </summary>
		/// <param name="context">provides the user data</param>
		/// <param name="next">standdard middleware - in this case MVC iteelf</param>
		/// <returns></returns>
		private Task PopulateThreadCurrentPrincipalForMvc(HttpContext context, Func<Task> next)
		{
			IPrincipal existingThreadPrincipal = null;
			try
			{
				existingThreadPrincipal = Thread.CurrentPrincipal;
				Thread.CurrentPrincipal = context.User;
				return next();
			}
			finally
			{
				Thread.CurrentPrincipal = existingThreadPrincipal;
			}
		}
		private static (bool result, string errorMessage) RepairSite(IApplicationBuilder app)
		{
			var sr = app.ApplicationServices.GetService<ISiteRepairer>();
			return sr.RepairSite();
		}
		/// <summary>
		/// The intention here is to allow the rich edit control to be changed from request to request.
		/// Before this flexibility is active a "settings" page will be required to make changes to
		/// DasBlogSettings
		/// </summary>
		/// <param name="serviceProvider"></param>
		/// <returns>object used by the taghandlers supporting the rich edit control</returns>
		/// <exception cref="Exception">EntryEditControl in site.config is misconfugured</exception>
		private IRichEditBuilder SelectRichEditor(IServiceProvider serviceProvider)
		{
			string entryEditControl = serviceProvider.GetService<IDasBlogSettings>()
			  .SiteConfiguration.EntryEditControl.ToLower();
			IRichEditBuilder richEditBuilder;
			switch (entryEditControl)
			{
				case Constants.TinyMceEditor:
					richEditBuilder = new TinyMceBuilder();
					break;
				case Constants.NicEditEditor:
					richEditBuilder = new NicEditBuilder();
					break;
				case Constants.TextAreaEditor:
					richEditBuilder = new TextAreaBuilder();
					break;
				default:
					throw new Exception($"Attempt to use unknown rich edit control, {entryEditControl}");
			}

			return richEditBuilder;
		}

		public class RouteOptions
		{
			public bool EnableTitlePermaLinkUnique { get; set; }
		}
		/// <summary>
		/// temporary hack pending the rationalisation of Configuration/Options
		/// </summary>
		/// <param name="env">I think this must be populated in the initialisation of
		///       WebHost.CreateDefaultBuilder</param>
		/// <returns>root locaation for config, logs and blog post content
		///   typically [project]/source/DasBlog.Web.UI for dev
		///   and some subdirectory of DasBlog.Tests for functional tests</returns>
		public static string GetDataRoot(IHostingEnvironment env)
		  => Environment.GetEnvironmentVariable(Constants.DasBlogDataRoot)
		  ?? env.ContentRootPath;
	}
}
