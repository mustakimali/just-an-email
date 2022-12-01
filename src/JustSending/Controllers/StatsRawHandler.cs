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

        public async Task<ActionResult<StatYear[]>> Handle()
        {
            var data = await _dataStore.Get<StatYear[]>("stats");
            if (data == null)
            {
                data = _statContext.GetAll().ToArray();

                await _dataStore.Set("stats", data, TimeSpan.FromHours(1));
            }

            return new JsonResult(data);
        }
    }
}