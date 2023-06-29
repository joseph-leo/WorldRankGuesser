using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.JSInterop;
using System.Text.Json;
using WorldRankGuesser.Data;

namespace WorldRankGuesser.Pages
{
    public partial class RankingsPage
    {
        [Inject]
        NavigationManager NavigationManager { get; set; }

        [Inject]
        IJSRuntime JSRuntime { get; set; }

        [Parameter]
        public List<Rank> Rankings { get; set; }

        private int Total { get; set; }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            var serializedRankings = await JSRuntime.InvokeAsync<string>("sessionStorage.getItem", "rankings");
            if (!string.IsNullOrEmpty(serializedRankings))
            {
                Rankings = JsonSerializer.Deserialize<List<Rank>>(serializedRankings);
                await JSRuntime.InvokeVoidAsync("sessionStorage.removeItem", "rankings");
                Total = Rankings.Select(x => x.Position).Sum();
            }           
            StateHasChanged();
        }

        private void NavigateBack()
        {
            NavigationManager.NavigateTo("/game");
        }
    }
}
