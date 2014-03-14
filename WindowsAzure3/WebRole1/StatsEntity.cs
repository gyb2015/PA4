using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace WebRole1
{
    public class StatsEntity : TableEntity
    {
        public const string PARTITION_KEY = "Partition";
        public const string ROW_KEY = "Row";

        public StatsEntity(string crawlerState, int numUrlsCrawled, int sizeOfQueue, string last10UrlsCrawled, int sizeOfIndexInKByte)
        {
            this.CrawlerState = crawlerState;
            this.NumUrlsCrawled = numUrlsCrawled;
            this.SizeOfQueue = sizeOfQueue;
            this.Last10UrlsCrawled = last10UrlsCrawled;
            this.SizeOfIndexInKByte = sizeOfIndexInKByte;

            this.PartitionKey = PARTITION_KEY;
            this.RowKey = ROW_KEY;
        }

        public StatsEntity() : this(CrawlerStates.Idle.ToString(), 0, 0, string.Empty, 0) { }

        //
        // Properties
        //
        public string CrawlerState { get; set; }
        public int NumUrlsCrawled { get; set; }
        public int SizeOfQueue { get; set; }
        public string Last10UrlsCrawled { get; set; }
        public int SizeOfIndexInKByte { get; set; }
    }
}