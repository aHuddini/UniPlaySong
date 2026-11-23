using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Playnite.SDK;

namespace UniPlaySong.Downloaders
{
    // Loads a web page through Playnite's offscreen browser, which runs the page's JavaScript, so a Cloudflare bot check
    // ("Just a moment...") can clear itself the way it does in a real browser. Knows pages, not music: a downloader
    // tries its plain HTTP client first and comes here only when it gets the check.
    public sealed class BrowserPageLoader
    {
        private const int ChallengeWaitMs = 15000, PollMs = 500;

        private readonly IWebViewFactory _webViews;
        private readonly string _userAgent;

        public BrowserPageLoader(IWebViewFactory webViews, string userAgent)
        {
            _webViews = webViews ?? throw new ArgumentNullException(nameof(webViews));
            _userAgent = userAgent;
        }

        // The bot check's own page, not the content behind it
        public static bool IsChallenge(string html) =>
            html != null &&
            (html.IndexOf("Just a moment", StringComparison.OrdinalIgnoreCase) >= 0 ||
             html.IndexOf("challenge-platform", StringComparison.OrdinalIgnoreCase) >= 0);

        // The page's HTML once the check has cleared (null if it didn't within 15 s), and the cookies the browser holds
        // for the page's site (the clearance cookie among them), as a Cookie header value. Blocks: call off the UI thread.
        public string LoadHtml(string url, CancellationToken cancellationToken, out string cookieHeader)
        {
            cookieHeader = null;
            IWebView view = null;
            try
            {
                // Created on the UI thread, as Playnite's views are; navigation and reads below block only this thread
                view = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    _webViews.CreateOffscreenView(new WebViewSettings { JavaScriptEnabled = true, UserAgent = _userAgent }));
                view.NavigateAndWait(url);

                var waited = Stopwatch.StartNew();
                var html = view.GetPageSource();
                while (IsChallenge(html) && waited.ElapsedMilliseconds < ChallengeWaitMs && !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(PollMs); // the check runs its script, then reloads into the real page
                    html = view.GetPageSource();
                }

                var host = new Uri(url).Host;
                cookieHeader = string.Join("; ", (view.GetCookies() ?? new List<HttpCookie>())
                    .Where(c => !string.IsNullOrEmpty(c.Domain) && host.EndsWith(c.Domain.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name + "=" + c.Value));
                return IsChallenge(html) ? null : html;
            }
            finally
            {
                if (view != null)
                    System.Windows.Application.Current.Dispatcher.Invoke(() => view.Dispose());
            }
        }
    }
}
