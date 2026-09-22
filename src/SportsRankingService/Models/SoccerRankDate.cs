using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SportsRankingService.Models
{
    public class SoccerRankDate
    {
        //[JsonProperty("id")]
        public string id { get; set; }

        public string iso { get; set; }

        //[JsonProperty(s)]
        public string dateText { get; set; }

    }
}
