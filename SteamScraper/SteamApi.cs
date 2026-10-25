using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Unbroken.LaunchBox.Plugins;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace SteamScraper
{
    class SteamApi
    {
        public static string name;
        public static string short_description;
        public static string header_image;
        public static string developer;
        public static string publishers;
        public static string genreListFinal;
        public static string movie;
        public static string release_date;
        private static string plataforma;
        private static string gameTitle;
        private static JArray screenshots;
        private static JArray genres;
        private static DateTime? dateTime;

        public static async Task SteamSearchAsync(string appId)
        {
            using (var httpClient = new HttpClient())
            {
                // Impostiamo un User-Agent per evitare blocchi da parte di Steam
                if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                }

                string gameUrl = "https://store.steampowered.com/api/appdetails?cc=UK&appids=" + appId;
                var json = await httpClient.GetStringAsync(gameUrl);

                JObject jsonContent = JObject.Parse(json);
                //Serialization
                var data = jsonContent[appId]["data"];
                JToken success = jsonContent[appId]["success"];

                if (success.ToString() == "True")
                {
                    Console.WriteLine("Found");
                }
                else
                {
                    SteamDBLink(appId);
                    PluginHelper.DataManager.Save();
                    return;
                }
                name = (string)jsonContent[appId]["data"]["name"];
                short_description = (string)jsonContent[appId]["data"]["short_description"];
                header_image = (string)jsonContent[appId]["data"]["header_image"];
                release_date = (string)jsonContent[appId]["data"]["release_date"]["date"];
                if (jsonContent[appId]["data"]["publishers"] != null)
                {
                    publishers = (string)jsonContent[appId]["data"]["publishers"][0];
                }
                else
                {
                    publishers = null;
                }
                if (jsonContent[appId]["data"]["developers"] != null)
                {
                    developer = (string)jsonContent[appId]["data"]["developers"][0];
                }
                else
                {
                    developer = null;
                }

                // --- LOGICA AGGIORNATA PER IL RECUPERO DEL TRAILER ---
                var movies = jsonContent[appId]["data"]["movies"];
                movie = null;
                if (movies != null && movies.HasValues)
                {
                    var firstMovie = movies[0];
                    // Tenta prima il formato MP4 max, poi 480, poi ripiega sui formati webm
                    movie = (string)(firstMovie["mp4"]?["max"] ?? firstMovie["mp4"]?["480"] 
                                     ?? firstMovie["webm"]?["max"] ?? firstMovie["webm"]?["480"]);
                }

                if (jsonContent[appId]["data"]["screenshots"] != null)
                {
                    screenshots = (JArray)jsonContent[appId]["data"]["screenshots"];
                }
                else
                {
                    screenshots = null;
                }
                if (jsonContent[appId]["data"]["genres"] != null)
                {
                    genres = (JArray)jsonContent[appId]["data"]["genres"];
                    //GenreList
                    List<string> genreList = new List<string>();
                    foreach (var oneGenre in genres)
                    {
                        genreList.Add(oneGenre["description"].ToString());
                    }
                    genreListFinal = string.Join(";", genreList);
                }
                else
                {
                    genres = null;
                }
            }
            string format = @"d MMM, yyyy";

            if (release_date.Length < 11)
            {
                dateTime = null;
                SteamScraper.game.ReleaseDate = dateTime;
            }
            else
            {
                dateTime = DateTime.ParseExact(release_date, format,
                                            CultureInfo.InvariantCulture);
                SteamScraper.game.ReleaseDate = dateTime.Value.Date;
            }

            String sDescFinal = removeHtmlTags(short_description);

            //Set Data
            SteamScraper.game.Title = name;
            SteamScraper.game.Notes = sDescFinal;
            SteamScraper.game.Developer = developer;
            SteamScraper.game.Publisher = publishers;
            SteamScraper.game.GenresString = genreListFinal;

            string path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            plataforma = SteamScraper.game.Platform;
            gameTitle = SteamScraper.game.Title;
            string destMovies = Path.Combine(path, "Videos", plataforma);
            string destImages = Path.Combine(path, "Images", plataforma);
            
            //Download Trailer
            if (movie != null)
            {
                string movieExtension = movie.Contains(".mp4") ? ".mp4" : ".webm";
                string moviePath = Path.Combine(destMovies, CleanFileName(gameTitle) + movieExtension);
                downloadFile(movie, moviePath);
            }
            
            //Download Banner
            string bannerPath = Path.Combine(destImages, "Steam Banner", CleanFileName(gameTitle) + ".jpg");
            downloadFile(header_image, bannerPath);

            //List of Screenshots
            if (screenshots != null)
            {
                var count = 1;
                foreach (var oneSS in screenshots)
                {
                    string screenshotPath = Path.Combine(destImages, "Steam Screenshot", 
                        CleanFileName(gameTitle) + "-" + count.ToString("D2") + ".jpg");
                    downloadFile(oneSS["path_full"].ToString(), screenshotPath);
                    count++;
                }
            }

            //Workaround for the new Steam Images
            string boxCover = "library_600x900_2x.jpg";
            string marquee = "library_hero.jpg";
            string clearLogo = "logo.png";

            var clearLogoType = await CheckURI(clearLogo, appId);
            if (clearLogoType == "image/png")
            {
                string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/" + clearLogo;
                string logoPath = Path.Combine(destImages, "Clear Logo", CleanFileName(gameTitle) + ".png");
                downloadFile(url, logoPath);
            }

            var boxCoverType = await CheckURI(boxCover, appId);
            if (boxCoverType == "image/jpeg")
            {
                string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/" + boxCover;
                string posterPath = Path.Combine(destImages, "Steam Poster", CleanFileName(gameTitle) + ".jpg");
                downloadFile(url, posterPath);
            }

            var marqueeType = await CheckURI(marquee, appId);
            if (marqueeType == "image/jpeg")
            {
                string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/" + marquee;
                string marqueePath = Path.Combine(destImages, "Steam Banner", CleanFileName(gameTitle) + ".jpg");
                downloadFile(url, marqueePath);
            }

            var dllPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var dllPathFinal = Path.Combine(dllPath, "properties.json");
            Properties jsonConfig = new Properties { customFields = "false" };
            if (File.Exists(dllPathFinal))
            {
                try
                {
                    string jsonFile = File.ReadAllText(dllPathFinal);
                    jsonConfig = JsonConvert.DeserializeObject<Properties>(jsonFile) ?? jsonConfig;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not read properties.json, using defaults: {ex.Message}");
                }
            }
            if (jsonConfig.customFields == "true")
            {
                JToken steamSpyTags = await SteamTags.SteamTag(appId);

                var oldfields = SteamScraper.game.GetAllCustomFields();
                foreach (var field in oldfields)
                {
                    if (field.Name == "Tags")
                    {
                        SteamScraper.game.TryRemoveCustomField(field);
                    }
                }
                List<string> tagList = new List<string>();
                foreach (var Tag in steamSpyTags)
                {
                    JProperty jProperty = Tag.ToObject<JProperty>();
                    string propertyName = jProperty.Name;
                    tagList.Add(propertyName);
                }
                string tagsValue = string.Join(";", tagList);
                var tagsField = SteamScraper.game.AddNewCustomField();
                tagsField.Name = "Tags";
                tagsField.Value = tagsValue;
            }
            //Saves data
            SteamDBLink(appId);
            PluginHelper.DataManager.Save();
        }

        public static void downloadFile(string url, string dest)
        {
            try
            {
                string directory = Path.GetDirectoryName(dest);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    }
                    var response = client.GetAsync(url).Result;
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var fileBytes = response.Content.ReadAsByteArrayAsync().Result;
                        File.WriteAllBytes(dest, fileBytes);
                        Console.WriteLine($"Successfully downloaded: {Path.GetFileName(dest)}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to download (HTTP {response.StatusCode}): {url}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP error downloading {url}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading {url}: {ex.Message}");
            }
        }

        public static async Task<string> CheckURI(string type, string appId)
        {
            string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/" + type;
            using (HttpClient client = new())
            {
                try
                {
                    if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    }
                    HttpResponseMessage response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    return response.Content.Headers.ContentType.MediaType;
                }
                catch (Exception)
                {
                    return "False";
                }
            }
        }

        public static string removeHtmlTags(string description)
        {
            string htmlTags = @"<[^>].+?>";
            string quote = @"&quot;";
            string amp = @"&amp;";
            var temp = Regex.Replace(description, htmlTags, String.Empty);
            temp = Regex.Replace(temp, quote,"\"");
            temp = Regex.Replace(temp, amp, "&");
            return temp;
        }

        public static string CleanFileName(string fileName)
        {
            var cleanTemp = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), "_"));
            return cleanTemp.Replace("'", "_");
        }

        [SupportedOSPlatform("windows7.0")]
        public static void SteamDBLink(string appId)
        {
            foreach (var addApp in SteamScraper.game.GetAllAdditionalApplications())
            {
                if (addApp.Name == "Visit Steam Database page")
                {
                    _ = SteamScraper.game.TryRemoveAdditionalApplication(addApp);
                }
            }
            // Controllo null-safety per evitare crash se SteamScraper.game non è inizializzato
            if (SteamScraper.game != null)
            {
                var additionalApplication = SteamScraper.game.AddNewAdditionalApplication();
                additionalApplication.Name = "Visit Steam Database page";
                additionalApplication.ApplicationPath = "https://steamdb.info/app/" + appId + "/";
            }
        }
    }
}
