using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace JustSending.Controllers
{
    [Route("secure-line")]
    public class SecureLineController : Controller
    {
        private static readonly ConcurrentDictionary<string, PostModel> _inMemoryData = new ConcurrentDictionary<string, PostModel>();

        [Route("")]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [Route("message")]
        public IActionResult Post([FromBody][Bind(nameof(PostModel.Id), nameof(PostModel.Data))] PostModel data)
        {
            if (string.IsNullOrEmpty(data.Id) || string.IsNullOrEmpty(data.Data))
            {
                return BadRequest("Must specify Id and Data property");
            }

            data.DateAdded = DateTime.UtcNow;

            _inMemoryData.AddOrUpdate(data.Id, data, (_, b) => b);

            return Created(Url.Action(nameof(Get), "SecureLine", new { id = data.Id }, Request.Protocol), data.Id);
        }

        [HttpGet]
        [Route("message")]
        public IActionResult Get(string id)
        {
            if (!_inMemoryData.Remove(id, out var data))
                return NotFound();

            return Content(data.Data, "application/json");
        }

        public class PostModel
        {
            public string Id { get; set; }
            public string Data { get; set; }
            public DateTime DateAdded { get; set; }
        }
    }
}
