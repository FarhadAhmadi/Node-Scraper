using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PuppeteerSharp;
using System.Net;
using System.Collections.Generic;

class Scraper
{
    // Function to sanitize the filename
    static string SanitizeFilename(string filename)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            filename = filename.Replace(c, '_');
        }
        return filename;
    }

    // Function to download the file
    static async Task DownloadFile(string downloadLink, string lessonFolder, string fileName, Stats stats)
    {
        try
        {
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            // Initial request to set cookies
            var initialResponse = await client.GetAsync(downloadLink);
            if (!initialResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Initial request failed with status: {initialResponse.StatusCode}");
                return;
            }

            var fileUrl = initialResponse.RequestMessage.RequestUri.ToString();
            string sanitizedFileName = SanitizeFilename(fileName) + Path.GetExtension(fileUrl);
            string filePath = Path.Combine(lessonFolder, sanitizedFileName);

            // Download the file
            using var fileResponse = await client.GetAsync(fileUrl);
            using var fileStream = new FileStream(filePath, FileMode.Create);
            await fileResponse.Content.CopyToAsync(fileStream);

            stats.FilesDownloaded++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download {downloadLink}: {ex.Message}");
            stats.FailedDownloads++;
        }
    }

    public class Stats
    {
        public int TotalLessons { get; set; }
        public int FilesDownloaded { get; set; }
        public int FailedDownloads { get; set; }
    }

    public static async Task ScrapeExamQuestions()
    {
        var stats = new Stats();
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync(BrowserFetcher.DefaultRevision);

        var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = "/usr/bin/google-chrome"
        });

        var page = await browser.NewPageAsync();
        await page.GoToAsync("https://www.kanoon.ir/Public/ExamQuestions", WaitUntilNavigation.Networkidle2);

        var content = await page.GetContentAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(content);

        var curriculumLinks = new List<(string href, string text)>();

        // Parse curriculum links
        foreach (var ul in doc.DocumentNode.SelectNodes("//ul[@class='list-group']"))
        {
            foreach (var li in ul.SelectNodes(".//li"))
            {
                var anchor = li.SelectSingleNode(".//a");
                if (anchor != null)
                {
                    var href = anchor.GetAttributeValue("href", string.Empty);
                    var text = anchor.InnerText.Trim();
                    curriculumLinks.Add(($"https://www.kanoon.ir{href}", text));
                }
            }
        }

        foreach (var (curriculumLink, curriculumName) in curriculumLinks)
        {
            var curriculumPage = await browser.NewPageAsync();
            await curriculumPage.GoToAsync(curriculumLink, WaitUntilNavigation.Networkidle2);

            var curriculumContent = await curriculumPage.GetContentAsync();
            var curriculumDoc = new HtmlDocument();
            curriculumDoc.LoadHtml(curriculumContent);

            var lessonLinks = new List<(string href, string name)>();

            // Parse lesson links
            foreach (var anchor in curriculumDoc.DocumentNode.SelectNodes("//a[@class='list-group-item']"))
            {
                var lessonHref = anchor.GetAttributeValue("href", string.Empty);
                var lessonName = anchor.SelectSingleNode(".//span[@class='LessonName']").InnerText.Trim();
                lessonLinks.Add(($"https://www.kanoon.ir{lessonHref}", lessonName));
            }

            string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string curriculumFolder = Path.Combine(homeDirectory, "Node-Scraper", "downloads", curriculumName);
            Directory.CreateDirectory(curriculumFolder);

            foreach (var (lessonLink, lessonName) in lessonLinks)
            {
                var lessonPage = await browser.NewPageAsync();
                await lessonPage.GoToAsync(lessonLink, WaitUntilNavigation.Networkidle2);

                var lessonContent = await lessonPage.GetContentAsync();
                var lessonDoc = new HtmlDocument();
                lessonDoc.LoadHtml(lessonContent);

                var downloadLinks = new List<(string href, string name)>();

                // Parse download links
                foreach (var anchor in lessonDoc.DocumentNode.SelectNodes("//a[@class='downloadfile']"))
                {
                    var downloadHref = anchor.GetAttributeValue("href", string.Empty);
                    var fileName = anchor.SelectSingleNode(".//div").InnerText.Trim();
                    downloadLinks.Add(($"https://www.kanoon.ir{downloadHref}", fileName));
                }

                string lessonFolder = Path.Combine(curriculumFolder, lessonName);
                Directory.CreateDirectory(lessonFolder);

                foreach (var (downloadLink, fileName) in downloadLinks)
                {
                    await DownloadFile(downloadLink, lessonFolder, fileName, stats);
                }

                stats.TotalLessons++;
                Console.WriteLine($"Completed downloading files for lesson: {lessonName}");
                await lessonPage.CloseAsync();
            }

            await curriculumPage.CloseAsync();
        }

        await browser.CloseAsync();

        Console.WriteLine("Scraping completed!");
        Console.WriteLine($"Total lessons processed: {stats.TotalLessons}");
        Console.WriteLine($"Total files downloaded: {stats.FilesDownloaded}");
        Console.WriteLine($"Total failed downloads: {stats.FailedDownloads}");
    }

    public static async Task Main(string[] args)
    {
        await ScrapeExamQuestions();
    }
}
