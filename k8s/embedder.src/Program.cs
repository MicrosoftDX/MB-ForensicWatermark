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
    using static embedder.Utils;
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


        public static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();


        static async Task<int> MainAsync(string[] args)
        {
            Action<string> stdout = Console.Out.WriteLine;
            Action<string> stderr = Console.Error.WriteLine;

            #region Install licenses

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LICENSES")))
            {
                stderr("Could not determine licenses from environment...");
                return -1;
            }
            LicenseData.InjectIntoFilesystem(environmentVariable: "LICENSES");

            #endregion

            #region Read Job

            EmbedderJobDTO job = null;
            var jobEnvironmentVariable = Environment.GetEnvironmentVariable("JOB");
            if (string.IsNullOrEmpty(jobEnvironmentVariable))
            {
                stderr("Could not retrieve job from 'JOB' environment variable...");
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
                    stderr("Could not read job description from 'JOB' environment variable");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                stderr($"Could not parse the job description: {ex.Message}");
                stderr($"JOB ==  {jobEnvironmentVariable}");
                return -1; 
            }

            #endregion

            var preprocessorData = Program.DeterminePreprocessorJobs(job);
            foreach (var pd in preprocessorData)
            {
                // run these compute-intensive jobs sequentially
                stdout($"***Start Preprocessor {DateTime.Now.ToString()}");
                await Program.RunPreprocessorAsync(pd, stdout, stderr);
                stdout($"***Finish Preprocessor {DateTime.Now.ToString()}");
            }

            stdout($"****Start Embedder {DateTime.Now.ToString()}");
            var embedderData = Program.DetermineEmbedderJobs(job);
            var embedderTasks = embedderData.Select(_ => Program.RunEmbedderAsync(_, stdout, stderr));
            await Task.WhenAll(embedderTasks);
            stdout($"****Finish Embedder {DateTime.Now.ToString()}");

            #region Delete all video files from pod filesystem

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

            foreach (var ed in embedderData)
            {
                Policy
                    .Handle<Exception>()

                    .WaitAndRetry(
                        retryCount: 5, 
                        sleepDurationProvider: attempt => TimeSpan.FromSeconds(1))

                    .Execute(() =>
                    {
                        if (ed.WatermarkedFile.Exists)
                        {
                            ed.WatermarkedFile.Delete();
                        }
                    });
            }

            #endregion

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
        
        private static async Task RunPreprocessorAsync(PreprocessorJob _, Action<string> stdout, Action<string> stderr)
        {
            ExecutionResult output;

            if (_.RunPreprocessorAndUploadMMRK)
            {
                #region Download MP4
                stdout($"***Start Download MP4 {DateTime.Now.ToString()}");
                output = await _.Mp4URL.DownloadToAsync(_.LocalFile);
                if (output.Success)
                {
                    stdout(output.Output);
                }
                else
                {
                    stderr(output.Output);
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
                        stderr(queueOutput.Output);
                    }

                    return;
                }
                stdout($"*** End Download MP4 {DateTime.Now.ToString()}");

                #endregion

                #region Pass 1
                //--x264encopts --bframes 2 --b-pyramid none --b-adapt 0 --no-scenecut
                stdout($"*** Start PREPROCESSOR1  {DateTime.Now.ToString()}");

                output = await Utils.RunProcessAsync(
                    prefix: "PREPROCESSOR1",
                    additionalEnvironment: new Dictionary<string, string> { { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" } },
                    fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                    //Original
                    //arguments: new[] {
                    //    $"--infile {_.LocalFile.FullName}",
                    //    $"--stats {_.StatsFile.FullName}",
                    //    $"--pass 1",
                    //    $"--vbitrate {_.VideoBitrate}",
                    //    $"--gopsize {_.GOPSize}",
                    //    "--x264encopts",
                    //    //"--bframes 2",
                    //    //"--b-pyramid none",
                    //    //"--b-adapt 0",
                    //    "--keyint 60",
                    //    "--min-keyint 60",
                    //    "--no-scenecut"

                    //}

                    //TEST 2
                    //arguments: new[] {
                    //    $"--infile {_.LocalFile.FullName}",
                    //    $"--stats {_.StatsFile.FullName}",
                    //    $"--pass 1",
                    //    $"--vbitrate {(_.VideoBitrate/1000)}K",
                    //    $"--gopsize {_.GOPSize}",
                    //    "--x264encopts",
                    //    "--keyint 60",
                    //    "--min-keyint 60",
                    //    "--no-scenecut"
                    //}
                    //TEST 3
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
                    stdout(output.Output);
                }
                else
                {
                    stderr(output.Output);
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
                        stderr(queueOutput.Output);
                    }

                    return;
                }
                stdout($"*** End PREPROCESSOR1  {DateTime.Now.ToString()}");

                #endregion

                #region Pass 2
                stdout($"***Start PREPROCESSOR2  {DateTime.Now.ToString()}");

                output = await Utils.RunProcessAsync(
                   prefix: "PREPROCESSOR1",
                   additionalEnvironment: new Dictionary<string, string> { { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" } },
                   fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                   //Original
                   //arguments: new[] {
                   //     $"--infile {_.LocalFile.FullName}",
                   //     $"--stats {_.StatsFile.FullName}",
                   //     $"--outfile {_.MmrkFile.FullName}",
                   //     $"--pass 2",
                   //     $"--vbitrate {_.VideoBitrate}", $"--gopsize {_.GOPSize}",
                   //     string.IsNullOrEmpty(_.VideoFilter) ? "" : $"--x264encopts --video-filter {_.VideoFilter}",
                   //     //"--bframes 2",
                   //     //"--b-pyramid none",
                   //     //"--b-adapt 0",
                   //      "--keyint 60",
                   //     "--min-keyint 60",
                   //     "--no-scenecut"
                   //}
                   //TEST 2
                   //arguments: new[] {
                   //     $"--infile {_.LocalFile.FullName}",
                   //     $"--stats {_.StatsFile.FullName}",
                   //     $"--outfile {_.MmrkFile.FullName}",
                   //     $"--pass 2",
                   //     $"--vbitrate {(_.VideoBitrate/1000)}K",
                   //     $"--gopsize {_.GOPSize}",
                   //     "--x264encopts",
                   //     "--keyint 60",
                   //    "--min-keyint 60",
                   //    "--no-scenecut"
                   //}
                   //TEST3
                   arguments: new[] {
                        $"--infile {_.LocalFile.FullName}",
                        $"--stats {_.StatsFile.FullName}",
                        $"--outfile {_.MmrkFile.FullName}",
                        $"--pass 2",
                        $"--vbitrate {(_.VideoBitrate)}",
                        $"--gopsize {_.GOPSize}",
                        "--x264encopts",
                        "--keyint 60",
                       "--min-keyint 60",
                       "--no-scenecut"
                   }
                );
                if (output.Success && _.MmrkFile.Exists)
                {
                    stdout(output.Output);
                }
                else
                {
                    stderr(output.Output);
                    stdout($"***Start  PREPROCESSOR Error Notification Message {DateTime.Now.ToString()}");
                    var queueOutput = await _.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = _.Job.AssetID,
                        JobID = _.Job.JobID,
                        FileName = _.Name,
                        Status = JobStatus.Error,
                        JobOutput = output.Output,
                        Stage = PreprocessorStage.Pass2
                    });
                    stdout($"***End  PREPROCESSOR  Error Notification Message {DateTime.Now.ToString()}");


                    if (!queueOutput.Success)
                    {
                        stderr(queueOutput.Output);
                    }

                    return;
                }
                stdout($"***End PREPROCESSOR2 {DateTime.Now.ToString()}");

                #endregion

                #region Delete MP4

                // Download the input MP4 after MMRK is generated
                if (_.LocalFile.Exists)
                {
                    _.LocalFile.Delete();
                }

                #endregion

                #region Upload MMRK
                stdout($"***Start Upload MMRK {DateTime.Now.ToString()}");

                output = await _.MmrkFile.UploadToAsync(_.MmmrkURL);
                if (output.Success)
                {
                    stdout(output.Output);
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
                        stderr(queueOutput.Output);
                    }
                }
                else
                {
                    stderr(output.Output);
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
                        stderr(queueOutput.Output);
                    }

                    return;
                }
                stdout($"***End Upload MMRK {DateTime.Now.ToString()}");

                #endregion
            }
            else
            {
                #region Download MMRK
                stdout($"***Start Download MMRK MMRK {DateTime.Now.ToString()}");

                output = await _.MmmrkURL.DownloadToAsync(_.MmrkFile);
                if (output.Success)
                {
                    stdout(output.Output);
                }
                else
                {
                    stderr(output.Output);
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
                        stderr(queueOutput.Output);
                    }

                    return;
                }
                stdout($"***End Download MMRK MMRK {DateTime.Now.ToString()}");

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

        private static async Task RunEmbedderAsync(EmbedderJob _, Action<string> stdout, Action<string> stderr)
        {
            #region Embedder

            var embedderOutput = await Utils.RunProcessAsync(
                prefix: "EMBEDDER",
                fileName: "/usr/bin/NGS_SmartEmbedderCLI",
                arguments: new[] {
                    _.MmrkFile.FullName,
                    _.UserID,
                    _.WatermarkedFile.FullName }
            );
            if (embedderOutput.Success)
            {
                stdout(embedderOutput.Output);
            }
            else
            {
                stderr(embedderOutput.Output);
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
                    stderr(queueOutput.Output);
                }

                return;
            }

            #endregion

            #region Upload

            var uploadResult = await _.WatermarkedFile.UploadToAsync(_.WatermarkedURL);
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
                    stderr(queueOutput.Output);
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
                    stderr(queueOutput.Output);
                }
            }

            #endregion
        }
    }
}
