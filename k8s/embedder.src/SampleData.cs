// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace embedder
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class SampleData
    {
        public static Dictionary<string, EmbedderJobDTO> PreprocessorAndEncoderJob { get; } = ((Func<Dictionary<string, EmbedderJobDTO>>)(() => {
            var queue = "https://mediahack.queue.core.windows.net:443/notifications?st=2017-06-29T15%3A41%3A00Z&se=2017-07-30T15%3A41%3A00Z&sp=raup&sv=2015-12-11&sig=6%2BgmtB96k%2FJzcjBAdGQZmH%2Bedw4oAZJqEuznRTYKX2g%3D";

            // Input MP4
            var c = "https://mediahack.blob.core.windows.net/asset-80dcf6de-33bb-4ddc-89b7-1500afdd9736";
            var bs = new[] {
                new { filename = "Microsoft%20Azure%20Datacenter_320x180_400.mp4", gopsize = 2, bitrate = 400, parameters = "resize:width=320,height=180" },
                new { filename = "Microsoft%20Azure%20Datacenter_640x360_650.mp4", gopsize = 2, bitrate = 650, parameters = "resize:width=640,height=360" },
                new { filename = "Microsoft%20Azure%20Datacenter_640x360_1000.mp4", gopsize = 2, bitrate = 1000, parameters = "resize:width=640,height=360" },
                new { filename = "Microsoft%20Azure%20Datacenter_960x540_1500.mp4", gopsize = 2, bitrate = 1500, parameters = "resize:width=960,height=540" },
                new { filename = "Microsoft%20Azure%20Datacenter_960x540_2250.mp4", gopsize = 2, bitrate = 2250, parameters = "resize:width=960,height=540" },
                new { filename = "Microsoft%20Azure%20Datacenter_1280x720_3400.mp4", gopsize = 2, bitrate = 3400, parameters = "resize:width=1280,height=720" },
                new { filename = "Microsoft%20Azure%20Datacenter_1920x1080_4700.mp4", gopsize = 2, bitrate = 4700, parameters = "resize:width=1920,height=1080" },
                new { filename = "Microsoft%20Azure%20Datacenter_1920x1080_6000.mp4", gopsize = 2, bitrate = 6000, parameters = "resize:width=1920,height=1080" }
            };
            var s = "?st=2017-06-29T15%3A29%3A00Z&se=2017-07-30T15%3A29%3A00Z&sp=r&sv=2015-12-11&sr=c&sig=DJ30OVgV8rAX%2BTyJ65xWbqrCY8l59PdQmoTEPjfj5BM%3D";

            // MMRK
            var mmrkContainerUrl = "https://mediahack.blob.core.windows.net/mmrk";
            var mmrkContainerSas = "?st=2017-06-29T15%3A29%3A00Z&se=2017-08-30T15%3A29%3A00Z&sp=rw&sv=2015-12-11&sr=c&sig=zrsWLha%2FQKjUaBpne0bT0BTO5Fm5bLUBSpFG%2FO2TN5Q%3D";

            var embedDetails = new[]
            {
                new { UserID = "0x123456", WatermarkURL = "https://mediahack.blob.core.windows.net/asset-80dcf6de-33bb-4ddc-89b7-1500afdd9736-0x123456?st=2017-06-29T15%3A29%3A00Z&se=2017-09-30T15%3A29%3A00Z&sp=rw&sv=2015-12-11&sr=c&sig=6gOwBCNG3xRnbJ%2F1OpNmg48MkuO0GsvPdg84HnHCPGU%3D" },
                new { UserID = "0x654321", WatermarkURL = "https://mediahack.blob.core.windows.net/asset-80dcf6de-33bb-4ddc-89b7-1500afdd9736-0x654321?st=2017-06-29T15%3A29%3A00Z&se=2017-07-30T15%3A29%3A00Z&sp=rw&sv=2015-12-11&sr=c&sig=t78%2FAWclQpw9%2F2RqF3v8%2Bp2g%2BFr4sL0s%2FsrWB8gjMOw%3D" }
            };

            var preprocessorAndEmbedderJob = new EmbedderJobDTO
            {
                JobID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736-full",
                AssetID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736",
                PreprocessorNotificationQueue = queue,
                EmbedderNotificationQueue = queue,
                PreprocessorItems = bs.Select(b => new VideoInformation
                {
                    FileName = b.filename,
                    VideoURL = $"{c}/{b.filename}{s}",
                    MmrkUrl = $"{mmrkContainerUrl}/{b.filename.Replace("mp4", "mmrk")}{mmrkContainerSas}",
                    GOPSize = b.gopsize,
                    VideoBitrate = b.bitrate,
                    VideoFilter = b.parameters
                }).ToArray(),
                EmbedderJobs = embedDetails.Select(_ => new EmbedderJobs
                {
                    UserID = _.UserID,
                    EmbedderItems = bs.Select(b => new EmbedderItems
                    {
                        WaterMarkedMp4 = _.WatermarkURL.Replace("?", $"/{b.filename}?"),
                        FileName = b.filename
                    }).ToArray()
                }).ToArray()
            };

            var preprocessorJob = new EmbedderJobDTO
            {
                JobID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736-full",
                AssetID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736",
                PreprocessorNotificationQueue = queue,
                EmbedderNotificationQueue = queue, 
                PreprocessorItems = bs.Select(b => new VideoInformation
                {
                    FileName = b.filename,
                    VideoURL = $"{c}/{b.filename}{s}",
                    MmrkUrl = $"{mmrkContainerUrl}/{b.filename.Replace("mp4", "mmrk")}{mmrkContainerSas}",
                    GOPSize = b.gopsize,
                    VideoBitrate = b.bitrate,
                    VideoFilter = b.parameters
                }).ToArray()
            };

            var embedderJob = new EmbedderJobDTO
            {
                JobID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736-full",
                AssetID = "nb:cid:UUID:80dcf6de-33bb-4ddc-89b7-1500afdd9736",
                PreprocessorNotificationQueue = queue,
                EmbedderNotificationQueue = queue,
                PreprocessorItems = bs.Select(b => new VideoInformation
                {
                    FileName = b.filename,
                    MmrkUrl = $"{mmrkContainerUrl}/{b.filename.Replace("mp4", "mmrk")}{mmrkContainerSas}"
                }).ToArray(),
                EmbedderJobs = embedDetails.Select(_ => new EmbedderJobs
                {
                    UserID = _.UserID,
                    EmbedderItems = bs.Select(b => new EmbedderItems
                    {
                        WaterMarkedMp4 = _.WatermarkURL.Replace("?", $"/{b.filename}?"),
                        FileName = b.filename
                    }).ToArray()
                }).ToArray()
            };

            return new Dictionary<string, EmbedderJobDTO> {
                { "preprocessorAndEmbedderJob", preprocessorAndEmbedderJob },
                { "preprocessorJob", preprocessorJob },
                { "embedderJob", embedderJob }
            };
        }))();
    }
}
