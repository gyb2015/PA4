using System;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using System.Web.Services;
using System.Web.Script.Services;
using System.Web.Script.Serialization;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace WebRole1
{
    /// <summary>
    /// Summary description for WebService1
    /// </summary>
    [WebService(Namespace = "WebRole1")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // Allow this Web Service to be called from script, using ASP.NET AJAX
    [System.Web.Script.Services.ScriptService]
    public class WebService1 : System.Web.Services.WebService
    {
        const int CACHE_MAX_COUNT = 100;
        const int RESULT_MAX_COUNT = 10;

        static PerformanceCounter CpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
        static PerformanceCounter MemCounter = new PerformanceCounter("Memory", "Available MBytes", true);
        static CloudQueue CommandQueue = null;
        static CloudQueue TitleQueue = null;
        static CloudTable CrawlingTable = null;
        static Dictionary<string, WebPageEntity> Cache = new Dictionary<string, WebPageEntity>();
        static string LastCrawlerState = CrawlerStates.Idle.ToString();
        static Thread BuildTrieThread = null;
        static Thread UpdateCPULoadThread = null;
        //static Trie trie = null;
        static bool ShouldBuildTrie = false;
        static float CPULoad = 0;
        static uint NumSuggestionsAvailable = 0;

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public List<string> GetSearchSuggestions(string input)
        {
            List<string> result;
            if (Global.trie != null)
            {
                result = Global.trie.SearchPhrasesForPrefix(Uri.UnescapeDataString(input));
            }
            else
            {
                result = new List<string>();
            }

            return result;
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public void StartCrawler()
        {
            if (LastCrawlerState != CrawlerStates.Idle.ToString())
            {
                return;
            }

            if (CommandQueue == null)
            {
                InitalizeCommandQueue();
            }

            // Create queue message
            CloudQueueMessage message = new CloudQueueMessage(Commands.CMD_START_CRAWLER.ToString());

            // Add message to command queue
            CommandQueue.AddMessage(message);

            // Start a new thread to build trie
            ShouldBuildTrie = true;
            BuildTrieThread = new Thread(this.BuildTrie);
            BuildTrieThread.Start();
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public void StopCrawler()
        {
            if (LastCrawlerState == CrawlerStates.Idle.ToString())
            {
                return;
            }

            if (CommandQueue == null)
            {
                InitalizeCommandQueue();
            }

            // Create queue message
            CloudQueueMessage message = new CloudQueueMessage(Commands.CMD_STOP_CRAWLER.ToString());

            // Add message to command queue
            CommandQueue.AddMessage(message);
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public void ClearCrawler()
        {
            if (LastCrawlerState == CrawlerStates.Idle.ToString())
            {
                return;
            }

            if (CommandQueue == null)
            {
                InitalizeCommandQueue();
            }

            // Create queue message
            CloudQueueMessage message = new CloudQueueMessage(Commands.CMD_CLEAR_CRAWLER.ToString());

            // Add message to command queue
            CommandQueue.AddMessage(message);

            // Stop building trie
            ShouldBuildTrie = false;

            // Clear cache
            Cache.Clear();

            // Reset suggestion num
            NumSuggestionsAvailable = 0;
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public List<string> Search(string searchItem)
        {
            List<string> retval = new List<string>();
            string rowKey = string.Empty;
            Dictionary<string, uint> results = new Dictionary<string, uint>();
            List<KeyValuePair<string, uint>> sortedResults;

            foreach(string keyword in Uri.UnescapeDataString(searchItem).Split(new Char[] { ' ', '/', '\'', '.' }))
            {
                string k = TrimKeyword(keyword.ToLower());
                WebPageEntity w = RetrieveWebPageEntity(k);

                // Build a dictionary of URLs with values of occurrence
                if (w != null)
                {
                    string[] urls = w.GetAllURLs();
                    string[] titles = w.GetAllTitles();
                    string[] dates = w.GetAllDates();
                    for (uint i = 0; i < urls.Length; i++)
                    {
                        StringBuilder keyBuilder = new StringBuilder(urls[i]);
                        keyBuilder.AppendFormat("|{0}|{1}", titles[i], dates[i]);
                        string key = keyBuilder.ToString();
                        if (results.ContainsKey(key))
                        {
                            results[key] = results[key] + 1;
                        }
                        else
                        {
                            results.Add(key, 1);
                        }
                    }
                }
            }
            
            // Sort the dictionary according to values
            sortedResults = results.ToList();
            sortedResults.Sort((firstPair, nextPair) =>
                {
                    return nextPair.Value.CompareTo(firstPair.Value);
                });

            // Print the sorted results
            foreach (KeyValuePair<string, uint> pair in sortedResults)
            {
                string[] url_title_date = pair.Key.Split('|');
                StringBuilder result = new StringBuilder();

                // Show title and URL
                result.AppendFormat("<a href=\"{0}\"><font size=\"+1\"><u>{1}</u></font></a><br />", url_title_date[0], url_title_date[1]);
                result.AppendFormat("{0}<br />", url_title_date[0]);

                // Append date if available
                if (string.IsNullOrEmpty(url_title_date[2]) == false)
                {
                    result.AppendFormat("{0}<br />", url_title_date[2]);
                }

                retval.Add(result.ToString());

                if (retval.Count >= RESULT_MAX_COUNT)
                {
                    break;
                }
            }

            if (retval.Count == 0)
            {
                retval.Add("No result");
            }

            return retval;
        }

        [WebMethod]
        [ScriptMethod(ResponseFormat = ResponseFormat.Json)]
        public string GetStats()
        {
            StringBuilder retval = new StringBuilder();

            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }

            // Start CPULoadMonitor if it hasn't started
            if (UpdateCPULoadThread == null)
            {
                UpdateCPULoadThread = new Thread(this.UpdateCPULoad);
                UpdateCPULoadThread.Start();
            }

            // Create a retrieve operation that takes a stats entity and execute the retrieve operation
            TableResult retrievedResult = CrawlingTable.Execute(TableOperation.Retrieve<StatsEntity>(StatsEntity.PARTITION_KEY, StatsEntity.ROW_KEY));

            if (retrievedResult.Result == null)
            {
                // Crawler state
                retval.Append("Crawler State: Idle <br />");
                LastCrawlerState = CrawlerStates.Idle.ToString();

                // Machine counters
                retval.AppendFormat(@"CPU Utilization: {0}%<br />", Convert.ToUInt32(CPULoad));
                retval.AppendFormat(@"RAM available: {0} MByte<br />", GetMemAvailable());

                // #URLs crawled
                retval.Append(@"#URLs crawled: 0<br />");

                // Size of queue
                retval.Append("Size of queue: 0<br />");

                // Size of index
                retval.Append("Size of index: 0 KByte<br />");

                // #suggestions available
                retval.Append(@"#suggestions available: 0<br />");

                // Last 10 URLs crawled
                retval.Append("Last 10 URLs crawled: <br />");

                return retval.ToString();
            }

            // Convert result to StatsEntity
            StatsEntity statsEntity = (StatsEntity)(retrievedResult.Result);

            // Crawler state
            retval.AppendFormat("Crawler State: {0}<br />", statsEntity.CrawlerState);
            LastCrawlerState = statsEntity.CrawlerState;

            // Machine counters
            retval.AppendFormat(@"CPU Utilization: {0}%<br />", Convert.ToUInt32(CPULoad));
            retval.AppendFormat(@"RAM available: {0} MByte<br />", GetMemAvailable());

            // #URLs crawled
            retval.AppendFormat(@"#URLs crawled: {0}<br />", statsEntity.NumUrlsCrawled);

            // Size of queue
            retval.AppendFormat("Size of queue: {0}<br />", statsEntity.SizeOfQueue);

            // Size of index
            retval.AppendFormat("Size of index: {0} KByte<br />", statsEntity.SizeOfIndexInKByte);

            // #suggestions available
            retval.AppendFormat(@"#suggestions available: {0}<br />", NumSuggestionsAvailable);

            // Last 10 URLs crawled
            retval.Append("Last 10 URLs crawled: <br />");
            string[] links = statsEntity.Last10UrlsCrawled.Split('|');
            foreach(string link in links)
            {
                retval.AppendFormat("{0}<br />", link);
            }

            return retval.ToString();
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
        }

        private WebPageEntity RetrieveWebPageEntity(string key)
        {
            Random rnd = new Random();

            // Directly return webpage entity from cache if exists
            if (Cache.ContainsKey(key))
            {
                WebPageEntity cachedEntity = Cache[key];
                
                // 50% of chance deleting the most recently used cache
                if (rnd.Next(0, 2) == 0)
                {
                    Cache.Remove(key);
                }

                return cachedEntity;
            }

            // Retrieve webpage entity from cloud table
            if (CrawlingTable == null)
            {
                InitializeCrawlingTable();
            }

            // Create a retrieve operation that takes a customer entity.
            TableOperation retrieveOperation = TableOperation.Retrieve<WebPageEntity>(WebPageEntity.PARTITION_KEY, key);

            // Execute the retrieve operation.
            TableResult retrievedResult = CrawlingTable.Execute(retrieveOperation);

            // Return null if no result
            if (retrievedResult.Result == null)
            {
                return null;
            }

            WebPageEntity w = (WebPageEntity)retrievedResult.Result;

            // Randomly remove a key if too many entries
            if (Cache.Count > CACHE_MAX_COUNT)
            {
                int i = rnd.Next(0, CACHE_MAX_COUNT);
                Cache.Remove(Cache.ToList()[i].Key);
            }

            // Add cache entry
            Cache.Add(key, w);

            return w;
        }

        private void BuildTrie()
        {
            if (TitleQueue == null)
            {
                InitializeTitleQueue();
            }

            // Build a new trie
            if (Global.trie == null)
            {
                Global.trie = new Trie();
            }

            while (ShouldBuildTrie)
            {
                Thread.Sleep(100);
                CloudQueueMessage message = TitleQueue.GetMessage();

                // Skip this loop if no message
                if (message == null)
                {
                    continue;
                }

                // Insert title to trie
                Global.trie.InsertPhrase(message.AsString);

                // Update #suggestions available
                NumSuggestionsAvailable = Global.trie.NumSuggestionsAvailable;

                // Delete last message from queue
                TitleQueue.DeleteMessage(message);
            }

            Global.trie = null;
        }

        private void UpdateCPULoad()
        {
            while (true)
            {
                Thread.Sleep(1000);
                CPULoad = CpuCounter.NextValue();
            }
        }

        private uint GetMemAvailable()
        {
            uint mem = Convert.ToUInt32(MemCounter.NextValue());

            if (mem <= 1024)
            {
                // Should stop crawling when memory is low
                //StopCrawler();
            }

            return mem;
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
    }
}
