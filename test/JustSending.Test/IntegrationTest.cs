using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace JustSending.Test
{
    [TestFixture]
    public class IntegrationTest : IDisposable
    {
        // ToDo: Use puppeteer-sharp
        private string _seleniumDriverPath;
        private IHost _host;
        private Uri _appHostName;

        public IntegrationTest()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            _seleniumDriverPath = Path.Combine(Path.GetFullPath("../../../", basePath!), "Drivers");
        }

        [OneTimeSetUp]
        public async Task Init()
        {
            await EnsureAppRunning().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        private Task EnsureAppRunning()
        {
            const int port = 5000;
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://*:{port}");
            Directory.SetCurrentDirectory(Path.Join(
                "..", "..", "..", "..", "..", "src", "JustSending"));

            _appHostName = new Uri($"http://localhost:{port}");
            _host = Program.BuildWebHost(new string[] { })
                .Build();
            _host.RunAsync();
            return Task.CompletedTask;
        }

        private IWebDriver CreateDriver()
        {
            // Require an working installation if chrome in dev machine
            // which is installed during docker-build
            return CreateDriverChrome();
        }

        private IWebDriver CreateDriverChrome()
        {
            var chromeOpt = new ChromeOptions();

            chromeOpt.AddArguments("--headless");
            chromeOpt.AddArguments("--disable-gpu");
            chromeOpt.AddArguments("--no-sandbox");

            if (Platform.CurrentPlatform.IsPlatformType(PlatformType.Unix) && !_seleniumDriverPath.EndsWith("Linux")) _seleniumDriverPath += "/Linux";

            var driver = new ChromeDriver(_seleniumDriverPath, chromeOpt);

            return InitialiseDriver(driver);
        }

        private IWebDriver InitialiseDriver(IWebDriver driver)
        {
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(5);
            driver.Navigate().GoToUrl(_appHostName);

            return driver;
        }

        private void Pair(IWebDriver client1, IWebDriver client2)
        {
            var token = client1.FindElement(By.Id("token")).Text;

            client2.FindElement(By.Id("Token")).SendKeys(token);
            client2.FindElement(By.Id("connect")).Click();

            WaitMs(2000);
        }

        private void Navigate(IWebDriver client1, IWebDriver client2)
        {
            client1.FindElement(By.Id("new-session")).Click();

            client2.FindElement(By.Id("connect")).Click();

            WaitMs(2000);
        }

        private void WaitMs(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }

        [Test]
        public void DeleteSession()
        {
            using (var client1 = CreateDriver())
            using (var client2 = CreateDriver())
            {
                Navigate(client1, client2);

                Pair(client1, client2);

                client2
                    .FindElement(By.Id("deleteBtn"))
                    .Click();

                WaitMs(1000);

                client2
                    .FindElement(By.ClassName("confirm"))
                    .Click();

                WaitMs(1000);

                var redirectsToHome = $"{_appHostName}?ref=app";

                client2.Url.Should().Be(redirectsToHome);
                client1.Url.Should().Be(redirectsToHome);
            }
        }

        [Test]
        public void PairMessageExchange()
        {
            using (var client1 = CreateDriver())
            using (var client2 = CreateDriver())
            {
                Navigate(client1, client2);

                Pair(client1, client2);

                WaitMs(2000);

                SendAndVerifyMessage();

                client1.FindElement(By.ClassName("navbar-brand")).Click();
                WaitMs(500);
                client2.FindElement(By.ClassName("confirm")).Click();
                WaitMs(500);
                client2.FindElement(By.ClassName("navbar-brand")).Click();

                #region Local Functions

                void SendAndVerifyMessage()
                {
                    var msgToSend = Guid.NewGuid().ToString("N");

                    client2.FindElement(By.Id("ComposerText")).SendKeys(msgToSend);
                    client2.FindElement(By.ClassName("sendBtn")).Click();

                    WaitMs(1000);

                    var textOnClient1 = client1.FindElement(By.CssSelector(".msg-c span.data")).Text;

                    textOnClient1.Should().Be(msgToSend);
                }

                #endregion
            }
        }

        [Test]
        public void RedirectsToLiteSession()
        {
            using (var client1 = CreateDriver())
            using (var client2 = CreateDriver())
            {
                var id = Guid.NewGuid().ToString("N");
                var id2 = Guid.NewGuid().ToString("N");

                client1.Navigate().GoToUrl($"{_appHostName}app/lite/{id}/{id2}");

                client2.FindElement(By.Id("connect")).Click();

                Pair(client1, client2);

                client2.Url.Should().Be(client1.Url);

                client2.FindElement(By.Id("erase")).Click();

                client2.FindElement(By.Id("erase")).Click();
            }
        }
    }
}