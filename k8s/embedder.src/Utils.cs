// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// Author: chgeuer@microsoft.com github.com/chgeuer

namespace embedder
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure.Storage.Blob;
    using Microsoft.WindowsAzure.Storage.Queue;
    using Newtonsoft.Json;
 
    public static class Utils
    {
        public class ExecutionResult
        {
            public bool Success{ get; set; }

            public string Output { get; set; }
        }

        private static Task<ExecutionResult> RunProcessAsync(ProcessStartInfo processStartInfo, string prefix)
        {
            var output = new List<string>();

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.RedirectStandardError = true;
            process.OutputDataReceived += (sender, data) => { output.Add($"{prefix}: {data.Data}"); };
            process.ErrorDataReceived += (sender, data) => { output.Add($"{prefix} ERR: {data.Data}"); };

            Func<Task<ExecutionResult>> RunAsync = () =>
            {
                var tcs = new TaskCompletionSource<ExecutionResult>();
                process.Exited += (sender, args) =>
                {
                    var o = string.Join("\n", output.ToArray());
                    tcs.SetResult(new ExecutionResult { Success = process.ExitCode == 0, Output = o });
                    process.Dispose();
                };

                process.Start();
                if (process.StartInfo.RedirectStandardOutput) process.BeginOutputReadLine();
                if (process.StartInfo.RedirectStandardError) process.BeginErrorReadLine();

                return tcs.Task;
            };

            return RunAsync();
        }

        public static async Task<ExecutionResult> RunProcessAsync(
             string fileName, string[] arguments = null,
             IDictionary<string, string> additionalEnvironment = null, 
             string prefix = "")
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments != null ? string.Join(" ", arguments) : null,
                // UseShellExecute = false,
                // CreateNoWindow = true,
                WorkingDirectory = "."
            };
            if (additionalEnvironment != null)
            {
                foreach (var k in additionalEnvironment.Keys)
                {
                    processStartInfo.Environment.Add(k, additionalEnvironment[k]);
                }
            }

            try
            {
                return await RunProcessAsync(processStartInfo, prefix);
            }
            catch (Exception ex)
            {
                return new ExecutionResult
                {
                    Success = false,
                    Output = $"{prefix}: Exception {ex.Message}"
                };
            }
        }

        public static FileInfo AsSafeFileName(this string x)
        {
            var unsafeChars = new List<char>(Path.GetInvalidFileNameChars());
            unsafeChars.AddRange(new[] { '%', ' ' });
            foreach (var c in unsafeChars) { x = x.Replace(c, '_'); }
            return new FileInfo(x);
        }

        public static FileInfo AsLocalFile(this string filename) { return (filename).AsSafeFileName(); }
        public static FileInfo AsStatsFile(this string filename) { return filename.Replace(".mp4", ".stats").AsSafeFileName(); }
        public static FileInfo AsMmrkFile(this string filename) { return filename.Replace(".mp4", ".mmrk").AsSafeFileName(); }
        public static FileInfo AsWatermarkFileForUser(this string filename, string userid) { return (filename.Replace(".mp4", $"-{userid}.mp4")).AsSafeFileName(); }

        public static Uri AsUri(this string uri) { return string.IsNullOrEmpty(uri) ? null : new Uri(uri); }

        public static async Task<ExecutionResult> DispatchMessage(this CloudQueue queue, INotificationMessage message)
        {
            var prefix = "QUEUE";
            if (queue == null)
            {
                return new ExecutionResult { Success = false, Output = $"{prefix}: ERR queue is null" };
            }
            try
            {
                await queue.AddMessageAsync(new CloudQueueMessage(JsonConvert.SerializeObject(message)));

                return new ExecutionResult { Success = true, Output = $"{prefix}: Sent message to {queue.Uri.AbsoluteUri}" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Output = $"{prefix}: ERR {ex.Message} {queue.Uri.AbsoluteUri}" };
            }
        }

        public static async Task<ExecutionResult> DownloadToAsync(this Uri blobAbsoluteUri, FileInfo file, string prefix = "")
        {
            try
            {
                //var blockBlob = new CloudBlockBlob(blobAbsoluteUri);
                //return blockBlob.DownloadToFileAsync(path: file.FullName, mode: System.IO.FileMode.Create);

                using (var client = new HttpClient())
                using (var stream = await client.GetStreamAsync(blobAbsoluteUri))
                using (var output = file.OpenWrite())
                {
                    await stream.CopyToAsync(output);
                }

                return new ExecutionResult { Success = true, Output = $"{prefix}: Downloaded {blobAbsoluteUri.AbsoluteUri} to {file.FullName}" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Output = $"{prefix}: ERR during download: \"{ex.Message}\" {blobAbsoluteUri.AbsoluteUri}" };
            }
        }

        public static async Task<ExecutionResult> UploadToAsync(this FileInfo file, Uri blobAbsoluteUri, string prefix = "")
        {
            try
            {
                var blockBlob = new CloudBlockBlob(blobAbsoluteUri);

                await LargeFileUploaderUtils.UploadAsync(file: file, blockBlob: blockBlob, uploadParallelism: 10);
                // await blockBlob.UploadFromFileAsync(file.FullName);

                return new ExecutionResult { Success = true, Output = $"{prefix}: Uploaded {file.FullName} to {blobAbsoluteUri.AbsoluteUri}" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Output = $"{prefix}: ERR during upload: \"{ex.Message}\" {blobAbsoluteUri.AbsoluteUri}" };
            }
        }
    }
}