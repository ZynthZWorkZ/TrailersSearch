using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide a movie title to search for.");
            Console.WriteLine("Usage: dotnet run -- \"movie title\" [-year \"YYYY\"]");
            return;
        }

        string searchQuery = "";
        string yearFilter = null;

        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-year" && i + 1 < args.Length)
            {
                yearFilter = args[i + 1];
                i++; // Skip the next argument since we've used it
            }
            else
            {
                searchQuery += args[i] + " ";
            }
        }

        searchQuery = searchQuery.Trim();
        Console.WriteLine($"Searching for: {searchQuery}" + (yearFilter != null ? $" (Year: {yearFilter})" : ""));

        try
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            options.AddArgument("--enable-unsafe-swiftshader");

            using var driver = new ChromeDriver(options);
            try
            {
                string movieUrl = await SearchIMDBMovie(driver, searchQuery, yearFilter);
                Console.WriteLine($"Movie URL: {movieUrl}");
                
                string videoUrl = await GetTrailerUrl(driver, movieUrl);
                if (!string.IsNullOrEmpty(videoUrl))
                {
                    Console.WriteLine("Playing trailer...");
                    PlayVideo(videoUrl);
                }
                else
                {
                    throw new Exception("No trailer found for this movie.");
                }
            }
            catch (Exception ex)
            {
                if (yearFilter != null)
                {
                    int currentYear = int.Parse(yearFilter);
                    int nextYear = currentYear - 1;
                    Console.WriteLine($"Failed with year {currentYear}: {ex.Message}");
                    Console.WriteLine($"Retrying with year: {nextYear}");
                    
                    // Update the search query to replace the year
                    string newSearchQuery = searchQuery.Replace(currentYear.ToString(), nextYear.ToString());
                    
                    // Construct new command
                    string newCommand = $"dotnet run -- \"{newSearchQuery}\" -year \"{nextYear}\"";
                    Console.WriteLine($"Running: {newCommand}");
                    
                    // Execute the new command
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run -- \"{newSearchQuery}\" -year \"{nextYear}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    process.WaitForExit();
                }
                else
                {
                    throw; // Re-throw if no year filter was specified
                }
            }
            finally
            {
                driver.Quit();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void PlayVideo(string videoUrl)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffplay",
                Arguments = $"-autoexit \"{videoUrl}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing video: {ex.Message}");
            Console.WriteLine("Make sure ffplay is installed and available in your PATH");
        }
    }

    static async Task<string> GetTrailerUrl(IWebDriver driver, string movieUrl)
    {
        try
        {
            driver.Navigate().GoToUrl(movieUrl);
            
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            await Task.Delay(3000);

            var videoElement = wait.Until(d => d.FindElement(By.CssSelector("video.jw-video.jw-reset")));
            string videoUrl = videoElement.GetAttribute("src");

            if (string.IsNullOrEmpty(videoUrl))
            {
                throw new Exception("Could not find video URL");
            }

            return videoUrl;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error getting trailer: {ex.Message}");
        }
    }

    static async Task<string> SearchIMDBMovie(IWebDriver driver, string searchQuery, string yearFilter)
    {
        try
        {
            string searchUrl = $"https://www.imdb.com/find/?q={Uri.EscapeDataString(searchQuery)}&ref_=nv_sr_sm";
            driver.Navigate().GoToUrl(searchUrl);
            
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            await Task.Delay(2000);

            var resultsList = wait.Until(d => d.FindElement(By.XPath("/html/body/div[2]/main/div[2]/div[3]/section/div/div[1]/section[2]/div[2]/ul")));
            var movieResults = resultsList.FindElements(By.TagName("li"));

            foreach (var result in movieResults)
            {
                try
                {
                    // Get the year from the result
                    var yearElement = result.FindElement(By.CssSelector("span.ipc-metadata-list-summary-item__li"));
                    string year = yearElement.Text;

                    // If year filter is specified, check if it matches
                    if (yearFilter != null && year != yearFilter)
                    {
                        continue;
                    }

                    // Get the movie link
                    var movieLink = result.FindElement(By.CssSelector("a.ipc-metadata-list-summary-item__t"));
                    string movieUrl = movieLink.GetAttribute("href");
                    
                    if (!string.IsNullOrEmpty(movieUrl))
                    {
                        if (!movieUrl.StartsWith("http"))
                        {
                            movieUrl = "https://www.imdb.com" + movieUrl;
                        }
                        return movieUrl;
                    }
                }
                catch
                {
                    continue;
                }
            }

            throw new Exception("No matching movie found" + (yearFilter != null ? $" for year {yearFilter}" : ""));
        }
        catch (Exception ex)
        {
            throw new Exception($"Error searching for movie: {ex.Message}");
        }
    }
}
