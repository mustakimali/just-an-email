using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace JustSending.Services.TagHelpers
{
    public class CssTagHelper : TagHelper
    {
        private readonly IWebHostEnvironment _env;
        private readonly IMemoryCache _cache;

        public CssTagHelper(IWebHostEnvironment env, IMemoryCache cache)
        {
            _env = env;
            _cache = cache;
        }
        public string Files { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var files = Files
                        .Split(",", StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Replace("~", _env.WebRootPath));

            output.TagName = "style";

            foreach (var path in files)
            {
                var css = _cache.GetOrCreate(path, _ => Regex.Replace(File.ReadAllText(path), "(\r\n|\r|\n)", string.Empty));
                output.Content.AppendHtml(css);
            }
        }
    }
}