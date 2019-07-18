﻿using DasBlog.Managers.Interfaces;
using DasBlog.Services.Rss.Rss20;
using DasBlog.Services.Rss.Rsd;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.IO;

namespace DasBlog.Web.Controllers
{
    public class FeedController : DasBlogController
    {
        private IMemoryCache memoryCache;
        private readonly ISubscriptionManager subscriptionManager;
		private readonly IXmlRpcManager xmlRpcManager;

        public FeedController(ISubscriptionManager subscriptionManager, IHttpContextAccessor httpContextAccessor,
								IXmlRpcManager xmlRpcManager, IMemoryCache memoryCache)
        {  
            this.subscriptionManager = subscriptionManager;
			this.xmlRpcManager = xmlRpcManager;
			this.memoryCache = memoryCache;
        }

		[Produces("text/xml")]
        [HttpGet("feed/rss")]
        public IActionResult Rss()
        {
			if (!memoryCache.TryGetValue(CACHEKEY_RSS, out RssRoot rss))
			{
				rss = subscriptionManager.GetRss();

				memoryCache.Set(CACHEKEY_RSS, rss, SiteCacheSettings());
			}

			return Ok(rss);
        }

		[Produces("text/xml")]
		[HttpGet("feed/rss/{category}")]
        public IActionResult RssByCategory(string category)
        {

			if (!memoryCache.TryGetValue(CACHEKEY_RSS + "_" + category, out RssRoot rss))
			{
				rss = subscriptionManager.GetRssCategory(category);

				memoryCache.Set(CACHEKEY_RSS + "_" + category, rss, SiteCacheSettings());
			}

			return Ok(rss);
        }

		[Produces("text/xml")]
		[HttpGet("feed/rsd")]
        public ActionResult Rsd()
        {
            RsdRoot rsd = null;

            rsd = subscriptionManager.GetRsd();

            return Ok(rsd);
        }

		[Produces("text/xml")]
		[HttpGet("feed/blogger")]
		public ActionResult Blogger()
		{
			// https://www.poppastring.com/blog/blogger.aspx
			// Implementation of Blogger XML-RPC Api
			// blogger
			// metaWebLog
			// mt

			return NoContent();
		}

		[Produces("text/xml")]
		[HttpPost("feed/blogger")]
		public IActionResult BloggerPost()
		{
			var blogger = string.Empty;

			using (var mem = new MemoryStream())
			{
				Request.Body.CopyTo(mem);
				blogger = xmlRpcManager.Invoke(mem);
			}

			BreakSiteCache();

			return Content(blogger);
		}

		[HttpGet("feed/pingback")]
		public ActionResult PingBack()
		{
			return Ok();
		}

		[HttpGet("feed/rss/comments/{entryid}")]
		public ActionResult RssComments(string entryid)
		{
			return Ok();
		}

		[HttpGet("feed/trackback/{entryid}")]
		public ActionResult TrackBack(string entryid)
		{
			return Ok();
		}

		private void BreakSiteCache()
		{
			memoryCache.Remove(CACHEKEY_RSS);
			memoryCache.Remove(CACHEKEY_FRONTPAGE);
		}
	}
}
