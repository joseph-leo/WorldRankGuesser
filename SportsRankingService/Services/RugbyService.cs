using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SportsRankingService.Models;
using SportsRankingService.Utilities;
using System.Diagnostics;

namespace SportsRankingService.Services
{
    public class RugbyService : ScrapeService
    {
        public RugbyService(ILogger<RugbyService> logger) : base(logger)
        {
        }

        protected override List<SportsRanking> ParseRanks(string response)
        {
            return Sport switch
            {
                "Rugby Union" => ParseUnion(response),
                "Rugby Sevens" => ParseSevens(response),
                _ => new List<SportsRanking>()
            };
        }

        private List<SportsRanking> ParseUnion(string response)
        {
            try
            {
                List<SportsRanking> rankings = new();
                JObject json = JObject.Parse(response);

                string outerKey = "entries";
                JToken? entries = json[outerKey];

                if (entries is not null)
                {
                    IEnumerable<JToken> results = entries.Children();

                    
                    foreach (JToken result in results)
                    {
                        string teamKey = "team";
                        JToken? team = result[teamKey];

                        if (team is not null)
                        {
                            string countryCodeKey = "countryCode";
                            string countryNameKey = "name";
                            string positionKey = "pos";
                            string? countryCode = team[countryCodeKey]?.Value<string>();
                            string? countryName = team[countryNameKey]?.Value<string>();
                            short? position = result[positionKey]?.Value<short>();

                            if (countryCode != null && countryName != null && position != null)
                            {
                                rankings.Add(new SportsRanking
                                {
                                    Gender = Gender,
                                    Sport = Sport,
                                    ISO3 = CountryUtil.GetISO3FromCode(countryCode, countryName),
                                    Position = position
                                });
                            }
                            else
                            {
                                string message = string.Empty;
                                message += countryCode == null ? LogHelper.NullJsonNodeExceptionMessage(nameof(countryCode), nameof(team), countryCodeKey) : string.Empty;
                                message += countryName == null ? LogHelper.NullJsonNodeExceptionMessage(nameof(countryName), nameof(team), countryNameKey) : string.Empty;
                                message += position == null ? LogHelper.NullJsonNodeExceptionMessage(nameof(position), nameof(team), positionKey) : string.Empty;
                                throw new NullReferenceException(message);
                            }
                        }
                        else
                        {
                            string message = LogHelper.NullJsonNodeExceptionMessage(nameof(team), nameof(result), teamKey);
                            throw new NullReferenceException(message);
                        }
                        
                    }
                }
                else
                {
                    string message = LogHelper.NullJsonNodeExceptionMessage(nameof(entries), nameof(json), outerKey);
                    throw new NullReferenceException(message);
                }
                
                return rankings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                return new List<SportsRanking>();
            }
            
        }

        private List<SportsRanking> ParseSevens(string response)
        {
            try
            {
                HtmlDocument? htmlDocument = new();
                htmlDocument.LoadHtml(response);
                HtmlNode? table = htmlDocument.DocumentNode.SelectSingleNode("//tale");

                List<SportsRanking> rankings = new();

                if (table != null)
                {
                    foreach (var row in table.SelectSingleNode("tbody").SelectNodes("tr"))
                    {
                        List<string> cells = row.SelectNodes("td").Select(x => x.InnerText.Trim()).ToList();
                        string countryCode = cells[1].Split('\n')[1].Trim();
                        countryCode = CountryUtil.IOCToISO3(countryCode);

                        if (cells != null)
                        {
                            if (short.TryParse(cells[0], out short rank))
                            {
                                rankings.Add(new SportsRanking
                                {
                                    ISO3 = CountryUtil.IOCToISO3(countryCode),
                                    Position = rank,
                                    Sport = Sport,
                                    Gender = Gender
                                });
                            }
                        }
                    }
                }
                else
                {
                    throw new NullReferenceException("HTML");
                }

                return rankings;
            }
            catch (Exception ex)
            {
                string message = new StackTrace(ex).GetFrame(0).GetMethod().Name;
                _logger.LogError(ex, "{Message}", message);
                return new List<SportsRanking>();
            }
        }
    }
}
