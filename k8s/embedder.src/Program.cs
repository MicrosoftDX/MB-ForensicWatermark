// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace embedder
{
    using Newtonsoft.Json;
    using Polly;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    public class Program
    {
        #region Logging

        public enum Category
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

        public static void stdout(Category category, string message) => print(category, message, Console.Out);
        public static void stderr(Category category, string message) => print(category, message, Console.Error);

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

            var tableConnectionString = Environment.GetEnvironmentVariable("LOGGINGTABLE");
            if (string.IsNullOrEmpty(tableConnectionString))
            {
                stdout(Program.Category.Main, $"Could not configure table logging, missing environment variable 'LOGGINGTABLE'");
            }
            var table = await TableWrapper.CreateAsync(
                connectionString: tableConnectionString, 
                preprocessorTableName: "preprocessor",
                embedderTableName: "embedder");

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
                string json = null;
                if (jobEnvironmentVariable.StartsWith("http"))
                {
                    var ms = new MemoryStream();
                    using (var client = new HttpClient())
                    using (var stream = await client.GetStreamAsync(jobEnvironmentVariable))
                    {
                        await stream.CopyToAsync(ms);
                    }
                    json = Encoding.UTF8.GetString(ms.GetBuffer());
                }
                else
                {
                    json = Encoding.UTF8.GetString(
                        Convert.FromBase64String(jobEnvironmentVariable));
                }

                // cat juan.json | jq -c -M . | base64 --wrap=0 > juan.base64
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

            var preprocessorData = PreprocessorJob.DeterminePreprocessorJobs(job);
            stdout(Category.Main, "Start Preprocessor");
            foreach (var pd in preprocessorData)
            {
                // run these compute-intensive jobs sequentially
                await Program.RunPreprocessorAsync(pd, stdout, stderr, table);
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
            var embedderData = EmbedderJob.DetermineEmbedderJobs(job);

            await embedderData.ForEachAsync(
                parallelTasks: parallelEmbedderTasks, 
                task: _ => Program.RunEmbedderAsync(_, stdout, stderr, table));

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
        
        private static async Task RunPreprocessorAsync(PreprocessorJob job, Action<Category, string> stdout, Action<Category, string> stderr, TableWrapper table)
        {
            ExecutionResult output;

            if (job.RunPreprocessorAndUploadMMRK)
            {
                #region Download MP4

                stdout(Category.DownloadMP4, $"Start Download MP4 {job.Mp4URL.AbsoluteUri}");
                await table.LogPreprocessorAsync(job, "Starting download");

                output = await job.Mp4URL.DownloadToAsync(job.LocalFile);
                if (output.Success)
                {
                    stdout(Category.DownloadMP4, output.Output);
                }
                else
                {
                    stderr(Category.DownloadMP4, output.Output);
                    await table.LogPreprocessorAsync(job, $"Download problem: {output.Output}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                stdout(Category.DownloadMP4, $"Finished Download MP4 {job.Mp4URL.AbsoluteUri}");
                await table.LogPreprocessorAsync(job, $"Finished Download MP4 {job.Mp4URL.AbsoluteUri}");

                #endregion

                #region Pass 1

                //--x264encopts --bframes 2 --b-pyramid none --b-adapt 0 --no-scenecut
                stdout(Category.PreprocessorStep1, $"Start {job.LocalFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Start pass 1 for {job.LocalFile.FullName}");

                output = await Utils.RunProcessAsync(
                    prefix: "PREPROCESSOR1",
                    additionalEnvironment: new Dictionary<string, string> {
                        { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" },
                        { "TMPDIR", Environment.CurrentDirectory }
                    },
                    fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                    arguments: new[] {
                        $"--infile {job.LocalFile.FullName}",
                        $"--stats {job.StatsFile.FullName}",
                        $"--pass 1",
                        $"--vbitrate {(job.VideoBitrate)}",
                        $"--gopsize {job.GOPSize}",
                        "--x264encopts",
                        "--keyint 60",
                        "--min-keyint 60",
                        "--no-scenecut"
                    }
                );

                if (output.Success && job.StatsFile.Exists)
                {
                    stdout(Category.PreprocessorStep1, "SUCCESS");

                    // stdout(Category.PreprocessorStep1, output.Output);
                }
                else
                {
                    stderr(Category.PreprocessorStep1, output.Output);
                    await table.LogPreprocessorAsync(job, $"Problem pass 1 for {job.LocalFile.FullName}: {output.Output}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                stdout(Category.PreprocessorStep1, $"Finished {job.LocalFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Finished pass 1 for {job.LocalFile.FullName}");

                #endregion

                #region Pass 2
                stdout(Category.PreprocessorStep2, $"Start {job.LocalFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Start pass 2 for {job.LocalFile.FullName}");

                output = await Utils.RunProcessAsync(
                   prefix: "PREPROCESSOR2",
                   additionalEnvironment: new Dictionary<string, string> {
                       { "LD_LIBRARY_PATH", "/usr/share/nexguardescreener-preprocessor/" },
                       { "TMPDIR", Environment.CurrentDirectory }
                   },
                   fileName: "/usr/share/nexguardescreener-preprocessor/NGS_Preprocessor",
                   //Original arguments: new[] { $"--infile {_.LocalFile.FullName}", $"--stats {_.StatsFile.FullName}", $"--outfile {_.MmrkFile.FullName}", $"--pass 2", $"--vbitrate {_.VideoBitrate}", $"--gopsize {_.GOPSize}", string.IsNullOrEmpty(_.VideoFilter) ? "" : $"--x264encopts --video-filter {_.VideoFilter}", "--bframes 2", "--b-pyramid none", "--b-adapt 0", "--keyint 60", "--min-keyint 60", "--no-scenecut" }
                   //TEST 2 arguments: new[] { $"--infile {_.LocalFile.FullName}", $"--stats {_.StatsFile.FullName}", $"--outfile {_.MmrkFile.FullName}", $"--pass 2", $"--vbitrate {(_.VideoBitrate/1000)}K", $"--gopsize {_.GOPSize}", "--x264encopts", "--keyint 60", "--min-keyint 60", "--no-scenecut" }
                   //TEST3
                   arguments: new[] {
                        $"--infile {job.LocalFile.FullName}",
                        $"--stats {job.StatsFile.FullName}",
                        $"--outfile {job.MmrkFile.FullName}",
                        $"--pass 2",
                        $"--vbitrate {(job.VideoBitrate)}",
                        $"--gopsize {job.GOPSize}",
                        "--x264encopts", "--keyint 60", "--min-keyint 60", "--no-scenecut"
                   }
                );
                if (output.Success && job.MmrkFile.Exists)
                {
                    stdout(Category.PreprocessorStep2, "SUCCESS");
                    await table.LogPreprocessorAsync(job, $"Finished pass 2 for {job.LocalFile.FullName}");
                    // stdout(Category.PreprocessorStep2, output.Output);
                }
                else
                {
                    stderr(Category.PreprocessorStep2, output.Output);
                    await table.LogPreprocessorAsync(job, $"Problem pass 2 for {job.LocalFile.FullName}: {output.Output}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                stdout(Category.PreprocessorStep2, $"Finished {job.LocalFile.FullName}");

                #endregion

                #region Delete MP4 and statistics files

                // Delete the input MP4 after MMRK is generated
                if (job.LocalFile.Exists)
                {
                    stdout(Category.Main, $"Deleting {job.LocalFile.FullName}");
                    job.LocalFile.Delete();
                }
                if (job.StatsFile.Exists)
                {
                    job.StatsFile.Delete();
                }
                if (File.Exists($"{job.StatsFile.FullName}.mbtree"))
                {
                    File.Delete($"{job.StatsFile.FullName}.mbtree");
                }

                #endregion

                #region Upload MMRK
                stdout(Category.UploadMMRK, $"Start Upload MMRK {job.MmrkFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Start Upload MMRK {job.MmrkFile.FullName}");

                output = await job.MmrkFile.UploadToAsync(job.MmmrkURL);
                if (output.Success)
                {
                    stdout(Category.UploadMMRK, output.Output);
                    await table.LogPreprocessorAsync(job, $"Finished upload MMRK {job.MmrkFile.FullName}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                    await table.LogPreprocessorAsync(job, $"Problem upload MMRK {job.MmrkFile.FullName} {output.Output}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationPreprocessor
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                stdout(Category.UploadMMRK, $"End Upload MMRK {job.MmrkFile.FullName}");

                #endregion
            }
            else
            {
                #region Download MMRK
                stdout(Category.DownloadMMRK, $"Start Download MMRK {job.MmrkFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Start Download MMRK {job.MmrkFile.FullName}");

                output = await job.MmmrkURL.DownloadToAsync(job.MmrkFile);
                if (output.Success)
                {
                    stdout(Category.DownloadMMRK, output.Output);
                }
                else
                {
                    stderr(Category.DownloadMMRK, output.Output);
                    await table.LogPreprocessorAsync(job, $"Problem downloading MMRK {job.MmrkFile.FullName}: {output.Output}");
                    var queueOutput = await job.Queue.DispatchMessage(new NotificationEmbedder
                    {
                        AssetID = job.Job.AssetID,
                        JobID = job.Job.JobID,
                        FileName = job.Name,
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
                stdout(Category.DownloadMMRK, $"Finished Download MMRK {job.MmrkFile.FullName}");
                await table.LogPreprocessorAsync(job, $"Finished Download MMRK {job.MmrkFile.FullName}");

                #endregion
            }
        }

        private static async Task RunEmbedderAsync(EmbedderJob job, Action<Category, string> stdout, Action<Category, string> stderr, TableWrapper table)
        {
            #region Embedder

            stdout(Category.Embedder, $"Start embed {job.UserID} into {job.WatermarkedFile.FullName}");
            await table.LogEmbedderAsync(job, $"Start embed {job.UserID} into {job.WatermarkedFile.FullName}");

            var embedderOutput = await Utils.RunProcessAsync(
                prefix: "EMBEDDER",
                fileName: "/usr/bin/NGS_SmartEmbedderCLI",
                additionalEnvironment: new Dictionary<string, string> {
                    { "TMPDIR", Environment.CurrentDirectory }
                },
                arguments: new[] {
                    job.MmrkFile.FullName,
                    job.UserID,
                    job.WatermarkedFile.FullName }
            );


            if (embedderOutput.Success)
            {
                stdout(Category.Embedder, $"Finished embed {job.UserID} into {job.WatermarkedFile.FullName}");
                await table.LogEmbedderAsync(job, $"Finished embed {job.UserID} into {job.WatermarkedFile.FullName}");
                // stdout(Category.Embedder, embedderOutput.Output);
            }
            else
            {
                stderr(Category.Embedder, embedderOutput.Output);
                await table.LogEmbedderAsync(job, $"Problem embed {job.UserID} into {job.WatermarkedFile.FullName}: {embedderOutput.Output}");
                var queueOutput = await job.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = job.Job.AssetID,
                    JobID = job.Job.JobID,
                    FileName = job.Name,
                    UserID = job.UserID,
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

            stdout(Category.UploadWatermarked, $"Start upload {job.UserID} {job.WatermarkedFile.FullName}");
            await table.LogEmbedderAsync(job, $"Start upload {job.UserID} {job.WatermarkedFile.FullName}");

            var uploadResult = await job.WatermarkedFile.UploadToAsync(job.WatermarkedURL);
            if (uploadResult.Success)
            {
                stdout(Category.UploadWatermarked, $"Finished upload {job.UserID} {job.WatermarkedFile.FullName}");
                await table.LogEmbedderAsync(job, $"Finished upload {job.UserID} {job.WatermarkedFile.FullName}");
                var queueOutput = await job.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = job.Job.AssetID,
                    JobID = job.Job.JobID,
                    FileName = job.Name,
                    UserID = job.UserID,
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
                await table.LogEmbedderAsync(job, $"Problem uploading {job.UserID} / {job.WatermarkedFile.FullName}: {uploadResult.Output}");
                var queueOutput = await job.Queue.DispatchMessage(new NotificationEmbedder
                {
                    AssetID = job.Job.AssetID,
                    JobID = job.Job.JobID,
                    FileName = job.Name,
                    UserID = job.UserID,
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
                   if (job.WatermarkedFile.Exists)
                   {
                       stdout(Category.Main, $"Delete {job.WatermarkedFile.FullName}");
                       job.WatermarkedFile.Delete();
                   }
               });

            #endregion
        }
    }
}