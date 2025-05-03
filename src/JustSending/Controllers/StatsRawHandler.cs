using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using JustSending.Data;
using JustSending.Data.Models.Bson;
using Microsoft.AspNetCore.Mvc;

namespace JustSending.Controllers
{
    [Route("stats/raw")]
    public class StatsRawHandler : ControllerBase
    {
        private readonly StatsDbContext _statContext;
        private readonly IDataStore _dataStore;

        public record StatMonth(string Month, Stats[] Days);

        public record StatYear(string Year, IEnumerable<StatMonth> Months);


        public StatsRawHandler(StatsDbContext dbContext, IDataStore dataStore)
        {
            _statContext = dbContext;
            _dataStore = dataStore;
        }

        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<StatYear[]>> Handle()
        {
            var data = await _statContext.GetAll();

            return new JsonResult(data);
        }
    }
}