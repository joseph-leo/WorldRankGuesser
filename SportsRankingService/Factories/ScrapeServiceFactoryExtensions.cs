using SportsRankingService.Parsers;
using SportsRankingService.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Factories
{
    public static class ScrapeServiceFactoryExtensions
    {
        public static void AddParserFactory(this IServiceCollection services)
        {
            services.AddTransient<IParser, BadmintonParser>();
            services.AddTransient<IParser, BaseballParser>();
            services.AddTransient<IParser, BasketballParser>();
            services.AddTransient<IParser, CricketParser>();
            services.AddTransient<IParser, GymnasticsParser>();
            services.AddTransient<IParser, HockeyParser>();
            services.AddTransient<IParser, RugbyParser>();
            services.AddTransient<IParser, SoccerParser>();
            services.AddTransient<IParser, TennisParser>();
            services.AddTransient<IParser, VolleyballParser>();

            services.AddSingleton<Func<IEnumerable<IParser>>>
                (x => () => x.GetService<IEnumerable<IParser>>()!);

            services.AddSingleton<IParserFactory, ParserFactory>();
        }

        public static void AddScrapeServiceFactory(this IServiceCollection services)
        {
            services.AddTransient<IScrapeService, WorldRankService>();

            services.AddSingleton<Func<IEnumerable<IScrapeService>>>
                (x => () => x.GetService<IEnumerable<IScrapeService>>()!);

            services.AddSingleton<IScrapeServiceFactory, ScrapeServiceFactory>();
        }
    }
}
