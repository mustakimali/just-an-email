using Microsoft.VisualStudio.TestTools.UnitTesting;
using JustSending;
using System.Linq;
using System;
using Microsoft.AspNetCore.TestHost;
using System.Net.Http;
using JustALink;
using Microsoft.AspNetCore;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using System.Reflection;
using System.IO;

namespace JustSending.Test
{
    [TestClass]
    public class IntegrationTest
    {
        private readonly TestServer _server;
        private readonly HttpClient _client;

        public IntegrationTest()
        {
            var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var relativePath = "../../../../../src/JustSending".Replace('/', Path.DirectorySeparatorChar);
            var contentRoot = Path.GetFullPath(relativePath, basePath);

            _server = new TestServer(WebHost.CreateDefaultBuilder()
                    .UseApplicationInsights()
                    .UseContentRoot(contentRoot)
                    .UseStartup<Startup>()
                    .UseEnvironment("Development"));
            _client = _server.CreateClient();
        }

        [TestMethod]
        public async Task TempTest()
        {
            var stats = await _client.GetStringAsync("http://localhost");

            stats.Should().Contain("Mustakim Ali");
        }
    }
}
