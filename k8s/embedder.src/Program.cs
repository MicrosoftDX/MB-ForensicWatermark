// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace embedder
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Microsoft.WindowsAzure.Storage.Queue;
    using Polly;

    class Program
    {
        #region helper data structures 

        internal class PreprocessorJob
        {
            internal EmbedderJobDTO Job { get; set; }
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
        }

        internal class EmbedderJob
        {
            internal EmbedderJobDTO Job { get; set; }
            public string Name { get; set; }
            public string UserID { get; set; }
            public Uri MmrkURL { get; set; }
            public FileInfo MmrkFile { get; set; }
            public Uri WatermarkedURL { get; set; }
            public FileInfo WatermarkedFile { get; set; }
            public CloudQueue Queue { get; set; }
        }

        #endregion

        #region Logging

        enum Category
        {
            Main = 0,
            DownloadMP4,
            PreprocessorStep1,
            PreprocessorStep2,
            UploadMMRK,
            DownloadMMRK,
            Embedder,
            UploadWatermarked,
            QueueNotifications
        }

        static readonly Dictionary<Category, string> CategoryName = new Dictionary<Category, string>
        {
            { Category.Main,               "Main               " },
            { Category.DownloadMP4,        "Download MP4       " },
            { Category.PreprocessorStep1,  "Preprocessor Step 1" },
            { Category.PreprocessorStep2,  "Preprocessor Step 2" },
            { Category.UploadMMRK,         "Upload MMRK        " },
            { Category.DownloadMMRK,       "Download MMRK      " },
            { Category.Embedder,           "Embedder           " },
            { Category.UploadWatermarked,  "Upload Result MP4  " },
            { Category.QueueNotifications, "Send Queue Message " }
        };

        static void stdout(Category category, string message) => print(category, message, Console.Out);
        static void stderr(Category category, string message) => print(category, message, Console.Error);

        static void print(Category category, string message, TextWriter writer)
        {
            writer.WriteLine($"*** {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss Z")}: {CategoryName[category]}: {message}");
        }

        #endregion

        public static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        static async Task<int> MainAsync(string[] args)
        {

            #region Install licenses

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LICENSES")))
            {
                stderr(Category.Main, "Could not determine licenses from environment...");
                return -1;
            }
            LicenseData.InjectIntoFilesystem(environmentVariable: "LICENSES");

            #endregion

            #region Read Job

            EmbedderJobDTO job = null;
            var jobEnvironmentVariable = Environment.GetEnvironmentVariable("JOB");
            if (string.IsNullOrEmpty(jobEnvironmentVariable))
            {
                stderr(Category.Main, "Could not retrieve job from 'JOB' environment variable...");
                return -1;
            }
            try
            {
                // cat juan.json | jq -c -M . | base64 --wrap=0 > juan.base64
                var json = Encoding.UTF8.GetString(
                    Convert.FromBase64String(jobEnvironmentVariable));
                job = JsonConvert.DeserializeObject<EmbedderJobDTO>(json);

                if (job == null)
                {
                    stderr(Category.Main, "Could not read job description from 'JOB' environment variable");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                stderr(Category.Main, $"Could not parse the job description: {ex.Message}");
                stderr(Category.Main, $"JOB == {jobEnvironmentVariable}");
                return -1;
            }

            #endregion

            var workFolder =
                Path.Combine("/mnt", job.JobID
                .Replace(":", "_")
                .Replace("/", "_"));

            stdout(Category.Main, $"Changing work directory to {workFolder}");

            Directory.CreateDirectory(workFolder);
            Environment.CurrentDirectory = workFolder;

            #region MMRK Generation

            var preprocessorData = Program.DeterminePreprocessorJobs(job);
            stdout(Category.Main, "Start Preprocessor");
            foreach (var pd in preprocessorData)
            {
                // run these compute-intensive jobs sequentially
                await Program.RunPreprocessorAsync(pd, stdout, stderr);
            }
            stdout(Category.Main, "Finished Preprocessor");

            #endregion

            #region Watermarking

            Func<int> getNumberOfParallelEmbedderTasks = () =>
            {
                int result;
                if (int.TryParse(Environment.GetEnvironmentVariable("PARALLELEMBEDDERS"), out result))
                {
                    return result;
                }
                return 5;
            };

            var parallelEmbedderTasks = getNumberOfParallelEmbedderTasks();

            stdout(Category.Main, "Start Embedder");
            var embedderData = Program.DetermineEmbedderJobs(job);

            await embedderData.ForEachAsync(
                parallelTasks: parallelEmbedderTasks, 
                task: _ => Program.RunEmbedderAsync(_, stdout, stderr));

            stdout(Category.Main, "Finished Embedder");

            #endregion

            #region Delete all MMRK files from pod filesystem

            foreach (var pd in preprocessorData)
            {
                Policy
                    .Handle<Exception>()
                    .WaitAndRetry(
                        retryCount: 5, 
                        sleepDurationProvider: attempt => TimeSpan.FromSeconds(1))
                    .Execute(() =>
                    {
                        if (pd.MmrkFile.Exists)
                        {
                            pd.MmrkFile.Delete();
                        }
                    });
            }

            #endregion

            Environment.CurrentDirectory = "/";
            stdout(Category.Main, $"Removing work directory {workFolder}");
            Directory.Delete(workFolder, recursive: true);

            return 0;
        }

        private static IEnumerable<PreprocessorJob> DeterminePreprocessorJobs(EmbedderJobDTO job)
        {
            var preprocessorQueue = new CloudQueue(job.PreprocessorNotificationQueue.AsUri());

            var pj =  job.PreprocessorItems.Select(_ => new PreprocessorJob
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
        
        private static async Task RunPreprocessorAsync(PreprocessorJob _, Action<Category, string> stdout, Action<Category, string> stderr)
        {
            ExecutionResult output;

            if (_.RunPreprocessorAndUploadMMRK)
            {
                #region Download MP4

                stdout(Category.DownloadMP4, $"Start Download MP4 {_.Mp4URL.AbsoluteUri}");
                output = await _.Mp4URL.DownloadToAsync(_.LocalFile);
                if (output.Success)
                {
                    stdout(Category.DownloadMP4, output.Output);
                }
                else
                {
                    stderr(Category.DownloadMP4, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output, 
                        Stage = PreprocessorStage.DownloadMP4
                    });
                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }

                    return;
                }
                stdout(Category.DownloadMP4, "Finished Download MP4 {_.Mp4URL.AbsoluteUri}");

                #endregion

                #region Pass 1

                //--x264encopts --bframes 2 --b-pyramid none --b-adapt 0 --no-scenecut
                stdout(Category.PreprocessorStep1, $"Start {_.LocalFile.FullName}");

                output = await Utils.RunProcessAsync(
                    prefix: "PREPROCESSOR1",
                    additionalEnvironment: new Dictionary<string, string> { { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" } },
                    fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                    arguments: new[] {
                        $"--infile {_.LocalFile.FullName}",
                        $"--stats {_.StatsFile.FullName}",
                        $"--pass 1",
                        $"--vbitrate {(_.VideoBitrate)}",
                        $"--gopsize {_.GOPSize}",
                        "--x264encopts",
                        "--keyint 60",
                        "--min-keyint 60",
                        "--no-scenecut"
                    }
                );

                if (output.Success && _.StatsFile.Exists)
                {
                    stdout(Category.PreprocessorStep1, output.Output);
                }
                else
                {
                    stderr(Category.PreprocessorStep1, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output,
                        Stage = PreprocessorStage.Pass1
                    });
                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }

                    return;
                }
                stdout(Category.PreprocessorStep1, $"Finished {_.LocalFile.FullName}");

                #endregion

                #region Pass 2
                stdout(Category.PreprocessorStep2, $"Start {_.LocalFile.FullName}");

                output = await Utils.RunProcessAsync(
                   prefix: "PREPROCESSOR2",
                   additionalEnvironment: new Dictionary<string, string> { { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" } },
                   fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                   //Original arguments: new[] { $"--infile {_.LocalFile.FullName}", $"--stats {_.StatsFile.FullName}", $"--outfile {_.MmrkFile.FullName}", $"--pass 2", $"--vbitrate {_.VideoBitrate}", $"--gopsize {_.GOPSize}", string.IsNullOrEmpty(_.VideoFilter) ? "" : $"--x264encopts --video-filter {_.VideoFilter}", "--bframes 2", "--b-pyramid none", "--b-adapt 0", "--keyint 60", "--min-keyint 60", "--no-scenecut" }
                   //TEST 2 arguments: new[] { $"--infile {_.LocalFile.FullName}", $"--stats {_.StatsFile.FullName}", $"--outfile {_.MmrkFile.FullName}", $"--pass 2", $"--vbitrate {(_.VideoBitrate/1000)}K", $"--gopsize {_.GOPSize}", "--x264encopts", "--keyint 60", "--min-keyint 60", "--no-scenecut" }
                   //TEST3
                   arguments: new[] {
                        $"--infile {_.LocalFile.FullName}",
                        $"--stats {_.StatsFile.FullName}",
                        $"--outfile {_.MmrkFile.FullName}",
                        $"--pass 2",
                        $"--vbitrate {(_.VideoBitrate)}",
                        $"--gopsize {_.GOPSize}",
                        "--x264encopts", "--keyint 60", "--min-keyint 60", "--no-scenecut"
                   }
                );
                if (output.Success && _.MmrkFile.Exists)
                {
                    stdout(Category.PreprocessorStep2, output.Output);
                }
                else
                {
                    stderr(Category.PreprocessorStep2, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output,
                        Stage = PreprocessorStage.Pass2
                    });

                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }

                    return;
                }
                stdout(Category.PreprocessorStep2, $"Finished {_.LocalFile.FullName}");

                #endregion

                #region Delete MP4

                // Delete the input MP4 after MMRK is generated
                if (_.LocalFile.Exists)
                {
                    stdout(Category.Main, $"Deleting {_.LocalFile.FullName}");
                    _.LocalFile.Delete();
                }

                #endregion

                #region Upload MMRK
                stdout(Category.UploadMMRK, $"Start Upload MMRK {_.MmrkFile.FullName}");

                output = await _.MmrkFile.UploadToAsync(_.MmmrkURL);
                if (output.Success)
                {
                    stdout(Category.UploadMMRK, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Finished,
                        JobOutput = output.Output,
                        Stage = PreprocessorStage.UploadMMRK
                    });
                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }
                }
                else
                {
                    stderr(Category.UploadMMRK, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output,
                        Stage = PreprocessorStage.UploadMMRK
                    });
                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }

                    return;
                }
                stdout(Category.UploadMMRK, $"End Upload MMRK {_.MmrkFile.FullName}");

                #endregion
            }
            else
            {
                #region Download MMRK
                stdout(Category.DownloadMMRK, $"Start Download MMRK {_.MmrkFile.FullName}");

                output = await _.MmmrkURL.DownloadToAsync(_.MmrkFile);
                if (output.Success)
                {
                    stdout(Category.DownloadMMRK, output.Output);
                }
                else
                {
                    stderr(Category.DownloadMMRK, output.Output);
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationEmbedder
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output,
                        Stage = EmbedderStage.DownloadMMRK
                    });
                    if (!queueOutput.Success)
                    {
                        stderr(Category.QueueNotifications, queueOutput.Output);
                    }

                    return;
                }
                stdout(Category.DownloadMMRK, $"Finished Download MMRK {_.MmrkFile.FullName}");

                #endregion
            }
        }

        private static IEnumerable<EmbedderJob> DetermineEmbedderJobs(EmbedderJobDTO job)
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

        private static async Task RunEmbedderAsync(EmbedderJob _, Action<Category, string> stdout, Action<Category, string> stderr)
        {
            #region Embedder

            stdout(Category.Embedder, $"Start embed {_.UserID} into {_.WatermarkedFile.FullName}");

            var embedderOutput = await Utils.RunProcessAsync(
                prefix: "EMBEDDER",
                fileName: "/usr/bin/NGS_SmartEmbedderCLI",
                arguments: new[] {
                    _.MmrkFile.FullName,
                    _.UserID,
                    _.WatermarkedFile.FullName }
            );

            stdout(Category.Embedder, $"Finished embed {_.UserID} into {_.WatermarkedFile.FullName}");

            if (embedderOutput.Success)
            {
                stdout(Category.Embedder, embedderOutput.Output);
            }
            else
            {
                stderr(Category.Embedder, embedderOutput.Output);
                var queueOutput = await _.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = _.Job.AssetID,
                    JobID = _.Job.JobID,
                    FileName = _.Name,
                    UserID = _.UserID,
                    Status = JobStatus.Error,
                    JobOutput = embedderOutput.Output
                });
                if (!queueOutput.Success)
                {
                    stderr(Category.QueueNotifications, queueOutput.Output);
                }

                return;
            }

            #endregion

            #region Upload

            stdout(Category.UploadWatermarked, $"Start upload {_.UserID} {_.WatermarkedFile.FullName}");
            var uploadResult = await _.WatermarkedFile.UploadToAsync(_.WatermarkedURL);
            stdout(Category.UploadWatermarked, $"Finished upload {_.UserID} {_.WatermarkedFile.FullName}");
            if (uploadResult.Success)
            {
                var queueOutput = await _.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = _.Job.AssetID,
                    JobID = _.Job.JobID,
                    FileName = _.Name,
                    UserID = _.UserID,
                    Status = JobStatus.Finished,
                    JobOutput = uploadResult.Output
                });
                if (!queueOutput.Success)
                {
                    stderr(Category.QueueNotifications, queueOutput.Output);
                }
            }
            else
            {
                var queueOutput = await _.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = _.Job.AssetID,
                    JobID = _.Job.JobID,
                    FileName = _.Name,
                    UserID = _.UserID,
                    Status = JobStatus.Error, 
                    JobOutput = uploadResult.Output
                });
                if (!queueOutput.Success)
                {
                    stderr(Category.QueueNotifications, queueOutput.Output);
                }
            }

            #endregion

            #region Delete watermarked file after upload

            Policy
               .Handle<Exception>()
               .WaitAndRetry(
                   retryCount: 5,
                   sleepDurationProvider: attempt => TimeSpan.FromSeconds(1))
               .Execute(() =>
               {
                   if (_.WatermarkedFile.Exists)
                   {
                       stdout(Category.Main, $"Delete {_.WatermarkedFile.FullName}");
                       _.WatermarkedFile.Delete();
                   }
               });

            #endregion
        }
    }
}