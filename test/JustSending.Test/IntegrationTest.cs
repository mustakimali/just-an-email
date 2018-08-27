using JustSending;
using System.Linq;
using System;
using System.Threading;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http;
using JustALink;
using Microsoft.AspNetCore;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using System.IO;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using System.Diagnostics;
using NUnit.Framework;
using OpenQA.Selenium.Firefox;

namespace JustSending.Test
{
    [TestFixture]
    public class IntegrationTest : IDisposable
    {
        private readonly string _seleniumDriverPath;
        private readonly Uri _appHostName = new Uri("http://localhost:5000");
        private readonly string _contentRoot;
        private Process _dotnetProcess;

        public IntegrationTest()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var relativePath = "../../../../../src/JustSending".Replace('/', Path.DirectorySeparatorChar);
            _contentRoot = Path.GetFullPath(relativePath, basePath);

            _seleniumDriverPath = Path.Combine(Path.GetFullPath("../../../", basePath), "Drivers");
        }

        [Test]
        public async Task Integration_1()
        {
            await EnsureAppRunning();

            using (var client1 = CreateDriver())
            using (var client2 = CreateDriver())
            {
                Navigate();

                WaitMs(1000);

                Pair();

                WaitMs(1000);

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

                void Pair()
                {
                    var token = client1.FindElement(By.Id("token")).Text;

                    client2.FindElement(By.Id("Token")).SendKeys(token);
                    client2.FindElement(By.Id("connect")).Click();
                }

                void Navigate()
                {
                    client1.FindElement(By.Id("new-session")).Click();

                    client2.FindElement(By.Id("connect")).Click();
                }

                #endregion
            }
        }

        [TearDown]
        public void Dispose()
        {
            if (_dotnetProcess != null && !_dotnetProcess.HasExited)
            {
                _dotnetProcess.Kill();
            }
        }

        private async Task EnsureAppRunning()
        {
            using (var client = new HttpClient())
            {
                string mode = "Debug";
#if !DEBUG
                mode = "Release";
#endif
                try
                {
                    var homePage = await client.GetStringAsync(_appHostName);

                    if (!homePage.Contains("Mustakim Ali"))
                        Assert.Fail($"Something else is running on {_appHostName}");
                }
                catch (Exception)
                {
                    _dotnetProcess = new Process()
                    {
                        StartInfo = {
                            WorkingDirectory = _contentRoot,
                            FileName = "dotnet",
                            Arguments = $"bin/{mode}/netcoreapp2.1/{(mode == "Release" ? "publish/" : "")}JustSending.dll"
                        }
                    };

                    _dotnetProcess.Start();
                    WaitMs(5000);
                }

            }
        }

        private IWebDriver CreateDriver()
        {
#if DEBUG
            // Developer Machine
            return CreateDriverChrome();
#else
            // AppVeyor
            return CreateDriverFirefox();
#endif
        }

        private IWebDriver CreateDriverChrome()
        {
            ChromeOptions chromeOpt = new ChromeOptions();

#if !DEBUG
            chromeOpt.AddArguments("--headless");
            chromeOpt.AddArguments("--disable-gpu");
#endif

            var driver = new ChromeDriver(_seleniumDriverPath, chromeOpt);

            return InitialiseDriver(driver);
        }

        private IWebDriver CreateDriverFirefox()
        {
            var driverService = FirefoxDriverService.CreateDefaultService();

            driverService.FirefoxBinaryPath = @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe";
            driverService.HideCommandPromptWindow = true;
            driverService.SuppressInitialDiagnosticInformation = true;

            var driver = new FirefoxDriver(driverService, new FirefoxOptions(), TimeSpan.FromSeconds(60));

            return InitialiseDriver(driver);
        }

        private IWebDriver InitialiseDriver(IWebDriver driver)
        {
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(5);
            driver.Navigate().GoToUrl(_appHostName);

            return driver;
        }

        private void WaitMs(int milliseconds)
        {
            System.Threading.Thread.Sleep(milliseconds);
        }
    }
}
