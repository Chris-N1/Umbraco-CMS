﻿using System;
using System.Web;
using System.Web.Mvc;
using System.Web.UI;
using Umbraco.Core;
using Umbraco.Core.Configuration;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.Cookie;
using Umbraco.Core.Hosting;
using Umbraco.Core.IO;
using Umbraco.Core.Runtime;
using Umbraco.Core.Services;
using Umbraco.Web.Composing;
using Umbraco.Web.Features;
using Umbraco.Web.JavaScript;
using Umbraco.Web.Mvc;
using Umbraco.Web.PublishedCache;
using Umbraco.Web.Trees;
using Constants = Umbraco.Core.Constants;

namespace Umbraco.Web.Editors
{
    [DisableBrowserCache]
    public class PreviewController : Controller
    {
        private readonly UmbracoFeatures _features;
        private readonly IGlobalSettings _globalSettings;
        private readonly IPublishedSnapshotService _publishedSnapshotService;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IUmbracoVersion _umbracoVersion;
        private readonly IContentSettings _contentSettings;
        private readonly IIOHelper _ioHelper;
        private readonly TreeCollection _treeCollection;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly ICookieManager _cookieManager;
        private IRuntimeSettings _runtimeSettings;
        private readonly ISecuritySettings _securitySettings;
        private readonly IRuntimeMinifier _runtimeMinifier;

        public PreviewController(
            UmbracoFeatures features,
            IGlobalSettings globalSettings,
            IPublishedSnapshotService publishedSnapshotService,
            IUmbracoContextAccessor umbracoContextAccessor,
            ILocalizationService localizationService,
            IUmbracoVersion umbracoVersion,
            IContentSettings contentSettings,
            IIOHelper ioHelper,
            TreeCollection treeCollection,
            IHttpContextAccessor httpContextAccessor,
            IHostingEnvironment hostingEnvironment,
            ICookieManager cookieManager,
            IRuntimeSettings settings,
            ISecuritySettings securitySettings,
            IRuntimeMinifier runtimeMinifier)
        {
            _features = features;
            _globalSettings = globalSettings;
            _publishedSnapshotService = publishedSnapshotService;
            _umbracoContextAccessor = umbracoContextAccessor;
            _localizationService = localizationService;
            _umbracoVersion = umbracoVersion;
            _contentSettings = contentSettings ?? throw new ArgumentNullException(nameof(contentSettings));
            _ioHelper = ioHelper ?? throw new ArgumentNullException(nameof(ioHelper));
            _treeCollection = treeCollection;
            _httpContextAccessor = httpContextAccessor;
            _hostingEnvironment = hostingEnvironment;
            _cookieManager = cookieManager;
            _runtimeSettings = settings;
            _securitySettings = securitySettings;
            _runtimeMinifier = runtimeMinifier;
        }

        [UmbracoAuthorize(redirectToUmbracoLogin: true)]
        [DisableBrowserCache]
        public ActionResult Index()
        {
            var availableLanguages = _localizationService.GetAllLanguages();

            var model = new BackOfficePreviewModel(_features, _globalSettings, _umbracoVersion, availableLanguages, _contentSettings, _ioHelper, _treeCollection, _httpContextAccessor, _hostingEnvironment, _runtimeSettings, _securitySettings);

            if (model.PreviewExtendedHeaderView.IsNullOrWhiteSpace() == false)
            {
                var viewEngineResult = ViewEngines.Engines.FindPartialView(ControllerContext, model.PreviewExtendedHeaderView);
                if (viewEngineResult.View == null)
                {
                    throw new InvalidOperationException("Could not find the view " + model.PreviewExtendedHeaderView + ", the following locations were searched: " + Environment.NewLine + string.Join(Environment.NewLine, viewEngineResult.SearchedLocations));
                }
            }

            return View(_globalSettings.Path.EnsureEndsWith('/') + "Views/Preview/" + "Index.cshtml", model);
        }

        /// <summary>
        /// Returns the JavaScript file for preview
        /// </summary>
        /// <returns></returns>
        [MinifyJavaScriptResult(Order = 0)]
        [OutputCache(Order = 1, VaryByParam = "none", Location = OutputCacheLocation.Server, Duration = 5000)]
        public JavaScriptResult Application()
        {
            var files = JavaScriptHelper.OptimizeScriptFiles(HttpContext, JavaScriptHelper.GetPreviewInitialization(), _runtimeMinifier);
            var result = JavaScriptHelper.GetJavascriptInitialization(HttpContext, files, "umbraco.preview", _globalSettings, _ioHelper);

            return JavaScript(result);
        }

        /// <summary>
        /// The endpoint that is loaded within the preview iframe
        /// </summary>
        /// <returns></returns>
        [UmbracoAuthorize]
        public ActionResult Frame(int id, string culture)
        {
            var user = _umbracoContextAccessor.UmbracoContext.Security.CurrentUser;

            var previewToken = _publishedSnapshotService.EnterPreview(user, id);

            Response.Cookies.Set(new HttpCookie(Constants.Web.PreviewCookieName, previewToken));

            // use a numeric url because content may not be in cache and so .Url would fail
            var query = culture.IsNullOrWhiteSpace() ? string.Empty : $"?culture={culture}";
            Response.Redirect($"../../{id}.aspx{query}", true);

            return null;
        }

        public ActionResult End(string redir = null)
        {
            var previewToken = _cookieManager.GetPreviewCookieValue();
            var service = Current.PublishedSnapshotService;
            service.ExitPreview(previewToken);

            System.Web.HttpContext.Current.ExpireCookie(Constants.Web.PreviewCookieName);

            if (Uri.IsWellFormedUriString(redir, UriKind.Relative)
                && redir.StartsWith("//") == false
                && Uri.TryCreate(redir, UriKind.Relative, out Uri url))
            {
                return Redirect(url.ToString());
            }

            return Redirect("/");
        }
    }
}
