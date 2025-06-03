using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Text.RegularExpressions;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide a movie title to search for.");
            Console.WriteLine("Usage: TrailerSearch.exe \"movie title\" [-year \"YYYY\"]");
            Console.WriteLine("       TrailerSearch.exe -getlinks");
            return;
        }

        // Check for -getlinks argument
        if (args[0] == "-getlinks")
        {
            await ProcessMovieLinks();
            return;
        }

        string searchQuery = "";
        string? yearFilter = null;

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

        // Always try to extract year from the search query first
        var yearMatch = System.Text.RegularExpressions.Regex.Match(searchQuery, @"\b(19|20)\d{2}\b$");
        if (yearMatch.Success)
        {
            // If year is in title and matches -year parameter, use it
            if (yearFilter == null || yearMatch.Value == yearFilter)
            {
                yearFilter = yearMatch.Value;
                searchQuery = searchQuery.Substring(0, yearMatch.Index).Trim();
            }
            // If year in title doesn't match -year parameter, remove it from search
            else
            {
                searchQuery = searchQuery.Substring(0, yearMatch.Index).Trim();
            }
        }

        Console.WriteLine($"Searching for: {searchQuery}" + (yearFilter != null ? $" (Year: {yearFilter})" : ""));

        try
        {
            string? movieUrl = null;
            string? bestMatchUrl = null;
            int bestMatchScore = 0;

            // First check if we have the URL in imdb_links.txt
            if (File.Exists("imdb_links.txt"))
            {
                string[] lines = await File.ReadAllLinesAsync("imdb_links.txt");
                foreach (string line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        string year = parts[0].Trim();
                        string title = parts[1].Trim();
                        string url = parts[2].Trim();

                        // Skip entries with NO URL FOUND
                        if (url == "NO URL FOUND") continue;

                        // Clean the title for comparison (remove any year at the end)
                        string cleanTitle = title;
                        var titleYearMatch = System.Text.RegularExpressions.Regex.Match(title, @"\b(19|20)\d{2}\b$");
                        if (titleYearMatch.Success)
                        {
                            cleanTitle = title.Substring(0, titleYearMatch.Index).Trim();
                        }

                        // Calculate match score
                        int matchScore = 0;
                        
                        // Exact title match (highest priority)
                        if (cleanTitle.Equals(searchQuery, StringComparison.OrdinalIgnoreCase))
                        {
                            matchScore += 100;
                            
                            // Year match bonus
                            if (yearFilter == null || year == yearFilter)
                            {
                                matchScore += 50;
                            }
                        }
                        // Contains match (lower priority)
                        else if (cleanTitle.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                                searchQuery.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            matchScore += 10;
                            
                            // Year match bonus
                            if (yearFilter == null || year == yearFilter)
                            {
                                matchScore += 5;
                            }
                        }

                        // Update best match if this is better
                        if (matchScore > bestMatchScore)
                        {
                            bestMatchScore = matchScore;
                            bestMatchUrl = url;
                        }

                        // If we have a perfect match (exact title + year), use it immediately
                        if (matchScore >= 150)
                        {
                            movieUrl = url;
                            Console.WriteLine("Found exact match in cache!");
                            break;
                        }
                    }
                }

                // If no perfect match was found, use the best match
                if (movieUrl == null && bestMatchUrl != null)
                {
                    movieUrl = bestMatchUrl;
                    Console.WriteLine("Found best matching movie in cache!");
                }
            }

            // If not found in cache, search IMDB
            if (movieUrl == null)
            {
                Console.WriteLine("Movie not found in cache, searching IMDB...");
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
                    movieUrl = await SearchIMDBMovie(driver, searchQuery, yearFilter);
                    Console.WriteLine($"Movie URL: {movieUrl}");
                }
                finally
                {
                    driver.Quit();
                }
            }

            if (movieUrl != null)
            {
                // Get the trailer URL
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
                finally
                {
                    driver.Quit();
                }
            }
            else
            {
                throw new Exception("Movie not found");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task ProcessMovieLinks()
    {
        try
        {
            if (!File.Exists("movie_links.txt"))
            {
                Console.WriteLine("Error: movie_links.txt file not found!");
                return;
            }

            // Load existing processed movies
            HashSet<string> processedMovies = new HashSet<string>();
            if (File.Exists("imdb_links.txt"))
            {
                string[] existingLines = await File.ReadAllLinesAsync("imdb_links.txt");
                foreach (string line in existingLines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        string year = parts[0].Trim();
                        string title = parts[1].Trim();
                        processedMovies.Add($"{year}|{title}");
                    }
                }
                Console.WriteLine($"Found {processedMovies.Count} previously processed movies.");
            }

            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            options.AddArgument("--enable-unsafe-swiftshader");

            using var driver = new ChromeDriver(options);
            using var writer = new StreamWriter("imdb_links.txt", true); // true for append mode

            string[] lines = await File.ReadAllLinesAsync("movie_links.txt");
            int total = lines.Length;
            int current = 0;
            int skipped = 0;
            int processed = 0;
            int notFound = 0;

            foreach (string line in lines)
            {
                current++;
                try
                {
                    // Parse the line
                    var parts = line.Split('|');
                    if (parts.Length < 2) continue;

                    string year = parts[0].Trim();
                    string title = parts[1].Trim();
                    string movieKey = $"{year}|{title}";

                    // Check if already processed
                    if (processedMovies.Contains(movieKey))
                    {
                        Console.WriteLine($"Skipping {current}/{total}: {title} ({year}) - Already processed");
                        skipped++;
                        continue;
                    }

                    Console.WriteLine($"Processing {current}/{total}: {title} ({year})");

                    try
                    {
                        // Search for the movie
                        string movieUrl = await SearchIMDBMovie(driver, title, year);
                        await writer.WriteLineAsync($"{year} | {title} | {movieUrl}");
                        processed++;
                    }
                    catch (Exception ex)
                    {
                        // If movie not found, add it with NO URL FOUND
                        await writer.WriteLineAsync($"{year} | {title} | NO URL FOUND");
                        notFound++;
                        Console.WriteLine($"No URL found for: {title} ({year})");
                    }
                    
                    await writer.FlushAsync();
                    processedMovies.Add(movieKey);

                    // Add a small delay to avoid overwhelming the server
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing line: {ex.Message}");
                    continue;
                }
            }

            Console.WriteLine($"\nProcessing complete!");
            Console.WriteLine($"Total movies: {total}");
            Console.WriteLine($"Skipped (already processed): {skipped}");
            Console.WriteLine($"Successfully processed: {processed}");
            Console.WriteLine($"Not found: {notFound}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing movie links: {ex.Message}");
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
            
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
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

    static async Task<string> SearchIMDBMovie(IWebDriver driver, string searchQuery, string? yearFilter)
    {
        try
        {
            string searchUrl = $"https://www.imdb.com/find/?q={Uri.EscapeDataString(searchQuery)}&ref_=nv_sr_sm";
            driver.Navigate().GoToUrl(searchUrl);
            
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            await Task.Delay(2000);

            var resultsList = wait.Until(d => d.FindElement(By.XPath("/html/body/div[2]/main/div[2]/div[3]/section/div/div[1]/section[2]/div[2]/ul")));
            var movieResults = resultsList.FindElements(By.TagName("li"));

            string? bestMatchUrl = null;
            int bestMatchScore = 0;

            foreach (var result in movieResults)
            {
                try
                {
                    // Get the year from the result
                    var yearElement = result.FindElement(By.CssSelector("span.ipc-metadata-list-summary-item__li"));
                    string year = yearElement.Text;

                    // Get the movie link and title
                    var movieLink = result.FindElement(By.CssSelector("a.ipc-metadata-list-summary-item__t"));
                    string title = movieLink.Text;
                    string movieUrl = movieLink.GetAttribute("href");

                    if (string.IsNullOrEmpty(movieUrl))
                    {
                        continue;
                    }

                    if (!movieUrl.StartsWith("http"))
                    {
                        movieUrl = "https://www.imdb.com" + movieUrl;
                    }

                    // Calculate match score
                    int matchScore = 0;

                    // Exact title match (highest priority)
                    if (title.Equals(searchQuery, StringComparison.OrdinalIgnoreCase))
                    {
                        matchScore += 100;
                        
                        // Year match bonus
                        if (yearFilter == null || year == yearFilter)
                        {
                            matchScore += 50;
                        }
                    }
                    // Contains match (lower priority)
                    else if (title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                            searchQuery.Contains(title, StringComparison.OrdinalIgnoreCase))
                    {
                        matchScore += 10;
                        
                        // Year match bonus
                        if (yearFilter == null || year == yearFilter)
                        {
                            matchScore += 5;
                        }
                    }

                    // Update best match if this is better
                    if (matchScore > bestMatchScore)
                    {
                        bestMatchScore = matchScore;
                        bestMatchUrl = movieUrl;
                    }

                    // If we have a perfect match (exact title + year), use it immediately
                    if (matchScore >= 150)
                    {
                        Console.WriteLine($"Found exact match: {title} ({year})");
                        return movieUrl;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // If no perfect match was found, use the best match
            if (bestMatchUrl != null)
            {
                Console.WriteLine($"Found best matching movie with score {bestMatchScore}");
                return bestMatchUrl;
            }

            throw new Exception("No matching movie found" + (yearFilter != null ? $" for year {yearFilter}" : ""));
        }
        catch (Exception ex)
        {
            throw new Exception($"Error searching for movie: {ex.Message}");
        }
    }
}
