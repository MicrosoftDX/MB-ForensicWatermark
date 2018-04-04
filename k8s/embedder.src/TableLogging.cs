namespace embedder
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.Table;
    using Polly;

    public class TableWrapper
    {
        CloudTableClient client;
        CloudTable preprocessorTable;
        CloudTable embedderTable;

        public static async Task<TableWrapper> CreateAsync(string connectionString, string preprocessorTableName, string embedderTableName)
        {
            var result = new TableWrapper();
            await result.InitAsync(connectionString, preprocessorTableName, embedderTableName);
            return result;
        }

        private TableWrapper() { }

        private async Task InitAsync(string connectionString, string preprocessorTableName, string embedderTableName)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                Console.WriteLine("WARNING: No connection string to table storage provided");
                return;
            }
            var account = CloudStorageAccount.Parse(connectionString: connectionString);
            this.client = account.CreateCloudTableClient();
            this.preprocessorTable = client.GetTableReference(tableName: preprocessorTableName);
            await this.preprocessorTable.CreateIfNotExistsAsync();
            this.embedderTable = client.GetTableReference(tableName: embedderTableName);
            await this.embedderTable.CreateIfNotExistsAsync();
        }

        public Task LogPreprocessorAsync(PreprocessorJob job, string status)
        {
            if (this.preprocessorTable == null)
            {
                return Task.CompletedTask;
            }

            return Policy.Handle<Exception>()
                .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: attempt => TimeSpan.FromSeconds(1))
                .Execute(() =>
                {
                    return preprocessorTable.InsertOrReplaceAsync<PreprocessorTableEntity>(new PreprocessorTableEntity(
                        job: job, status: status));
                });
        }

        public Task LogEmbedderAsync(EmbedderJob job, string status)
        {
            if (this.embedderTable == null)
            {
                return Task.CompletedTask;
            }

            return Policy.Handle<Exception>()
                .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: attempt => TimeSpan.FromSeconds(1))
                .Execute(() =>
                {
                    return embedderTable.InsertOrReplaceAsync<EmbedderTableEntity>(new EmbedderTableEntity(
                        job: job, status: status));
                });
        }
    }

    public class PreprocessorTableEntity : StorageTableEntityBase
    {
        public PreprocessorTableEntity() { }

        public PreprocessorTableEntity(PreprocessorJob job, string status)
        {
            this.PartitionKey = job.Job.AssetID;
            this.RowKey = $"{job.MmmrkURL.NormalizedURL().ToPartitionKey()}-{job.VideoBitrate}";
            this.VideoURL = job.Mp4URL != null ? job.Mp4URL.NormalizedURL() : "";
            this.MMRKURL = job.MmmrkURL.NormalizedURL();
            this.AssetID = job.Job.AssetID;
            this.JobID = job.Job.JobID;
            this.Status = status;
        }

        public string VideoURL { get; set; }
        public string MMRKURL { get; set; }
        public string AssetID { get; set; }
        public string JobID { get; set; }
        public string Status { get; set; }
    }

    public class EmbedderTableEntity : StorageTableEntityBase
    {
        public EmbedderTableEntity() { }

        public EmbedderTableEntity(EmbedderJob job, string status)
        {
            this.PartitionKey = job.Job.AssetID;
            this.RowKey = $"{job.WatermarkedURL.NormalizedURL().ToPartitionKey()}_{job.UserID}";

            this.WaterMarkedMp4 = job.WatermarkedURL.NormalizedURL();
            this.UserID = job.UserID;
            this.AssetID = job.Job.AssetID;
            this.JobID = job.Job.JobID;
            this.Status = status;
        }

        public string WaterMarkedMp4 { get; set; }
        public string UserID { get; set; }
        public string AssetID { get; set; }
        public string JobID { get; set; }
        public string Status { get; set; }
    }

    public static class TableExtensions
    {
        public static Task InsertOrReplaceAsync<T>(this CloudTable table, T entity)
            where T : StorageTableEntityBase, new()
        {
            return table.ExecuteAsync(TableOperation.InsertOrReplace(
                new AzStorageEntityAdapter<T>(entity)));
        }

        public static async Task<T> RetrieveEntityAsync<T>(this CloudTable table, string partitionKey, string rowKey) where T : StorageTableEntityBase, new()
        {
            var tableResult = await table.ExecuteAsync(
                TableOperation.Retrieve<AzStorageEntityAdapter<T>>(partitionKey, rowKey));
            if (tableResult.Result == null) { return default(T); }
            return ((AzStorageEntityAdapter<T>)tableResult.Result).InnerObject;
        }

        public static string NormalizedURL(this Uri uri)
        {
            return $"https://{uri.Host}{uri.LocalPath}";
        }

        public static string ToPartitionKey(this string uri)
        {
            // https://blogs.msdn.microsoft.com/jmstall/2014/06/12/azure-storage-naming-rules/
            return uri
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace("#", "_")
                .Replace("?", "_")
                ;
        }
    }

    public class StorageTableEntityBase
    {
        public string ETag { get; set; }
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public StorageTableEntityBase() { }
        public StorageTableEntityBase(string partitionKey, string rowKey) { PartitionKey = partitionKey; RowKey = rowKey; }
    }

    internal class AzStorageEntityAdapter<T> : ITableEntity where T : StorageTableEntityBase, new()
    {
        public string PartitionKey { get { return InnerObject.PartitionKey; } set { InnerObject.PartitionKey = value; } }
        public string RowKey { get { return InnerObject.RowKey; } set { InnerObject.RowKey = value; } }
        public DateTimeOffset Timestamp { get { return InnerObject.Timestamp; } set { InnerObject.Timestamp = value; } }
        public string ETag { get { return InnerObject.ETag; } set { InnerObject.ETag = value; } }
        public T InnerObject { get; set; }
        public AzStorageEntityAdapter() { this.InnerObject = new T(); }
        public AzStorageEntityAdapter(T innerObject) { this.InnerObject = innerObject; }
        public virtual void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext) { TableEntity.ReadUserObject(this.InnerObject, properties, operationContext); }
        public virtual IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext) { return TableEntity.WriteUserObject(this.InnerObject, operationContext); }
    }
}
