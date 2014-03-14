using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace WebRole1
{
    public class WebPageEntity : TableEntity
    {
        // Hard code partition key name and use URL as row key
        public const string PARTITION_KEY = "WebPagePartition";

        public WebPageEntity(string keyword, string url, string title, string date)
        {
            this.Keyword = keyword;
            this.URLs = url;
            this.Titles = title;
            this.Dates = date;

            this.PartitionKey = PARTITION_KEY;
            this.RowKey = keyword;
        }

        public WebPageEntity() : this(string.Empty, string.Empty, string.Empty, string.Empty) { }

        //
        // Private attributes
        //
        private StringBuilder URLBuilder = new StringBuilder();
        private StringBuilder TitleBuilder = new StringBuilder();
        private StringBuilder DateBuilder = new StringBuilder();

        //
        // Properties
        //
        public string Keyword { get; set; }

        public string URLs
        {
            get
            {
                return URLBuilder.ToString();
            }
            set
            {
                URLBuilder = new StringBuilder(value);
            }
        }

        public string Titles
        {
            get
            {
                return TitleBuilder.ToString();
            }
            set
            {
                TitleBuilder = new StringBuilder(value);
            }
        }

        public string Dates
        {
            get
            {
                return DateBuilder.ToString();
            }
            set
            {
                DateBuilder = new StringBuilder(value);
            }
        }

        //
        // Methods
        //
        public void AddUrlTitleDate(string url, string title, string date)
        {
            URLBuilder.AppendFormat("|{0}", url);
            TitleBuilder.AppendFormat("|{0}", title);
            DateBuilder.AppendFormat("|{0}", date);
        }

        public string[] GetAllURLs()
        {
            return this.URLs.Split('|');
        }

        public string[] GetAllTitles()
        {
            return this.Titles.Split('|');
        }

        public string[] GetAllDates()
        {
            return this.Dates.Split('|');
        }

        public int GetURLsNum()
        {
            return GetAllURLs().Length;
        }

        public int GetTitlesNum()
        {
            return GetAllTitles().Length;
        }

        public int GetDatsNum()
        {
            return GetAllDates().Length;
        }

        public string GetURL(int idx)
        {
            return (GetAllURLs())[idx];
        }

        public string GetTitle(int idx)
        {
            return (GetAllTitles())[idx];
        }

        public string GetDate(int idx)
        {
            return (GetAllDates())[idx];
        }

        public float GetEntitySizeKByte()
        {
            // Overhead
            float sizeInByte = 4;

            // Len(PartitionKey + RowKey) * 2
            sizeInByte += (PARTITION_KEY.Length + Keyword.Length) * 2;

            //
            // For-Each Property: 8 bytes + Len(Property Name) * 2 bytes + Sizeof(.Net Property Type)
            //

            // Keyword
            sizeInByte += 8 + (7) * 2 + Keyword.Length * 2 + 4;

            // URL
            sizeInByte += 8 + (3) * 2 + URLs.Length * 2 + 4;

            // Title
            sizeInByte += 8 + (5) * 2 + Titles.Length * 2 + 4;

            // Date
            sizeInByte += 8 + (4) * 2 + Dates.Length * 2 + 4;

            // Return size in Megabyte
            return sizeInByte / 1024;
        }
    }
}
