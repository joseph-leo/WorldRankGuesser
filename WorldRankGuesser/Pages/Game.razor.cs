using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.JSInterop;
using WorldRankGuesser;
using WorldRankGuesser.Shared;
using WorldRankGuesser.Helpers;
using System.Globalization;
using WorldRankGuesser.Data;
using WorldRankGuesser.Services;

namespace WorldRankGuesser.Pages
{
    public partial class Game
    {
        [Inject]
        BasketballService BasketballRankingService { get; set; }

        [Inject]
        BaseballService BaseballService { get; set; }

        [Inject]
        HockeyService HockeyService { get; set; }

        [Inject]
        GymnasticsService GymnasticsService { get; set; }

        [Inject]
        CricketService CricketService { get; set; }

        private RegionInfo DisplayCountry { get; set; } = new RegionInfo("aa-DJ");
        private string Flag { get; set; } = string.Empty;
        private BasketballRank? BasketballRank { get; set; }
        private BaseballRank? BaseballRank { get; set; }
        private HockeyRank? HockeyRank { get; set; }
        private GymnasticsRank? GymnasticsRank { get; set; }
        public CricketRank? CricketRank { get; set; }

        private List<Rank> rankings = new List<Rank>();


        private List<RegionInfo> countries = CountryUtil.GetCountries();

        protected override void OnInitialized()
        {
            RandomizeCountries();           
        }

        //protected override async Task OnInitializedAsync()
        //{
            
        //}

        private void RandomizeCountries()
        {
            Random rng = new();
            rng.Shuffle(countries);
            StateHasChanged();
        }
        private async Task CycleCountriesAsync()
        {
            for (int i = 0; i < 50; i++)
            {
                DisplayCountry = countries[i];
                Flag = CountryUtil.GetFlag(DisplayCountry.TwoLetterISORegionName);
                StateHasChanged();
                await Task.Delay(50);
            }
            
            countries.Remove(DisplayCountry);
            RandomizeCountries();
        }

        private async Task GetBasketballRankAsync(string ISO3)
        {
            BasketballRank = await BasketballRankingService.GetLowestRankAsync(ISO3);
        }

        private async Task GetBaseballRankAsync(string ISO3)
        {
            BaseballRank = await BaseballService.GetLowestRankAsync(ISO3);
        }

        private async Task GetHockeyRankAsync(string ISO3)
        {
            HockeyRank = await HockeyService.GetLowestRankAsync(ISO3);
        }

        private async Task GetGymnasticsRankAsync(string ISO3)
        {
            GymnasticsRank = await GymnasticsService.GetLowestRankAsync(ISO3);
        }

        private async Task GetCricketRankAsync(string ISO3)
        {
            CricketRank = await CricketService.GetLowestRankAsync(ISO3);
        }

        private List<Rank> GetRankings()
        {
            rankings.Add(BasketballRank);
            rankings.Add(BaseballRank);
            rankings.Add(HockeyRank);
            rankings.Add(GymnasticsRank);

            return rankings;
        }
    }
}