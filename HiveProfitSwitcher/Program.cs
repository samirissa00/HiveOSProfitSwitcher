using HtmlAgilityPack;
using Ionic.Zip;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Globalization;
using System.Threading;

namespace HiveProfitSwitcher
{
    internal class Program
    {
        static string hiveApiKey = "";
        static string farmId = "";
        static double coinThreshold = Convert.ToDouble(0.05);

        static void Main(string[] args)
        {
            //new AppUpdater().HandleUpdate();

            if (String.IsNullOrEmpty(ConfigurationManager.AppSettings["HiveOSApiKey"]) || String.IsNullOrEmpty(ConfigurationManager.AppSettings["HiveFarmId"]))
            {
                Console.WriteLine("Hive API Key and Hive Farm ID are required. Please check the config file.");
            }
            else
            {
                hiveApiKey = ConfigurationManager.AppSettings["HiveOSApiKey"];
                farmId = ConfigurationManager.AppSettings["HiveFarmId"];
                List<String> configuredRigs = new List<string>();

                if (ConfigurationManager.AppSettings["CoinDifferenceThreshold"] != null)
                {
                    coinThreshold = Convert.ToDouble(ConfigurationManager.AppSettings["CoinDifferenceThreshold"]);
                }

                var config = (ProfitSwitchingConfig)ConfigurationManager.GetSection("profitSwitching");
                if (config != null && config.Workers != null && config.Workers.Count > 0)
                {
                    foreach (WorkerElement item in config.Workers)
                    {
                        if (!configuredRigs.Contains(item.Name))
                        {
                            configuredRigs.Add(item.Name);
                        }
                    }
                    RestClient client = new RestClient("https://api2.hiveos.farm/api/v2");
                    RestRequest request = new RestRequest(String.Format("/farms/{0}/workers", farmId));
                    request.AddHeader("Authorization", "Bearer " + hiveApiKey);
                    var response = client.Get(request);
                    dynamic responseContent = JsonConvert.DeserializeObject(response.Content);
                    foreach (var item in responseContent.data)
                    {
                        string workerName = item?.name?.Value;
                        if (configuredRigs.Contains(workerName))
                        {
                            foreach (WorkerElement worker in config.Workers)
                            {
                                if (worker.Name == workerName)
                                {
                                    HandleRig(item, worker);
                                }
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No Workers configured. Please check the config file.");
                }  
            }
        }
        static string MinestatUrlBuild(WorkerElement worker)
        {
            var result = "";
            var config = (ProfitSwitchingConfig)ConfigurationManager.GetSection("profitSwitching");
            var minerStatApi = ConfigurationManager.AppSettings["MinerStatApi"] ?? null;
            if (minerStatApi == null ) {

                Console.WriteLine("No MinerStatApi configured : Please check the config file.");
                return null;

            }
            if (config != null && config.Workers != null && config.Workers.Count > 0)
            {
                foreach (WorkerElement workerData in config.Workers)
                {
                    if (worker.Name == workerData.Name)
                    {
                        if (workerData.EnabledCoins != null && workerData.EnabledCoins.Count > 0)
                        {
                            foreach (ConfiguredCoinElement enabledCoin in workerData.EnabledCoins)
                            {                                    
                                var coin = enabledCoin.CoinTicker;
                                minerStatApi = minerStatApi + coin + ",";
                                //Console.WriteLine("minerStatApi : {0}", minerStatApi);

                            }
                        }
                    }
                }
            }
            result = minerStatApi;
            return result.TrimEnd(',');
        }

        static string DetermineCurrentCoin(dynamic item, WorkerElement worker)
        {
            string workerName = item?.name?.Value?.ToString();
            string result = item?.flight_sheet?.name?.Value?.ToString().ToUpper().Replace(String.Format("{0}_", workerName.ToUpper().Replace(" ", "_")), String.Empty);
            var config = (ProfitSwitchingConfig)ConfigurationManager.GetSection("profitSwitching");
            if (config != null && config.Workers != null && config.Workers.Count > 0)
            {
                foreach (WorkerElement workerData in config.Workers)
                {
                    if (worker.Name == workerData.Name)
                    {
                        if (workerData.EnabledCoins != null && workerData.EnabledCoins.Count > 0)
                        {
                            foreach (ConfiguredCoinElement enabledCoin in workerData.EnabledCoins)
                            {
                                if (item?.flight_sheet?.name?.Value?.ToString().ToUpper() == enabledCoin.FlightSheetName.ToUpper())
                                {
                                    result = enabledCoin.CoinTicker;
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        static void HandleRig(dynamic item, WorkerElement worker)
        {
            CultureInfo us = new CultureInfo("en-US");
            string currentFlighSheetId = item?.flight_sheet?.id?.Value?.ToString();
            string workerName = item?.name?.Value?.ToString();
            
            var btcPrice = 0.01;
            var coinDeskApiUrl = ConfigurationManager.AppSettings["CoinDeskApi"] ?? "https://api.coindesk.com/v1/bpi/currentprice.json";

            RestClient btcRestClient = new RestClient(coinDeskApiUrl);
            RestRequest btcRestRequest = new RestRequest("");
            dynamic btcMarketData = JsonConvert.DeserializeObject(btcRestClient.Get(btcRestRequest).Content);
            btcPrice = Convert.ToDouble(btcMarketData?.bpi?.USD?.rate?.Value ?? 0.01, us.NumberFormat);
            
            string currentCoin = DetermineCurrentCoin(item, worker);
            Dictionary<string, double> coins = new Dictionary<string, double>();
            Dictionary<String, String> configuredCoins = new Dictionary<string, String>();

            Boolean checkMerge = Convert.ToBoolean(ConfigurationManager.AppSettings["CheckMerge"] ?? "false" );


            foreach (ConfiguredCoinElement enabledCoin in worker.EnabledCoins)
            {
                 if (!configuredCoins.ContainsKey(enabledCoin.CoinTicker))
                 {
                    //Only mine ETH until merge TTD is reached
                    if (checkMerge)
                    {
                        if (enabledCoin.CoinTicker == "ETH")
                        {
                            var ttd = BigInteger.Parse("58750000000000000000000");
                            string url = "https://wenmerge.com/";
                            var web = new HtmlAgilityPack.HtmlWeb();
                            HtmlDocument doc = web.Load(url);
                            Thread.Sleep(5000);
                            var currentTotalDifficulty = doc?.DocumentNode?.SelectNodes("//*[@class=\"wpb_wrapper\"]")?[16]?.SelectNodes("//*[@class=\"uvc-sub-heading ult-responsive\"]")?[4]?.InnerText?.ToString();
                            if (currentTotalDifficulty != null)
                            {
                                if (BigInteger.Parse(currentTotalDifficulty, us.NumberFormat) >= ttd)
                                {
                                    continue;
                                }
                            }

                        }
                    }
                    configuredCoins.Add(enabledCoin.CoinTicker, enabledCoin.FlightSheetName);
                 }
            }
            
            string minerStat =  ConfigurationManager.AppSettings["MinerStat"] ?? "false";
            var minerStatBoolean = Convert.ToBoolean(minerStat);
            var powerPrice = "0.0";
            string urlMinerStat = "";
            if (minerStatBoolean) {

                powerPrice = ConfigurationManager.AppSettings["MinerStatPowerPrice"] ?? "0.1" ;

                urlMinerStat = MinestatUrlBuild(worker);
                RestClient clientMinerstat = new RestClient(urlMinerStat);
                RestRequest requestMinerstat = new RestRequest("");
                var responseMinerstat = clientMinerstat.Get(requestMinerstat);
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                dynamic minerStatResponseContent = JsonConvert.DeserializeObject(responseMinerstat.Content );

                foreach (ConfiguredCoinElement enabledCoin in worker.EnabledCoins)
                {
                    foreach (var coin in minerStatResponseContent)
                    {
                        if (enabledCoin.CoinTicker == coin?.coin?.Value)
                        {
                            if (configuredCoins.ContainsKey(coin?.coin?.Value))
                            {
                                var coinRevenue = coin?.reward?.Value ?? "0.00";
                                var algorithm = coin?.algorithm?.Value;
                                var coinPrice = coin?.price?.Value ?? "0.00";
                                var powerConsumption = 0.00;
                                Console.WriteLine("coinRevenue {0}", coinRevenue);
                                Console.WriteLine("algorithm {0}", algorithm);
                                Console.WriteLine("coinPrice {0}", coinPrice);

                                var msHashrate = enabledCoin.MsHashrate;
                                Console.WriteLine("msHashrate {0}", msHashrate);
                                var msPower = enabledCoin.MsPower;
                                Console.WriteLine("msPower {0}", msPower);
                                var msHashUnit = enabledCoin.MsHashUnit;
                                long calcFactor = 0;
                                Console.WriteLine("msHashUnit {0}", msHashUnit);
                                switch (msHashUnit.ToUpper())
                                {
                                    case "H": calcFactor = 1; break;
                                    case "KH": calcFactor = 1000; break;
                                    case "MH": calcFactor = 1000000; break;
                                    case "GH": calcFactor = 1000000000; break;
                                    case "TH": calcFactor = 1000000000000; break;
                                    case "PH": calcFactor = 1000000000000000; break;
                                    case "EH": calcFactor = 1000000000000000000; break;
                                }
                                                               
                                var dailyPowerCost = 24 * ((Convert.ToDouble(powerConsumption, us.NumberFormat) / 1000) * Convert.ToDouble(powerPrice, us.NumberFormat));
                                //var dailyRevenue = Convert.ToDouble(btcRevenue) * Convert.ToDouble(btcPrice);
                                //var dailyProfit = dailyRevenue - dailyPowerCost;

                                var dailyRevenue = ((Convert.ToDouble(coinRevenue, us.NumberFormat) * (Convert.ToDouble(msHashrate, us.NumberFormat)) * calcFactor) * 24) * Convert.ToDouble(coinPrice, us.NumberFormat);
                                var dailyProfit = dailyRevenue - dailyPowerCost;
                                Console.WriteLine("dailyProfit {0}", dailyProfit);

                                coins.Add(coin?.coin?.Value, dailyProfit);
                            }
                        }
                    }
                    Console.WriteLine("==========================");
                }
            }
            else
            {
                powerPrice = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cost]");
                RestClient client = new RestClient(worker.WTMEndpoint);
                RestRequest request = new RestRequest("");
                var response = client.Get(request);
                dynamic whatToMineResponseContent = JsonConvert.DeserializeObject(response.Content);

                foreach (var coin in whatToMineResponseContent.coins)
                {
                    if (configuredCoins.ContainsKey(coin?.First?.tag?.Value))
                    {
                        var btcRevenue = coin?.First?.btc_revenue?.Value ?? "0.00";
                        var algorithm = coin?.First?.algorithm?.Value;
                        var powerConsumption = "0.00";
                        switch (algorithm.ToUpper())
                        {
                            case "AUTOLYKOS":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[al_p]");
                                break;
                            case "BEAMHASHIII":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[eqb_p]");
                                break;
                            case "CORTEX":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cx_p]");
                                break;
                            case "CRYPTONIGHTFASTV2":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cnf_p]");
                                break;
                            case "CRYPTONIGHTGPU":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cng_p]");
                                break;
                            case "CRYPTONIGHTHAVEN":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cnh_p]");
                                break;
                            case "CUCKAROO29S":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cr29_p]");
                                break;
                            case "CUCKATOO31":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ct31_p]");
                                break;
                            case "CUCKATOO32":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ct32_p]");
                                break;
                            case "CUCKOOCYCLE":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[cc_p]");
                                break;
                            case "EQUIHASH (210,9)":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[eqa_p]");
                                break;
                            case "EQUIHASHZERO":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[eqz_p]");
                                break;
                            case "ETCHASH":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[e4g_p]");
                                break;
                            case "ETHASH":
                                if (coin?.First?.tag?.Value == "ETH" || coin?.First?.tag?.Value == "NICEHASH")
                                {
                                    powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[eth_p]");
                                }
                                else
                                {
                                    powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[e4g_p]");
                                }
                                break;
                            case "FIROPOW":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[fpw_p]");
                                break;
                            case "KAWPOW":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[kpw_p]");
                                break;
                            case "NEOSCRYPT":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ns_p]");
                                break;
                            case "OCTOPUS":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ops_p]");
                                break;
                            case "PROGPOW":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ppw_p]");
                                break;
                            case "PROGPOWZ":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[ppw_p]");
                                break;
                            case "RANDOMX":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[rmx_p]");
                                break;
                            case "UBQHASH":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[e4g_p]");
                                break;
                            case "VERTHASH":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[vh_p]");
                                break;
                            case "X25X":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[x25x_p]");
                                break;
                            case "ZELHASH":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[zlh_p]");
                                break;
                            case "ZHASH":
                                powerConsumption = HttpUtility.ParseQueryString(new Uri(HttpUtility.UrlDecode(worker.WTMEndpoint)).Query).Get("factor[zh_p]");
                                break;
                            default:
                                break;
                        }
                        var dailyPowerCost = 24 * ((Convert.ToDouble(powerConsumption, us.NumberFormat) / 1000) * Convert.ToDouble(powerPrice, us.NumberFormat));
                        var dailyRevenue = Convert.ToDouble(btcRevenue, us.NumberFormat) * Convert.ToDouble(btcPrice, us.NumberFormat);
                        var dailyProfit = dailyRevenue - dailyPowerCost;
                        coins.Add(coin?.First?.tag?.Value, dailyProfit);
                    }
                }

            }

