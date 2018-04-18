namespace embedder
{
    using Microsoft.WindowsAzure.Storage.Queue;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.IO;

    public class EmbedderJob
    {
        internal EmbedderJobDTO Job { get; set; }
        public string Name { get; set; }
        public string UserID { get; set; }
        public Uri MmrkURL { get; set; }
        public FileInfo MmrkFile { get; set; }
        public Uri WatermarkedURL { get; set; }
        public FileInfo WatermarkedFile { get; set; }
        public CloudQueue Queue { get; set; }

        public static IEnumerable<EmbedderJob> DetermineEmbedderJobs(EmbedderJobDTO job)
        {
            var embedderQueue = new CloudQueue(job.EmbedderNotificationQueue.AsUri());

            var ej = job.EmbedderJobs.SelectMany(
                _ => _.EmbedderItems,
                (a, b) => new EmbedderJob
                {
                    Job = job,
                    Name = b.FileName,
                    UserID = a.UserID,
                    MmrkURL = job.PreprocessorItems.FirstOrDefault(_ => _.FileName == b.FileName)?.MmrkUrl.AsUri(),
                    MmrkFile = b.FileName.AsMmrkFile(),
                    WatermarkedFile = b.FileName.AsWatermarkFileForUser(a.UserID),
                    WatermarkedURL = b.WaterMarkedMp4.AsUri(),
                    Queue = embedderQueue

                })
            .Where(_ => _.MmrkURL != null);

            return ej;
        }
    }
}
