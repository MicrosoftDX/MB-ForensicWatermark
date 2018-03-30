using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.Entities
{

    public class AssetProcessLock : TableEntity
    {
        public AssetProcessLock(string AssetId, string JobId)
        {
            PartitionKey = AssetId;
            RowKey = "lock";
            this.JobId = JobId;
        }
        public string JobId { get; set; }
    }
}