            var currentCoinPrice = coins.Where(x => x.Key == currentCoin).FirstOrDefault().Value;
            var newCoinBestPrice = coins.Values.Max();
            var newTopCoinTicker = coins.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;
            Console.WriteLine("currentCoinPrice {0}", currentCoinPrice);
            Console.WriteLine("newCoinBestPrice {0}", newCoinBestPrice);
            Console.WriteLine("newTopCoinTicker {0}", newTopCoinTicker);

            coinThreshold = Convert.ToDouble(ConfigurationManager.AppSettings["CoinDifferenceThreshold"], us.NumberFormat);
            var teste = currentCoinPrice + (currentCoinPrice * coinThreshold);


            if (newCoinBestPrice > (currentCoinPrice + (currentCoinPrice * coinThreshold)))
            {
                Console.WriteLine("newCoinBestPrice ====  {0} > {1}", newCoinBestPrice, teste);
                Console.WriteLine("newCoinBestPrice --- {0}", newTopCoinTicker);
                string newFlightSheeId = currentFlighSheetId;
                Console.WriteLine("currentFlighSheetId {0}", currentFlighSheetId);
                newFlightSheeId = GetFlightsheetID(configuredCoins[newTopCoinTicker]);
                Console.WriteLine("newFlightSheeId {0}", newFlightSheeId);
                if (!newFlightSheeId.Equals(currentFlighSheetId))
                {
                    Console.WriteLine("changing FlightSheet ... {0}", newFlightSheeId);
                    UpdateFlightSheetID(item?.id?.Value?.ToString(), newFlightSheeId, configuredCoins[newTopCoinTicker], newCoinBestPrice.ToString());
                }
            }
            else {
                Console.WriteLine("newCoinBestPrice ====  {0} < {1}", newCoinBestPrice, teste);
            }
        }

