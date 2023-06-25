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
        BasketballService BasketballService { get; set; } = default!;

        [Inject]
        BaseballService BaseballService { get; set; } = default!;

        [Inject]
        HockeyService HockeyService { get; set; } = default!;

        [Inject]
        GymnasticsService GymnasticsService { get; set; } = default!;

        [Inject]
        CricketService CricketService { get; set; } = default!;

        [Inject]
        SoccerService SoccerService { get; set; } = default!;

        [Inject]
        RugbyService RugbyService { get; set; } = default!;

        [Inject]
        VolleyballService VolleyballService { get; set; } = default!;

        [Inject]
        TennisService TennisService { get; set; } = default!;

        [Inject]
        BadmintonService BadmintonService { get; set; } = default!;

        private RegionInfo DisplayCountry { get; set; } = new RegionInfo("aa-DJ");
        private string Flag { get; set; } = string.Empty;
        private BasketballRank? BasketballRank { get; set; }
        private BaseballRank? BaseballRank { get; set; }
        private HockeyRank? HockeyRank { get; set; }
        private GymnasticsRank? GymnasticsRank { get; set; }
        public CricketRank? CricketRank { get; set; }
        public SoccerRank? SoccerRank { get; set; }
        public RugbyRank? RugbyRank { get; set; }
        public VolleyballRank? VolleyballRank { get; set; }
        public TennisRank? TennisRank { get; set; }
        public BadmintonRank? BadmintonRank { get; set; }

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
            BasketballRank = await BasketballService.GetLowestRankAsync(ISO3);
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

        private async Task GetSoccerRankAsync(string ISO3)
        {
            SoccerRank = await SoccerService.GetLowestRankAsync(ISO3);
        }

        private async Task GetRugbyRankAsync(string ISO3)
        {
            RugbyRank = await RugbyService.GetLowestRankAsync(ISO3);
        }

        private async Task GetVolleyballRankAsync(string ISO3)
        {
            VolleyballRank = await VolleyballService.GetLowestRankAsync(ISO3);
        }

        private async Task GetTennisRankAsync(string ISO3)
        {
            TennisRank = await TennisService.GetLowestRankAsync(ISO3);
        }

        private async Task GetBadmintonRankAsync(string ISO3)
        {
            BadmintonRank = await BadmintonService.GetLowestRankAsync(ISO3);
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