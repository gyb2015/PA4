using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Xml;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using WebRole1;

namespace WorkerRole1
{
    public class WorkerRole : RoleEntryPoint
    {
        private const string crawlURL = @"http://www.cnn.com/";
        private const string crawlDomain = @"cnn.com";
        private CloudQueue CommandQueue = null;
        private CloudQueue SitemapQueue = null;
        private CloudQueue CrawlingQueue = null;
        private CloudQueue TitleQueue = null;
        private CloudTable CrawlingTable = null;
        private StatsEntity statsEntity = new StatsEntity();
        private HashSet<string> crawledUrls = new HashSet<string>();
        private List<string> disallowedDirs = new List<string>();
        private Queue<string> last10UrlsCrawled = new Queue<string>();
        private float sizeOfIndexInKByte = 0;

        public override void Run()
        {
            // Initialize the queues and table
            InitalizeCommandQueue();
            InitializeSitemapQueue();
            InitializeCrawlingQueue();
            InitializeTitleQueue();
            InitializeCrawlingTable();
            
            while (true)
            {
                Thread.Sleep(1);

                // Process CommandQueue
                ProcessCommandQueue();

                // Do crawling
                if (statsEntity.CrawlerState == CrawlerStates.Loading.ToString())
                {
                    OnCrawlerStateLoading();
                }
                else if (statsEntity.CrawlerState == CrawlerStates.Crawling.ToString())
                {
                    OnCrawlerStateCrawling();
                }

                // Do nothing if idle
            }
        }
        
        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            return base.OnStart();
        }

        private void InitalizeCommandQueue()
        {
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the queue client
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

            // Retrieve a reference to CommandQueue
            CommandQueue = queueClient.GetQueueReference(WebRole.AZURE_COMMAND_QUEUE);

            // Create the queue if it doesn't already exist
            CommandQueue.CreateIfNotExists();

            // Clear the queue
            CommandQueue.Clear();
        }

        private void InitializeSitemapQueue()
        {
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the queue client
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

            // Retrieve a reference to SitemapQueue
            SitemapQueue = queueClient.GetQueueReference(WebRole.AZURE_SITEMAP_QUEUE);

            // Create the queue if it doesn't already exist
            SitemapQueue.CreateIfNotExists();

            // Clear the queue
            SitemapQueue.Clear();
        }

        private void InitializeCrawlingQueue()
        {
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the queue client
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

            // Retrieve a reference to CrawlingQueue
            CrawlingQueue = queueClient.GetQueueReference(WebRole.AZURE_CRAWLING_QUEUE);

            // Create the queue if it doesn't already exist
            CrawlingQueue.CreateIfNotExists();

            // Clear the queue
            CrawlingQueue.Clear();

            // Pre-set crawling queue
            PresetCrawlingQueue();
        }

        private void PresetCrawlingQueue()
        {
            // Crawl NBA site first
            CrawlingQueue.AddMessage(new CloudQueueMessage(@"http://sportsillustrated.cnn.com/nba"));
        }

        private void InitializeTitleQueue()
        {
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the queue client
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();

            // Retrieve a reference to TitleQueue
            TitleQueue = queueClient.GetQueueReference(WebRole.AZURE_TITLE_QUEUE);

            // Create the queue if it doesn't already exist
            TitleQueue.CreateIfNotExists();

            // Clear the queue
            TitleQueue.Clear();
        }

        private void InitializeCrawlingTable()
        {
            // Retrieve storage account from connection string
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                CloudConfigurationManager.GetSetting("StorageConnectionString"));

            // Create the table client
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Retrieve a reference to CrawlingTable
            CrawlingTable = tableClient.GetTableReference(WebRole.AZURE_CRAWLING_TABLE);

            // Create the table if it doesn't exist
            CrawlingTable.CreateIfNotExists();