        static string GetFlightsheetID(string flightSheetName)
        {
            string result = "";
            RestClient client = new RestClient("https://api2.hiveos.farm/api/v2");
            RestRequest request = new RestRequest(String.Format("/farms/{0}/fs", farmId));
            request.AddHeader("Authorization", "Bearer " + hiveApiKey);
            var response = client.Get(request);
            dynamic responseContent = JsonConvert.DeserializeObject(response.Content);
            foreach (var item in responseContent.data)
            {
                var fsName = item.name;
                if (!String.IsNullOrEmpty(fsName?.Value) && fsName?.Value == flightSheetName)
                {
                    result = item?.id?.ToString();
                }
            }
            return result;
        }

        static void UpdateFlightSheetID(string workerId, string flightSheetId, string flightSheeName, string profit)
        {
            RestClient client = new RestClient("https://api2.hiveos.farm/api/v2");
            RestRequest request = new RestRequest(String.Format("/farms/{0}/workers/{1}", farmId, workerId));
            request.AddHeader("Authorization", "Bearer " + hiveApiKey);
            var requestBody = new WorkerPatchRequest() { fs_id = flightSheetId };
            request.AddJsonBody(requestBody);
            var response = client.Patch(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                Console.WriteLine(String.Format("Flightsheet Updated to {0}. Estimated Current Profit: ${1}", flightSheeName, Math.Round(Convert.ToDouble(profit),2)));
            }
            else
            {
                Console.WriteLine("Flightsheet Failed to Update");
            }
        }
    }
}
