﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using HtmlAgilityPack;
using OpenQA.Selenium.Chrome;
using RestSharp;
using TbsCore.Helpers;
using TbsCore.Models;
using TbsCore.Models.Access;
using TbsCore.Models.AccModels;
using TravBotSharp.Files.Helpers;
using TravBotSharp.Files.Tasks.LowLevel;

namespace TravBotSharp.Files.Models.AccModels
{
    public class WebBrowserInfo : IDisposable
    {
        // Average log length is 70 chars. 20 overhead + 70 * 2 (each char => 2 bytes)
        // Average memory consumption for logs will thus be: 160 * maxLogCnt => ~500kB
        private const int maxLogCnt = 1000;
        private Account acc;

        private ChromeDriverService chromeService;

        public WebBrowserInfo()
        {
            Logs = new CircularBuffer<string>(maxLogCnt);
        }

        public ChromeDriver Driver { get; set; }
        public string CurrentUrl => Driver.Url;
        public HtmlDocument Html { get; set; }

        /// <summary>
        ///     Http client, configured with proxy
        /// </summary>
        public RestClient RestClient { get; set; }

        // Account Logs
        public CircularBuffer<string> Logs { get; set; }

        public void Dispose()
        {
            while (Driver != default)
                try
                {
                    Driver.Close();
                    Driver.Quit(); // Also disposes
                    Driver = default;
                }
                catch
                {
                }

            chromeService.Dispose();
        }

        public event EventHandler LogHandler;

        public void Log(string message, Exception e)
        {
            Log(message + $"\n---------------------------\n{e}\n---------------------------\n");
        }

        public void Log(string msg)
        {
            msg = DateTime.Now.ToString("HH:mm:ss") + ": " + msg;
            Logs.PushFront(msg);

            LogHandler?.Invoke(typeof(WebBrowserInfo), new LogEventArgs {Log = msg});
        }

        public async Task InitSelenium(Account acc, bool newAccess = true)
        {
            this.acc = acc;
            var access = newAccess ? await acc.Access.GetNewAccess() : acc.Access.GetCurrentAccess();

            SetupChromeDriver(access, acc.AccInfo.Nickname, acc.AccInfo.ServerUrl);

            if (Html == null) Html = new HtmlDocument();

            InitHttpClient(access);
            if (!string.IsNullOrEmpty(access.Proxy))
            {
                var checkproxy = new CheckProxy();
                await checkproxy.Execute(acc);
            }
            else
            {
                await Navigate(acc.AccInfo.ServerUrl);
            }
        }

        private void InitHttpClient(Access a)
        {
            RestClient = HttpHelper.InitRestClient(a, acc.AccInfo.ServerUrl);
        }

        private void SetupChromeDriver(Access access, string username, string server)
        {
            var options = new ChromeOptions();

            // Turn on logging preferences for buildings localization (string).
            //var loggingPreferences = new OpenQA.Selenium.Chromium.ChromiumPerformanceLoggingPreferences();
            //loggingPreferences.IsCollectingNetworkEvents = true;
            //options.PerformanceLoggingPreferences = loggingPreferences;
            //options.SetLoggingPreference("performance", LogLevel.All);

            if (!string.IsNullOrEmpty(access.Proxy))
            {
                if (!string.IsNullOrEmpty(access.ProxyUsername))
                {
                    // Add proxy authentication
                    var extensionPath = ProxyHelper.CreateExtension(username, server, access);
                    options.AddExtension(extensionPath);
                }

                options.AddArgument($"--proxy-server={access.Proxy}:{access.ProxyPort}");
                options.AddArgument("ignore-certificate-errors"); // --ignore-certificate-errors ?
            }

            options.AddArgument($"--user-agent={access.UserAgent}");

            //options.AddArguments("--disable-logging");
            //options.AddArguments("--disable-metrics");
            //options.AddArguments("--disable-dev-tools");
            //options.AddArguments("--disable-gpu-shader-disk-cache");
            //options.AddArguments("--aggressive-cache-discard");
            //options.AddArguments("--arc-disable-gms-core-cache");

            // Mute audio because of the Ads
            options.AddArguments("--mute-audio");

            // Make browser headless to preserve memory resources
            if (acc.Settings.HeadlessMode) options.AddArguments("headless");

            // Do not download images in order to preserve memory resources / proxy traffic
            if (acc.Settings.DisableImages)
                options.AddArguments("--blink-settings=imagesEnabled=false"); //--disable-images

            // Add browser caching
            var dir = IoHelperCore.GetCacheDir(username, server, access);
            Directory.CreateDirectory(dir);
            options.AddArguments("user-data-dir=" + dir);

            // Hide command prompt
            chromeService = ChromeDriverService.CreateDefaultService();
            chromeService.HideCommandPromptWindow = true;
            try
            {
                if (acc.Settings.OpenMinimized)
                {
                    options.AddArguments("--window-position=5000,5000");
                    Driver = new ChromeDriver(chromeService, options);
                    Driver.Manage().Window.Position = new Point(200, 200); // TODO: change coords?
                    Driver.Manage().Window.Minimize();
                }
                else
                {
                    Driver = new ChromeDriver(chromeService, options);
                }

                // Set timeout
                Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
            }
            catch (Exception e)
            {
                Log("Error opening chrome driver! Is it already opened?", e);
            }
        }

        internal Dictionary<string, string> GetCookies()
        {
            var cookies = Driver.Manage().Cookies.AllCookies;
            var cookiesDir = new Dictionary<string, string>();
            for (var i = 0; i < cookies.Count; i++) cookiesDir.Add(cookies[i].Name, cookies[i].Value);
            return cookiesDir;
        }

        public async Task Navigate(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            var repeatCnt = 0;
            bool repeat;
            do
            {
                try
                {
                    // Will throw exception after timeout
                    Driver.Navigate().GoToUrl(url);
                    repeat = false;
                }
                catch (Exception e)
                {
                    if (acc.Wb == null) return;
                    acc.Wb.Log(
                        $"Error navigation to {url} - probably due to proxy/Internet or due to chrome still being opened",
                        e);
                    repeat = true;
                    if (5 <= ++repeatCnt && !string.IsNullOrEmpty(acc.Access.GetCurrentAccess().Proxy))
                    {
                        // Change access
                        repeatCnt = 0;
                        var changeAccess = new ChangeAccess();
                        await changeAccess.Execute(acc);
                        await Task.Delay(AccountHelper.Delay() * 5);
                    }

                    await Task.Delay(AccountHelper.Delay());
                }
            } while (repeat);

            await Task.Delay(AccountHelper.Delay());

            UpdateHtml();
            await TaskExecutor.PageLoaded(acc);
        }

        public void UpdateHtml()
        {
            Html.LoadHtml(Driver.PageSource);
        }

        public class LogEventArgs : EventArgs
        {
            public string Log { get; set; }
        }
    }
}