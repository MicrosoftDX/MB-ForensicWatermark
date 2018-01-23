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

            //EmbedderJobDTO job = JsonConvert.DeserializeObject<EmbedderJobDTO>(
            //    File.ReadAllText(
            //        path: @"C:\github\chgeuer\MB-ForensicWatermark.public\k8s\job.json"));

            #endregion

            var workFolder =
                Path.Combine("/mnt", job.JobID
                .Replace(":", "_")
                .Replace("/", "_"));

            stdout($"Changing work directory to {workFolder}");

            Directory.CreateDirectory(workFolder);
            Environment.CurrentDirectory = workFolder;

            #region MMRK Generation

            var preprocessorData = Program.DeterminePreprocessorJobs(job);
            foreach (var pd in preprocessorData)
            {
                // run these compute-intensive jobs sequentially
                stdout($"***Start Preprocessor {DateTime.Now.ToString()}");
                await Program.RunPreprocessorAsync(pd, stdout, stderr);
                stdout($"***Finish Preprocessor {DateTime.Now.ToString()}");
            }

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

            stdout($"****Start Embedder {DateTime.Now.ToString()}");
            var embedderData = Program.DetermineEmbedderJobs(job);

            await embedderData.ForEachAsync(
                parallelTasks: parallelEmbedderTasks, 
                task: _ => Program.RunEmbedderAsync(_, stdout, stderr));

            // var embedderTasks = embedderData.Select(_ => Program.RunEmbedderAsync(_, stdout, stderr));
            // await Task.WhenAll(embedderTasks);

            stdout($"****Finish Embedder {DateTime.Now.ToString()}");

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
            stdout($"Removing work directory {workFolder}");
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
                stdout($"*** Start PREPROCESSOR1 {DateTime.Now.ToString()}");

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
                    stdout(output.Output);
                }
                else
                {
                    stderr(output.Output);
                    stdout($"***Start PREPROCESSOR2 Error Notification Message {DateTime.Now.ToString()}");
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

                // Delete the input MP4 after MMRK is generated
                if (_.LocalFile.Exists)
                {
                    stdout($"***Deleting {_.LocalFile.FullName} to save space");
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
                stdout($"***Start Download MMRK {_.MmrkFile.FullName} {DateTime.Now.ToString()}");

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
                stdout($"***End Download MMRK {DateTime.Now.ToString()}");

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

            stdout($"***Start embedder task: userID={_.UserID} MMRK={_.MmrkFile}  Date={DateTime.Now.ToString()}");

            var embedderOutput = await Utils.RunProcessAsync(
                prefix: "EMBEDDER",
                fileName: "/usr/bin/NGS_SmartEmbedderCLI",
                arguments: new[] {
                    _.MmrkFile.FullName,
                    _.UserID,
                    _.WatermarkedFile.FullName }
            );

            stdout($"***NGS_SmartEmbeddeWatermarkedFilerCLI finished: userID={_.UserID} MMRK={_.MmrkFile.FullName}  Date={DateTime.Now.ToString()}");

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

            stdout($"***Start upload userID={_.UserID} WatermarkedFile={_.WatermarkedFile.FullName}  Date={DateTime.Now.ToString()}");
            var uploadResult = await _.WatermarkedFile.UploadToAsync(_.WatermarkedURL);
            stdout($"***Finished upload userID={_.UserID} WatermarkedFile={_.WatermarkedFile.FullName} Date={DateTime.Now.ToString()} Success={uploadResult.Success}");
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
                       stdout($"***Try to delete userID={_.UserID} WatermarkedFile={_.WatermarkedFile.FullName} Date={DateTime.Now.ToString()}");
                       _.WatermarkedFile.Delete();
                       stdout($"***Deleted userID={_.UserID} WatermarkedFile={_.WatermarkedFile.FullName} Date={DateTime.Now.ToString()}");
                   }
               });

            #endregion
        }
    }
}