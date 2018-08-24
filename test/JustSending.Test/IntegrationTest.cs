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

namespace JustSending.Test
{
    [TestFixture]
    public class IntegrationTest
    {
        private readonly string _seleniumDriverPath;

        public IntegrationTest()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var relativePath = "../../../../../src/JustSending".Replace('/', Path.DirectorySeparatorChar);
            var contentRoot = Path.GetFullPath(relativePath, basePath);

            _seleniumDriverPath = Path.Combine(Path.GetFullPath("../../../", basePath), "Drivers");
        }

        [Test]
        public void Integration_1()
        {
            using (var client1 = CreateDriver())
            using (var client2 = CreateDriver())
            {
                client1.FindElement(By.Id("new-session")).Click();

                client2.FindElement(By.Id("connect")).Click();

                WaitMs(1000);

                var token = client1.FindElement(By.Id("token")).Text;

                client2.FindElement(By.Id("Token")).SendKeys(token);
                client2.FindElement(By.Id("connect")).Click();

                WaitMs(1000);

                client2.FindElement(By.Id("ComposerText")).SendKeys(Guid.NewGuid().ToString("N"));
                client2.FindElement(By.ClassName("sendBtn")).Click();

                WaitMs(1000);

                client1.FindElement(By.ClassName("navbar-brand")).Click();
                WaitMs(500);
                client2.FindElement(By.ClassName("confirm")).Click();
                WaitMs(500);
                client2.FindElement(By.ClassName("navbar-brand")).Click();
            }
        }

        private IWebDriver CreateDriver()
        {
            var driver = new ChromeDriver(_seleniumDriverPath);

            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(5);
            driver.Navigate().GoToUrl("http://localhost:61452");

            return driver;
        }

        private void WaitMs(int milliseconds)
        {
            System.Threading.Thread.Sleep(milliseconds);
        }
    }
}
