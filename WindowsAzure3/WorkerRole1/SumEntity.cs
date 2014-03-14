using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace WorkerRole1
{
    class SumEntity : TableEntity
    {
        public SumEntity(string partitionKey, string rowKey)
        {
            this.PartitionKey = partitionKey;
            this.RowKey = rowKey;
        }

        public SumEntity() { }

        public int Sum { get; set; }
    }
}
