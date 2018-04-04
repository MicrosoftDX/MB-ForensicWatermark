namespace embedder
{
    using Microsoft.WindowsAzure.Storage.Queue;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.IO;

    public class PreprocessorJob
    {
        public EmbedderJobDTO Job { get; set; }
        public string Name { get; set; }
        public Uri Mp4URL { get; set; }
        public Uri MmmrkURL { get; set; }
        public int GOPSize { get; set; }
        public int VideoBitrate { get; set; }
        public string VideoFilter { get; set; }
        public bool RunPreprocessorAndUploadMMRK { get; set; }
        public FileInfo LocalFile { get; set; }
        public FileInfo StatsFile { get; set; }
        public FileInfo MmrkFile { get; set; }
        public CloudQueue Queue { get; set; }

        public static IEnumerable<PreprocessorJob> DeterminePreprocessorJobs(EmbedderJobDTO job)
        {
            var preprocessorQueue = new CloudQueue(job.PreprocessorNotificationQueue.AsUri());

            var pj = job.PreprocessorItems.Select(_ => new PreprocessorJob
            {
                Job = job,
                Name = _.FileName,
                LocalFile = _.FileName.AsLocalFile(),
                StatsFile = _.FileName.AsStatsFile(),
                MmrkFile = _.FileName.AsMmrkFile(),
                Mp4URL = _.VideoURL.AsUri(),
                MmmrkURL = _.MmrkUrl.AsUri(),
                GOPSize = _.GOPSize,
                VideoBitrate = _.VideoBitrate,
                VideoFilter = _.VideoFilter,
                RunPreprocessorAndUploadMMRK = !string.IsNullOrEmpty(_.VideoURL),
                Queue = preprocessorQueue
            });

            return pj;
        }
    }
}
