// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace embedder
{
    // src: https://raw.githubusercontent.com/chgeuer/AzureLargeFileUploader/master/LargeFileUploaderUtils.cs

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Polly;

    using global::Microsoft.WindowsAzure.Storage;
    using global::Microsoft.WindowsAzure.Storage.Blob;

    public static class LargeFileUploaderUtils
    {
        const int kB = 1024;
        const int MB = kB * 1024;
        const long GB = MB * 1024;
        public static int NumBytesPerChunk = 4 * MB; // A block may be up to 4 MB in size. 
        public static Action<string> Log { get; set; }
        public static void UseConsoleForLogging() { Log = Console.Out.WriteLine; }
        const uint DEFAULT_PARALLELISM = 1;

        #region overloads

        public static Task<string> UploadAsync(string inputFile, string storageConnectionString, string containerName, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            return (new FileInfo(inputFile)).UploadAsync(CloudStorageAccount.Parse(storageConnectionString), containerName, uploadParallelism);
        }

        public static Task<string> UploadAsync(this FileInfo file, CloudStorageAccount storageAccount, string containerName, string blobName, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            return UploadAsync(
                fetchLocalData: (offset, length) => file.GetFileContentAsync(offset, length),
                blobLenth: file.Length,
                storageAccount: storageAccount,
                containerName: containerName,
                blobName: blobName,
                uploadParallelism: uploadParallelism);
        }

        public static Task<string> UploadAsync(this FileInfo file, CloudStorageAccount storageAccount, string containerName, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            return UploadAsync(
                fetchLocalData: (offset, length) => file.GetFileContentAsync(offset, (int)length),
                blobLenth: file.Length,
                storageAccount: storageAccount,
                containerName: containerName,
                blobName: file.Name,
                uploadParallelism: uploadParallelism);
        }

        public static Task<string> UploadAsync(this FileInfo file, CloudBlockBlob blockBlob, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            return UploadAsync(
                fetchLocalData: (offset, length) => file.GetFileContentAsync(offset, (int)length),
                blobLenth: file.Length,
                blockBlob: blockBlob,
                uploadParallelism: uploadParallelism);
        }

        public static Task<string> UploadAsync(this byte[] data, CloudStorageAccount storageAccount, string containerName, string blobName, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            return UploadAsync(
                fetchLocalData: (offset, count) => { return Task.FromResult((new ArraySegment<byte>(data, (int)offset, (int)count)).Array); },
                blobLenth: data.Length,
                storageAccount: storageAccount,
                containerName: containerName,
                blobName: blobName,
                uploadParallelism: uploadParallelism);
        }

        public static async Task<string> UploadAsync(Func<long, int, Task<byte[]>> fetchLocalData, long blobLenth,
            CloudStorageAccount storageAccount, string containerName, string blobName, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference(containerName);
            await container.CreateIfNotExistsAsync();
            var blockBlob = container.GetBlockBlobReference(blobName);
            return await UploadAsync(fetchLocalData, blobLenth, blockBlob, uploadParallelism);
        }

        #endregion

        public static async Task<string> UploadAsync(Func<long, int, Task<byte[]>> fetchLocalData, long blobLenth,
            CloudBlockBlob blockBlob, uint uploadParallelism = DEFAULT_PARALLELISM)
        {
            const int MAXIMUM_UPLOAD_SIZE = 100 * MB;
            if (NumBytesPerChunk > MAXIMUM_UPLOAD_SIZE) { NumBytesPerChunk = MAXIMUM_UPLOAD_SIZE; }

            #region Which blocks exist in the file

            var allBlockInFile = Enumerable
                 .Range(0, 1 + ((int)(blobLenth / NumBytesPerChunk)))
                 .Select(_ => new BlockMetadata(_, blobLenth, NumBytesPerChunk))
                 .Where(block => block.Length > 0)
                 .ToList();
            var blockIdList = allBlockInFile.Select(_ => _.BlockId).ToList();

            #endregion

            #region Which blocks are already uploaded

            List<BlockMetadata> missingBlocks = null;
            try
            {
                var existingBlocks = (await blockBlob.DownloadBlockListAsync(
                        BlockListingFilter.Uncommitted,
                        AccessCondition.GenerateEmptyCondition(),
                        new BlobRequestOptions { },
                        new OperationContext { }))
                    .Where(_ => _.Length == NumBytesPerChunk)
                    .ToList();

                missingBlocks = allBlockInFile.Where(blockInFile => !existingBlocks.Any(existingBlock =>
                    existingBlock.Name == blockInFile.BlockId &&
                    existingBlock.Length == blockInFile.Length)).ToList();
            }
            catch (StorageException)
            {
                missingBlocks = allBlockInFile;
            }

            #endregion

            Func<BlockMetadata, Statistics, Task> uploadBlockAsync = async (block, stats) =>
            {
                DateTime start = DateTime.UtcNow;
                stats.Add(block.Length, start);

                var fetchLocalDataResult = await Policy
                   .Handle<Exception>()
                   .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                   .ExecuteAndCaptureAsync<byte[]>(() => fetchLocalData(block.Index, block.Length));

                if (fetchLocalDataResult.Outcome == OutcomeType.Failure)
                {
                    throw new Exception($"Could not read local file", fetchLocalDataResult.FinalException);
                }

                byte[] blockData = fetchLocalDataResult.Result;

                string contentHash = md5()(blockData);

                var putBlockAsyncResult = await Policy
                   .Handle<Exception>()
                   .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                   .ExecuteAndCaptureAsync(() => blockBlob.PutBlockAsync(
                        blockId: block.BlockId,
                        blockData: new MemoryStream(blockData, true),
                        contentMD5: contentHash,
                        accessCondition: AccessCondition.GenerateEmptyCondition(),
                        options: new BlobRequestOptions { StoreBlobContentMD5 = true, UseTransactionalMD5 = true },
                        operationContext: new OperationContext()));

                if (putBlockAsyncResult.Outcome == OutcomeType.Failure)
                {
                    throw new Exception($"Could not call PutBlockAsync", putBlockAsyncResult.FinalException);
                }
            };

            var s = new Statistics(missingBlocks.Sum(b => (long)b.Length));

            await LargeFileUploaderUtils.ForEachAsync(
                source: missingBlocks,
                parallelTasks: 4,
                task: blockMetadata => uploadBlockAsync(blockMetadata, s));

            var putBlockListAsyncResult = await Policy
                   .Handle<Exception>()
                   .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)))
                   .ExecuteAndCaptureAsync(() => blockBlob.PutBlockListAsync(blockIdList));

            if (putBlockListAsyncResult.Outcome == OutcomeType.Failure)
            {
                throw new Exception($"Could not call PutBlockListAsync", putBlockListAsyncResult.FinalException);
            }

            log("PutBlockList succeeded, finished upload to {0}", blockBlob.Uri.AbsoluteUri);

            return blockBlob.Uri.AbsoluteUri;
        }

        public static async Task<string> DownloadRecomputeMD5Async(this CloudBlockBlob blockBlob)
        {
            // http://blog.monogram.sk/pokojny/2011/09/25/calculating-hash-while-processing-stream/
            using (var stream = await blockBlob.OpenReadAsync())
            {
                using (MD5 md5 = MD5.Create())
                {
                    var hashValue = md5.ComputeHash(stream);
                    
                    return Convert.ToBase64String(hashValue);
                }
            }
        }

        public static async Task<string> DownloadRecomputeAndSetMD5Async(this CloudBlockBlob blockBlob)
        {
            var md5 = await blockBlob.DownloadRecomputeMD5Async();
            await blockBlob.FetchAttributesAsync();
            blockBlob.Properties.ContentMD5 = md5;
            await blockBlob.SetPropertiesAsync();
            return md5;
        }

        internal static void log(string format, params object[] args)
        {
            if (Log != null) { Log(string.Format(format, args)); }
        }

        public static async Task<byte[]> GetFileContentAsync(this FileInfo file, long offset, int length)
        {
            using (var stream = file.OpenRead())
            {
                stream.Seek(offset, SeekOrigin.Begin);

                byte[] contents = new byte[length];
                var len = await stream.ReadAsync(contents, 0, contents.Length);
                if (len == length)
                {
                    return contents;
                }

                byte[] rest = new byte[len];
                Array.Copy(contents, rest, len);
                return rest;
            }
        }

        public static CloudStorageAccount ToStorageAccount(this string connectionString)
        {
            return CloudStorageAccount.Parse(connectionString);
        }

        internal static Task ForEachAsync<T>(this IEnumerable<T> source, int parallelTasks, Func<T, Task> task)
        {
            return Task.WhenAll(
                Partitioner
                .Create(source)
                .GetPartitions(parallelTasks)
                .Select(partition => Task.Run(async () =>
                {
                    using (partition)
                    {
                        while (partition.MoveNext())
                        {
                            await task(partition.Current);
                        }
                    }
                })));
        }

        public static Func<byte[], string> md5()
        {
            var hashFunction = MD5.Create();

            return (content) => Convert.ToBase64String(hashFunction.ComputeHash(content));
        }

        internal class BlockMetadata
        {
            internal BlockMetadata(int id, long length, int bytesPerChunk)
            {
                this.Id = id;
                this.BlockId = Convert.ToBase64String(System.BitConverter.GetBytes(id));
                this.Index = ((long)id) * ((long)bytesPerChunk);
                long remainingBytesInFile = length - this.Index;
                this.Length = (int)Math.Min(remainingBytesInFile, (long)bytesPerChunk);
            }

            public long Index { get; private set; }
            public int Id { get; private set; }
            public string BlockId { get; private set; }
            public int Length { get; private set; }
        }

        internal class Statistics
        {
            public Statistics(long totalBytes) { this.TotalBytes = totalBytes; }

            internal readonly DateTime InitialStartTime = DateTime.UtcNow;

            internal readonly object _lock = new object();
            internal long TotalBytes { get; private set; }
            internal long Done { get; private set; }

            internal void Add(long moreBytes, DateTime start)
            {
                long done;
                lock (_lock)
                {
                    this.Done += moreBytes;
                    done = this.Done;
                }

                var kbPerSec = (((double)moreBytes) / (DateTime.UtcNow.Subtract(start).TotalSeconds * kB));
                var MBPerMin = (((double)moreBytes) / (DateTime.UtcNow.Subtract(start).TotalMinutes * MB));

                log(
                    "Uploaded {0} ({1}) with {2} kB/sec ({3} MB/min), {4}",
                    absoluteProgress(done, this.TotalBytes),
                    relativeProgress(done, this.TotalBytes),
                    kbPerSec.ToString("F0"),
                    MBPerMin.ToString("F1"),
                    estimatedArrivalTime()
                    );
            }

            internal string estimatedArrivalTime()
            {
                var now = DateTime.UtcNow;

                double elapsedSeconds = now.Subtract(InitialStartTime).TotalSeconds;
                double progress = ((double)this.Done) / ((double)this.TotalBytes);

                if (this.Done == 0) return "unknown time";

                double remainingSeconds = elapsedSeconds * (1 - progress) / progress;

                TimeSpan remaining = TimeSpan.FromSeconds(remainingSeconds);

                return string.Format("{0} remaining, (expect to finish by {1} local time)",
                    remaining.ToString("g"),
                    now.ToLocalTime().Add(remaining));
            }

            private static string absoluteProgress(long current, long total)
            {
                if (total < kB)
                {
                    // Bytes is reasonable
                    return string.Format("{0} of {1} bytes", current, total);
                }
                else if (total < 10 * MB)
                {
                    // kB is a reasonable unit
                    return string.Format("{0} of {1} kByte", (current / kB), (total / kB));
                }
                else if (total < 10 * GB)
                {
                    // MB is a reasonable unit
                    return string.Format("{0} of {1} MB", (current / MB), (total / MB));
                }
                else
                {
                    // GB is a reasonable unit
                    return string.Format("{0} of {1} GB", (current / GB), (total / GB));
                }
            }

            private static string relativeProgress(long current, long total)
            {
                return string.Format("{0} %",
                    (100.0 * current / total).ToString("F3"));
            }
        }
    }
}