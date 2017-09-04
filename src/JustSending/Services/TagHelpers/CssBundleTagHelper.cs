using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Linq;
using System.IO;

namespace JustSending.Services.TagHelpers
{
    public class CssTagHelper : TagHelper
    {
        private readonly IHostingEnvironment _env;

        public CssTagHelper(IHostingEnvironment env)
        {
            _env = env;
        }
        public string Files { get; set; }
        public override async void Process(TagHelperContext context, TagHelperOutput output)
        {
            var files = Files
                        .Split(",", StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Replace("~", _env.WebRootPath));

            output.TagName = "style";
            output.Content.SetContent(Path.Combine(_env.WebRootPath, @"css\_home.css"));

            return;
            var content = await output.GetChildContentAsync();
            foreach (var file in files)
            {
                content.SetContent(await File.ReadAllTextAsync(file));
            }
        }
    }
}