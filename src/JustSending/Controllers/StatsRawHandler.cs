using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using JustSending.Data;
using Microsoft.AspNetCore.Mvc;

namespace JustSending.Controllers
{
    [Route("stats/raw")]
    public class StatsRawHandler : BaseEndpoint 
                                        .WithoutRequest
                                        .WithResponse<IEnumerable<StatsRawHandler.StatYear>>
    {
        private readonly AppDbContext _dbContext;

        public record StatMonth(string Month, IGrouping<string, Stats> Days);
        public record StatYear(string Year, IEnumerable<StatMonth> Months);


        public StatsRawHandler(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public override ActionResult<IEnumerable<StatYear>> Handle()
        {
            var data = _dbContext
                .Statistics
                .Find(x => x.Id > 1)
                .GroupBy(x => x.Id.ToString()[..2])
                .Select(x => new StatYear(x.Key, x
                    .GroupBy(y => y.Id.ToString().Substring(2, 2))
                    .Select(dayData => new StatMonth(dayData.Key, dayData))));

            return new JsonResult(data);
        }
    }
}