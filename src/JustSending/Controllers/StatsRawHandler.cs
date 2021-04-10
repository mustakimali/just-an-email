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
    public class StatsRawHandler : BaseEndpointAsync
    {
        private readonly StatsDbContext _dbContext;
        private readonly IDataStore _dataStore;

        public record StatMonth(string Month, Stats[] Days);

        public record StatYear(string Year, IEnumerable<StatMonth> Months);


        public StatsRawHandler(StatsDbContext dbContext, IDataStore dataStore)
        {
            _dbContext = dbContext;
            _dataStore = dataStore;
        }

        public async Task<ActionResult<StatYear[]>> Handle()
        {
            var data = await _dataStore.Get<StatYear[]>("stats");
            if (data == null)
            {
                data = _dbContext
                    .Statistics
                    .Find(x => x.Id > 1)
                    .GroupBy(x => x.Id.ToString()[..2])
                    .Select(x => new StatYear(x.Key, x
                        .GroupBy(y => y.Id.ToString().Substring(2, 2))
                        .Select(dayData => new StatMonth(dayData.Key, dayData.ToArray()))))
                    .ToArray();

                await _dataStore.Set("stats", data, TimeSpan.FromHours(1));
            }

            return new JsonResult(data);
        }
    }
}