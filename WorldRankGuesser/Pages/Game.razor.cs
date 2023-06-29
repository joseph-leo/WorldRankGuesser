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
using System.Text.Json;
using System.Diagnostics;

namespace WorldRankGuesser.Pages
{
    public partial class Game
    {
        [Inject]
        private NavigationManager NavigationManager { get; set; }

        [Inject]
        IJSRuntime JSRuntime { get; set; }

        private RegionInfo? DisplayCountry { get; set; }
        private string Flag { get; set; } = string.Empty;

        private List<Rank> Rankings { get; set; } = new List<Rank>();

        private List<RegionInfo> countries = CountryUtil.GetCountries();
        private Dictionary<string, string?> CardFlags { get; set; } = new();

        private bool loading = false;

        

        protected override async Task OnInitializedAsync()
        {
            RandomizeCountries();
            await CycleCountriesAsync();
        }

        private void RandomizeCountries()
        {
            Random rng = new();
            rng.Shuffle(countries);
            StateHasChanged();
        }
        private async Task CycleCountriesAsync()
        {
            loading = true;
            for (int i = 0; i < 50; i++)
            {
                DisplayCountry = countries[i];
                Flag = CountryUtil.GetFlag(DisplayCountry.TwoLetterISORegionName);
                StateHasChanged();
                await Task.Delay(50);
            }
            countries.Remove(DisplayCountry);
            loading = false;

            RandomizeCountries();            
        }

        private async Task GetRankAsync<TService, TRank, TModel>(RegionInfo country) where TService : ScrapeService<TRank, TModel> where TRank : Rank, new()
        {
            string sport = typeof(TRank).Name.Replace("Rank", string.Empty);
            CardFlags[sport] = Flag;

            if (CardFlags.Count < 10)
            {
                await CycleCountriesAsync();
            }          

            var service = Activator.CreateInstance<TService>();
            TRank rank = await service.GetLowestRankAsync(country.ThreeLetterISORegionName);
            rank.Flag = CardFlags[sport];
            Rankings.Add(rank);

            if (Rankings.Count == 10)
            {
                var serializedRankings = JsonSerializer.Serialize(Rankings);
                await JSRuntime.InvokeVoidAsync("sessionStorage.setItem", "rankings", serializedRankings);

                NavigationManager.NavigateTo("/rankings", forceLoad: true);
            }
        }

        private async Task CheckUnrankedCountries<TService, TRank, TModel>() where TService : ScrapeService<TRank, TModel> where TRank : Rank, new()
        {
            
        }

        private List<Type> GetServiceTypes()
        {
            List<Type> services = new List<Type>();
            foreach (Type rank in GetRankTypes())
            {
                services.AddRange(typeof(ScrapeService<Rank, Rank>).Assembly.GetTypes().Where(x => x.IsSubclassOf(typeof(ScrapeService<Rank, Rank>))).ToList());
            }
            return services;
        }

        private IEnumerable<Type> GetRankTypes()
        {
            return typeof(Rank).Assembly.GetTypes().Where(x => x.IsSubclassOf(typeof(Rank)));
        }
    }
}