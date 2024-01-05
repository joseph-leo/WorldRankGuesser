using SportsRankingService.Services.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public static class ScrapeServiceFactoryExtensions
    {
        public static void AddWorldRankServiceFactory(this IServiceCollection services)
        {
            services.AddTransient<WorldRankService, BadmintonService>();
            services.AddTransient<WorldRankService, BaseballService>();
            services.AddTransient<WorldRankService, BasketballService>();
            services.AddTransient<WorldRankService, CricketService>();
            services.AddTransient<WorldRankService, GymnasticsService>();
            services.AddTransient<WorldRankService, HockeyService>();
            services.AddTransient<WorldRankService, RugbyService>();
            services.AddTransient<WorldRankService, SoccerService>();
            services.AddTransient<WorldRankService, TennisService>();
            services.AddTransient<WorldRankService, VolleyballService>();

            services.AddSingleton<Func<IEnumerable<WorldRankService>>>
                (x => () => x.GetService<IEnumerable<WorldRankService>>()!);

            services.AddSingleton<IWorldRankServiceFactory, WorldRankServiceFactory>();
        }
    }
}