            // Clear the table
            //CrawlingTable.Delete();
            //CrawlingTable.Create();
        }

        private void ProcessCommandQueue()
        {
            if (CommandQueue == null)
            {
                InitalizeCommandQueue();
            }

            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }

            CloudQueueMessage message = CommandQueue.GetMessage();
            
            if (message == null)
            {
                // Exit this function if there is no message
                return;
            }

            if (message.AsString == Commands.CMD_START_CRAWLER.ToString())
            {
                if (statsEntity.CrawlerState == CrawlerStates.Idle.ToString())
                {
                    // Start crawling if the current state is 'idle'
                    statsEntity.CrawlerState = CrawlerStates.Loading.ToString();
                }
            }
            else if (message.AsString == Commands.CMD_STOP_CRAWLER.ToString())
            {
                statsEntity.CrawlerState = CrawlerStates.Idle.ToString();
                TableOperation insertOrReplaceOperation = TableOperation.InsertOrReplace(statsEntity);
                CrawlingTable.Execute(insertOrReplaceOperation);
            }
            else if (message.AsString == Commands.CMD_CLEAR_CRAWLER.ToString())
            {
                ClearCrawler();
            }

            // After reading the message, the client should delete it
            CommandQueue.DeleteMessage(message);
        }

        private void OnCrawlerStateLoading()
        {
            if (SitemapQueue == null)
            {
                InitializeSitemapQueue();
            }

            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }

            // Put stats to table
            TableOperation insertOrReplaceOperation = TableOperation.InsertOrReplace(statsEntity);
            CrawlingTable.Execute(insertOrReplaceOperation);

            if (crawledUrls.Count == 0)
            {
                // Load the site maps and disallow list from robots.txt
                try
                {
                    WebClient webClient = new WebClient();
                    string data = webClient.DownloadString(crawlURL + "robots.txt");
                    string[] lines = data.Split('\n');
                    foreach (string line in lines)
                    {
                        if (line.Contains("Sitemap"))
                        {
                            CloudQueueMessage message = new CloudQueueMessage(line.Substring(line.IndexOf("http"), line.Length - 9));
                            SitemapQueue.AddMessage(message);
                        }
                        else if (line.Contains("Disallow"))
                        {
                            disallowedDirs.Add(line.Remove(0, 10));
                        }
                    }

                    // Additional directories that we don't want to crawl:
                    disallowedDirs.Add("/rss");
                    disallowedDirs.Add("/~ff");
                    disallowedDirs.Add("arabic.cnn.com");
                    disallowedDirs.Add("mexico.cnn.com");
                }
                catch (Exception)
                {
                    // Do nothing
                }
            }

            // Change state to 'crawling'
            statsEntity.CrawlerState = CrawlerStates.Crawling.ToString();
        }

        private void OnCrawlerStateCrawling()
        {
            WebPageEntity webpageEntity = null;
            List<string> qualifiedUrls = new List<string>();
            XmlDocument doc = new XmlDocument();
            string textdata = string.Empty;
            string URL = string.Empty;
            string title = string.Empty;
            string date = string.Empty;
            bool IsCrawlingMessage = true;
            bool IsSitemapMessage = true;

            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }
            
            if (CrawlingQueue == null)
            {
                InitializeCrawlingQueue();
            }

            if (TitleQueue == null)
            {
                InitializeTitleQueue();
            }

            if (SitemapQueue == null)
            {
                InitializeSitemapQueue();
            }

            CloudQueueMessage message = CrawlingQueue.GetMessage();

            if (message == null)
            {
                // No more webpage to crawl
                IsCrawlingMessage = false;
                message = SitemapQueue.GetMessage();

                if (message == null)
                {
                    // No more sitemap to crawl
                    IsSitemapMessage = false;
                    statsEntity.CrawlerState = CrawlerStates.Idle.ToString();
                    return;
                }
            }
            else
            {
                IsSitemapMessage = false;
            }

            // Store URL temporarily
            URL = message.AsString;

            // Check whether the site has been crawled
            if (crawledUrls.Contains(URL))
            {
                statsEntity.SizeOfQueue = statsEntity.SizeOfQueue - 1;

                if (IsCrawlingMessage)
                {
                    CrawlingQueue.DeleteMessage(message);
                }
                if (IsSitemapMessage)
                {
                    SitemapQueue.DeleteMessage(message);
                }

                return;
            }

            // Add this URL to the crawled hashset
            crawledUrls.Add(URL);

            try
            {
                if (IsSitemapMessage)
                {
                    // Load URL as XML document
                    doc.Load(URL);
                }
                else
                {
                    // Load URL as html document
                    WebClient webClient = new WebClient();
                    textdata = webClient.DownloadString(URL);

                    // Get title from html document
                    title = GetPageTitle(textdata);

                    // Get date from html document
                    date = GetPageDate(textdata);
                }
            }
            catch (Exception)
            {
                // Remove problematic webpage and skip
                if (IsCrawlingMessage)
                {
                    CrawlingQueue.DeleteMessage(message);
                }
                if (IsSitemapMessage)
                {
                    SitemapQueue.DeleteMessage(message);
                }
                return;
            }

            // Only index page within 3 months old
            DateTime dt;
            if (string.IsNullOrEmpty(date))
            {
                dt = DateTime.Today;
            }
            else
            {
                dt = DateTime.Parse(date, new System.Globalization.CultureInfo("en-US", true), System.Globalization.DateTimeStyles.AssumeLocal);
            }

            if (((DateTime.Today.Month - dt.Month) + 12 * (DateTime.Today.Year - dt.Year)) <= 3)
            {
                string[] keywords = title.Split(new Char[] { ' ', '/', '\'', '.' });
                foreach (string keyword in keywords)
                {
                    // Remove symbols from the front and end of a keyword
                    string k = TrimKeyword(keyword);

                    if (string.IsNullOrEmpty(k))
                    {
                        // Skip the invalid keyword
                        continue;
                    }

                    // Retrieve webpage entity from the crawling table according to keyword
                    TableResult retrievedResult = CrawlingTable.Execute(TableOperation.Retrieve<WebPageEntity>(WebPageEntity.PARTITION_KEY, k));
                    if (retrievedResult.Result != null)
                    {
                        // Add info if exists
                        webpageEntity = (WebPageEntity)retrievedResult.Result;
                        webpageEntity.AddUrlTitleDate(URL, title, date);
                    }
                    else
                    {
                        // Create new entity if not exist
                        webpageEntity = new WebPageEntity(k, URL, title, date);
                    }

                    // Put webpage entity to the crawling table
                    CrawlingTable.Execute(TableOperation.InsertOrReplace(webpageEntity));
                }

                // Add the qualified links in this URL to the SitemapQueue or CrawlingQueue
                // Only do this for pages less than 3 months, assuming old pages don't link to new pages
                if (IsSitemapMessage)
                {
                    qualifiedUrls = GetPageLinks(doc);
                }
                else
                {
                    qualifiedUrls = GetPageLinks(textdata);
                }

                foreach (string url in qualifiedUrls)
                {
                    if (url.Contains(".xml"))
                    {
                        SitemapQueue.AddMessage(new CloudQueueMessage(url));
                    }
                    else
                    {
                        CrawlingQueue.AddMessage(new CloudQueueMessage(url));
                    }
                }

                // Put title in TitleQueue to build trie
                if (!string.IsNullOrEmpty(title))
                {
                    TitleQueue.AddMessage(new CloudQueueMessage(title));
                }
            }

            // Update stats
            statsEntity.NumUrlsCrawled = statsEntity.NumUrlsCrawled + 1;
            
            last10UrlsCrawled.Enqueue(URL);
            while (last10UrlsCrawled.Count > 10)
            {
                last10UrlsCrawled.Dequeue();
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < last10UrlsCrawled.Count; i++)
            {
                string temp = last10UrlsCrawled.Dequeue();
                sb.Append(temp);
                if (i != last10UrlsCrawled.Count)
                {
                    sb.Append("|");
                }
                last10UrlsCrawled.Enqueue(temp);
            }
            statsEntity.Last10UrlsCrawled = sb.ToString();
            
            statsEntity.SizeOfQueue = statsEntity.SizeOfQueue + qualifiedUrls.Count - 1;
            if (webpageEntity != null)
            {
                sizeOfIndexInKByte += webpageEntity.GetEntitySizeKByte();
            }
            statsEntity.SizeOfIndexInKByte = (int)sizeOfIndexInKByte;

            // Put stats to table
            TableOperation insertOrReplaceOperation = TableOperation.InsertOrReplace(statsEntity);
            CrawlingTable.Execute(insertOrReplaceOperation);

            // After processing the URL, the client should delete it
            if (IsCrawlingMessage)
            {
                CrawlingQueue.DeleteMessage(message);
            }
            if (IsSitemapMessage)
            {
                SitemapQueue.DeleteMessage(message);
            }
        }

        private string GetPageTitle(XmlDocument doc)
        {
            XmlReader reader = new XmlNodeReader(doc);

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Name.ToLower() == "title")
                    {
                        string t = System.Web.HttpUtility.HtmlDecode(reader.Value.ToLower());
                        int i = t.LastIndexOf(".com");

                        if (i < 0)
                        {
                            return t;
                        }

                        int j = t.LastIndexOf(" - ");

                        if ((j > 0) && (j < i))
                        {
                            return t.Substring(0, j);
                        }
                        else
                        {
                            return t;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private string GetPageTitle(string textdata)
        {
            Match m = Regex.Match(textdata, @"<title>\s*(.+?)\s*</title>", RegexOptions.IgnoreCase);

            if (m.Success)
            {
                string t = System.Web.HttpUtility.HtmlDecode(m.Groups[1].Value.ToLower());
                int i = t.LastIndexOf(".com");

                if (i < 0)
                {
                    return t;
                }

                int j = t.LastIndexOf(" - ");

                if ((j > 0) && (j < i))
                {
                    return t.Substring(0, j);
                }
                else
                {
                    return t;
                }
            }
            else
            {
                return string.Empty;
            }
        }

        private string GetPageDate(string textdata)
        {
            Match m = Regex.Match(textdata, "\\s*publish_date:\\s*\"(.+?)\",\\s*", RegexOptions.IgnoreCase);

            if (m.Success)
            {
                return m.Groups[1].Value;
            }
            else
            {
                return string.Empty;
            }
        }

        private List<string> GetPageLinks(XmlDocument doc)
        {
            List<string> links = new List<string>();
            XmlReader reader = new XmlNodeReader(doc);

            // Look for <loc> and <a> tags
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Name.ToLower() == "loc")
                    {
                        if (reader.Read())
                        {
                            string URL = TrimEndHash(reader.Value.ToLower()).Trim().TrimEnd('/');
                            if (IsLinkAllowed(URL))
                            {
                                links.Add(URL);
                            }
                        }
                    }
                }
            }

            return links;
        }

        private List<string> GetPageLinks(string textdata)
        {
            Match m;
            List<string> links = new List<string>();
            string HRefPattern = "href\\s*=\\s*(?:[\"'](?<1>[^\"']*)[\"']|(?<1>\\S+))";
            string URL = string.Empty;

            try
            {
                m = Regex.Match(
                    textdata, HRefPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1)
                    );
                while (m.Success)
                {
                    URL = m.Groups[1].Value;

                    if (string.IsNullOrEmpty(URL))
                    {
                        m = m.NextMatch();
                        continue;
                    }

                    if (URL[0] == '/')
                    {
                        // Append relative path to domain
                        URL = crawlURL + URL.Substring(1, URL.Length - 1);
                    }

                    URL = TrimEndHash(URL).TrimEnd('/');

                    if (IsLinkAllowed(URL))
                    {
                        links.Add(URL);
                    }

                    m = m.NextMatch();
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Do nothing
            }

            return links;
        }

        private bool IsLinkAllowed(string URL)
        {
            Uri uri;
            string host;

            try
            {
                uri = new Uri(URL);
                host = uri.Host;
            }
            catch (Exception)
            {
                return false;
            }

            // Check domain
            if (!host.Contains(crawlDomain))
            {
                return false;
            }

            // Check disallow list
            foreach (string s in disallowedDirs)
            {
                if (URL.Contains(s))
                {
                    return false;
                }
            }

            // Check whether the page is already visited
            if (crawledUrls.Contains(URL))
            {
                return false;
            }

            return true;
        }

        private string TrimEndHash(string s)
        {
            int i = s.IndexOf('#');

            if (i > -1)
            {
                return s.Substring(0, i);
            }

            return s;
        }

        private string TrimKeyword(string keyword)
        {
            char[] a = keyword.ToLower().ToArray();
            int i = 0;
            int j = 0;

            // Search for first alphabetical character from front
            for (i = 0; i < keyword.Length; i++)
            {
                if ((a[i] >= 'a' && a[i] <= 'z')
                    || (a[i] >= '0' && a[i] <= '9'))
                {
                    break;
                }
            }

            // Search for first alphabetical character from end
            for (j = keyword.Length - 1; j >= 0; j--)
            {
                if ((a[j] >= 'a' && a[j] <= 'z')
                    || (a[j] >= '0' && a[j] <= '9'))
                {
                    break;
                }
            }

            if (i > j)
            {
                return string.Empty;
            }

            return keyword.Substring(i, j + 1 - i);
        }

        private void ClearCrawler()
        {
            if (SitemapQueue == null)
            {
                InitializeSitemapQueue();
            }

            if (CrawlingQueue == null)
            {
                InitializeCrawlingQueue();
            }

            if (TitleQueue == null)
            {
                InitializeTitleQueue();
            }

            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }

            crawledUrls.Clear();
            SitemapQueue.Clear();
            CrawlingQueue.Clear();
            TitleQueue.Clear();
            disallowedDirs.Clear();
            CrawlingTable.Delete();
            CrawlingTable.CreateIfNotExists();
            statsEntity = new StatsEntity();
            last10UrlsCrawled.Clear();
            sizeOfIndexInKByte = 0;

            PresetCrawlingQueue();
        }
    }
}
