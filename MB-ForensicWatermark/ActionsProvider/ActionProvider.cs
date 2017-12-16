// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using ActionsProvider.UnifiedResponse;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Queue;
using System.Diagnostics;
using ActionsProvider.K8S;

namespace ActionsProvider
{
    class ReferenceNames
    {
        public static string WaterMarkedRender { get { return "WaterMarkedRender"; } }
        public static string ProcessStatus { get { return "processStatus"; } }
        public static string WaterMarkedAssetInfo { get { return "WaterMarkedAssetInfo"; } }
    }
    class ActionProvider : IActionsProvider
    {
        CloudStorageAccount storageAccount;
        CloudTableClient tableClient;
        CloudTable _ProcessStatusTable;
        CloudTable _MMRKSttausTable;

        CloudTable _AssetStatus;
        CloudTable _WaterMarkedAssetInfo;


        public ActionProvider(string strConn)
        {
            storageAccount = CloudStorageAccount.Parse(strConn);
            tableClient = storageAccount.CreateCloudTableClient();
            _ProcessStatusTable = tableClient.GetTableReference(ReferenceNames.ProcessStatus);
            _MMRKSttausTable = tableClient.GetTableReference("mmrkStatus");
            _AssetStatus = tableClient.GetTableReference("AssetStatus");
            _WaterMarkedAssetInfo = tableClient.GetTableReference(ReferenceNames.WaterMarkedAssetInfo);
            _ProcessStatusTable.CreateIfNotExists();
            _MMRKSttausTable.CreateIfNotExists();
            _AssetStatus.CreateIfNotExists();
            _WaterMarkedAssetInfo.CreateIfNotExists();
        }

        public UnifiedResponse.WaterMarkedRender UpdateWaterMarkedRender(UnifiedResponse.WaterMarkedRender renderData)
        {
            UnifiedResponse.TWaterMarkedRender myStatus = new UnifiedResponse.TWaterMarkedRender(renderData);
            var myTable = tableClient.GetTableReference("WaterMarkedRender");
            myTable.CreateIfNotExists();
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(myStatus);
            myTable.Execute(InsertOrReplace);
            return renderData;
        }
        public List<UnifiedResponse.WaterMarkedRender> GetWaterMarkedRenders(string ParentAssetId, string EmbeddedCodeValue)
        {
            List<UnifiedResponse.WaterMarkedRender> myList = new List<UnifiedResponse.WaterMarkedRender>();
            var wmrTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
            wmrTable.CreateIfNotExists();

            TableQuery<UnifiedResponse.TWaterMarkedRender> query =
                new TableQuery<UnifiedResponse.TWaterMarkedRender>().Where(TableQuery.GenerateFilterCondition(
                    "PartitionKey", QueryComparisons.Equal, $"{ParentAssetId}-{EmbeddedCodeValue}"));

            var wmrTList = wmrTable.ExecuteQuery(query);

            foreach (var item in wmrTList)
            {
                myList.Add(item.GetWaterMarkedRender());
            }


            return myList;
        }
        public UnifiedResponse.WaterMarkedRender GetWaterMarkedRender(string ParentAssetId, string EmbeddedCodeValue, string RenderName)
        {
            UnifiedResponse.WaterMarkedRender x = null;
            UnifiedResponse.WaterMarkedAssetInfo wai = new UnifiedResponse.WaterMarkedAssetInfo()
            {
                AssetId = ParentAssetId,

            };
            var myTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
            myTable.CreateIfNotExists();
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TWaterMarkedRender>($"{ParentAssetId}-{EmbeddedCodeValue}", RenderName);
            TableResult retrievedResult = myTable.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                x = ((UnifiedResponse.TWaterMarkedRender)retrievedResult.Result).GetWaterMarkedRender();
            }

            return x;
        }
        public async Task<int> EvalPEmbeddedNotifications()
        {
            int nNotification = 0;
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("embeddernotification");

            foreach (CloudQueueMessage message in await queue.GetMessagesAsync(20, TimeSpan.FromMinutes(60), null, null))
            {
                try
                {
                    NotificationEmbedder rawdata = Newtonsoft.Json.JsonConvert.DeserializeObject<NotificationEmbedder>(message.AsString);
                    WaterMarkedRender data = GetWaterMarkedRender(rawdata.AssetId, rawdata.EmbeddedCode, rawdata.FileName);
                    string url = data.MP4URL;
                    data = new WaterMarkedRender(rawdata, url);
                    var outputData = UpdateWaterMarkedRender(data);
                }
                catch (Exception X)
                {
                    Trace.TraceError($"EvalPEmbeddedNotifications Error: {X.Message}");
                    CloudQueue deadletter = queueClient.GetQueueReference("deadletter");
                    await deadletter.CreateIfNotExistsAsync();
                    await deadletter.AddMessageAsync(message);
                }
                await queue.DeleteMessageAsync(message);
                nNotification += 1;
            }
            return nNotification;
        }

        #region Asset Info
        public UnifiedResponse.AssetStatus GetAssetStatus(string AssetId)
        {
            UnifiedResponse.AssetStatus r = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TAssetStatus>("Flags", AssetId);
            TableResult retrievedResult = _AssetStatus.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                r = ((UnifiedResponse.TAssetStatus)retrievedResult.Result).GetAssetStatus();
            }
            return r;
        }
        public UnifiedResponse.AssetStatus EvalAssetStatus(string AssetId)
        {
            var assetStatus = GetAssetStatus(AssetId);
            //MMRK renders list
            TableQuery<TMMRKStatus> query = new TableQuery<TMMRKStatus>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, AssetId));
            var mmrkList = _MMRKSttausTable.ExecuteQuery(query);
            if (mmrkList != null)
            {
                if (mmrkList.Where(m => m.State == ExecutionStatus.Error.ToString()).Count() > 0)
                {
                    //Error
                    assetStatus.State = ExecutionStatus.Error;
                    //TODO: error all process
                }
                else
                {
                    //Not Error
                    int finishcount = mmrkList.Where(m => m.State == ExecutionStatus.Finished.ToString()).Count();
                    if (finishcount == mmrkList.Count())
                    {
                        //Finish
                        assetStatus.State = ExecutionStatus.Finished;
                    }
                    else
                    {
                        //Update MMRK Satus

                    }
                }
            }
            UpdateAssetStatus(assetStatus);
            return assetStatus;
        }
        private void UpdateAssetStatus(UnifiedResponse.AssetStatus theAsset)
        {
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(new UnifiedResponse.TAssetStatus(theAsset));
            _AssetStatus.Execute(InsertOrReplace);
        }
        private bool AssetIsRunningAnyProcess(string AssetId)
        {
            bool R = false;
            TableQuery<TJobStatus> query = new TableQuery<TJobStatus>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, AssetId));
            foreach (TJobStatus processInstance in _ProcessStatusTable.ExecuteQuery(query))
            {
                if (processInstance.State == "Running")
                {
                    //Asset has a 
                    Trace.TraceInformation($"Asset has process {processInstance.RowKey} {processInstance.State} >> {processInstance.PartitionKey}");
                    R = true;
                    break;
                }
            }
            return R;
        }
        #endregion

        #region Process Information

        public void UpdateUnifiedProcessStatus(UnifiedResponse.UnifiedProcessStatus curretnData)
        {
            //Update Asset
            var asset = curretnData.AssetStatus;
            UpdateAssetStatus(asset);
            //Update JobInformation
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(new UnifiedResponse.TJobStatus(curretnData.JobStatus, curretnData.AssetStatus.AssetId));
            _ProcessStatusTable.Execute(InsertOrReplace);
            //Update all Enbebed
            foreach (var data in curretnData.EmbeddedCodesList)
            {
                UpdateWaterMarkedAssetInfo(data, curretnData.AssetStatus.AssetId);
            }

        }
        private UnifiedResponse.JobStatus GetJobStatus(string AssetId, string JobId)
        {
            UnifiedResponse.JobStatus data = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TJobStatus>(AssetId, JobId);
            TableResult retrievedResult = _ProcessStatusTable.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                data = ((UnifiedResponse.TJobStatus)retrievedResult.Result).GetJobStatus();
            }
            return data;
        }
        public UnifiedResponse.UnifiedProcessStatus GetUnifiedProcessStatus(string AssetId, string JobId)
        {
            UnifiedResponse.UnifiedProcessStatus Manifest = new UnifiedResponse.UnifiedProcessStatus
            {
                AssetStatus = GetAssetStatus(AssetId),
                JobStatus = GetJobStatus(AssetId, JobId),
                EmbeddedCodesList = new List<UnifiedResponse.WaterMarkedAssetInfo>()

            };
            foreach (var ecode in Manifest.JobStatus.EmbeddedCodeList)
            {
                Manifest.EmbeddedCodesList.Add(GetWaterMarkedAssetInfo(AssetId, ecode));
            }
            return Manifest;
        }
        public UnifiedResponse.UnifiedProcessStatus StartNewProcess(string AssetId, string JobId, string[] EmbeddedCodeList)
        {
            //NEW Process
            UnifiedResponse.UnifiedProcessStatus newProcess = new UnifiedResponse.UnifiedProcessStatus
            {
                EmbeddedCodesList = new List<UnifiedResponse.WaterMarkedAssetInfo>(),
                //2. JobStatus
                JobStatus = new UnifiedResponse.JobStatus()
                {
                    Details = "Queue",
                    Duration = null,
                    FinishTime = null,
                    JobId = JobId,
                    StartTime = DateTime.Now,
                    State = ExecutionStatus.Running,
                    EmbeddedCodeList = EmbeddedCodeList
                },

                AssetStatus = GetAssetStatus(AssetId) ?? new UnifiedResponse.AssetStatus() { AssetId = AssetId, State = ExecutionStatus.New }
            };

            //Status
            ExecutionStatus EmbeddedStatus = ExecutionStatus.New;

            switch (newProcess.AssetStatus.State)
            {
                case ExecutionStatus.Error:
                    newProcess.JobStatus.State = ExecutionStatus.Error;
                    newProcess.JobStatus.Details = "MMRK Files Generation Error";
                    newProcess.JobStatus.FinishTime = DateTime.Now;
                    newProcess.JobStatus.Duration = DateTime.Now.Subtract(newProcess.JobStatus.StartTime);
                    EmbeddedStatus = ExecutionStatus.Aborted;

                    break;
                case ExecutionStatus.Running:
                    newProcess.JobStatus.Details = "MMRK Files Generation Runnig";
                    newProcess.JobStatus.State = ExecutionStatus.Error;
                    newProcess.JobStatus.FinishTime = DateTime.Now;
                    newProcess.JobStatus.Duration = DateTime.Now.Subtract(newProcess.JobStatus.StartTime);
                    EmbeddedStatus = ExecutionStatus.Aborted;

                    break;

                case ExecutionStatus.New:
                case ExecutionStatus.Finished:
                    
                    if (newProcess.AssetStatus.State == ExecutionStatus.New)
                    {
                        //New Asset
                        //Asset has not oter process running
                        newProcess.AssetStatus.State = ExecutionStatus.Running;
                        EmbeddedStatus = ExecutionStatus.Running;
                        newProcess.JobStatus.State = ExecutionStatus.Running;
                    }
                    else
                    {
                        //Asset  Ready
                        //Is another process running for this Asset??
                        if (AssetIsRunningAnyProcess(AssetId))
                        {
                            //Already Running a process
                            newProcess.JobStatus.Details = "Asset ready but another process is running on asset";
                            newProcess.JobStatus.State = ExecutionStatus.Error;
                            newProcess.JobStatus.FinishTime = DateTime.Now;
                            newProcess.JobStatus.Duration = DateTime.Now.Subtract(newProcess.JobStatus.StartTime);
                            EmbeddedStatus = ExecutionStatus.Aborted;
                        }
                        else
                        {
                            //Asset has not other process running
                            EmbeddedStatus = ExecutionStatus.Running;
                            newProcess.JobStatus.State = ExecutionStatus.Running;
                        }
                    }
                    break;
                default:
                    break;
            }
            //Embeddedecodes
            foreach (var code in EmbeddedCodeList)
            {
                newProcess.EmbeddedCodesList.Add(
                    new UnifiedResponse.WaterMarkedAssetInfo()
                    {
                        ParentAssetId = AssetId,
                        State = EmbeddedStatus,
                        EmbeddedCodeValue = code,
                        AssetId = "",
                        Details = "Just Start"
                    });
            }

            UpdateUnifiedProcessStatus(newProcess);


            return newProcess;
        }
        public UnifiedProcessStatus UpdateJob(UnifiedResponse.UnifiedProcessStatus processState, ExecutionStatus AssetState, ExecutionStatus JobState, string JobStateDetails, ExecutionStatus watermarkState, string WaterMarkCopiesStatusDetails)
        {
            processState.AssetStatus.State = AssetState;

            processState.JobStatus.State = JobState;
            processState.JobStatus.Details = JobStateDetails;
            switch (processState.JobStatus.State)
            {
                case ExecutionStatus.Finished:
                case ExecutionStatus.Error:
                case ExecutionStatus.Aborted:
                    processState.JobStatus.FinishTime = DateTime.Now;
                    processState.JobStatus.Duration = DateTime.Now.Subtract(processState.JobStatus.StartTime);
                    break;

            }
            foreach (var watermaekAssetInfo in processState.EmbeddedCodesList)
            {
                watermaekAssetInfo.State = watermarkState;
                watermaekAssetInfo.Details = WaterMarkCopiesStatusDetails;
            }

            UpdateUnifiedProcessStatus(processState);

            return processState;
        }
        #endregion

        #region MMRK files
        public MMRKStatus GetMMRKStatus(string AsssetId, string JobRender)
        {
            MMRKStatus myData = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<TMMRKStatus>(AsssetId, JobRender);
            TableResult retrievedResult = _MMRKSttausTable.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                var Status = ((TMMRKStatus)retrievedResult.Result);
                myData = Status.GetMMRKStatus();
            }
            return myData;
        }
        public List<MMRKStatus> GetMMRKStatusList(string AssetId)
        {
            List<MMRKStatus> ret = new List<MMRKStatus>();
            TableQuery<TMMRKStatus> query = new TableQuery<TMMRKStatus>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, AssetId));
            var mmrkList = _MMRKSttausTable.ExecuteQuery(query);
            foreach (var item in mmrkList)
            {
                ret.Add(item.GetMMRKStatus());

            }
            return ret;
        }
        private UnifiedResponse.WaterMarkedAssetInfo UpdateWaterMarkedAssetInfo(UnifiedResponse.WaterMarkedAssetInfo data, string ParentAssetId)
        {
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(new UnifiedResponse.TWaterMarkedAssetInfo(data, ParentAssetId));
            _WaterMarkedAssetInfo.Execute(InsertOrReplace);
            return data;
        }
        private UnifiedResponse.WaterMarkedAssetInfo GetWaterMarkedAssetInfo(string AssetId, string EmbeddedCodeValue)
        {
            UnifiedResponse.WaterMarkedAssetInfo z = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TWaterMarkedAssetInfo>(AssetId, EmbeddedCodeValue);
            TableResult retrievedResult = _WaterMarkedAssetInfo.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                z = ((UnifiedResponse.TWaterMarkedAssetInfo)retrievedResult.Result).GetWaterMarkedAsssetInfo();
            }

            return z;
        }
        public UnifiedResponse.WaterMarkedAssetInfo EvalWaterMarkedAssetInfo(string ParentAssetId, string EmbeddedCodeValue)
        {
            UnifiedResponse.WaterMarkedAssetInfo currentWaterMarkInfo = GetWaterMarkedAssetInfo(ParentAssetId, EmbeddedCodeValue);

            if (currentWaterMarkInfo.State == ExecutionStatus.Running)
            {
                var wmrTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
                wmrTable.CreateIfNotExists();

                TableQuery<UnifiedResponse.TWaterMarkedRender> query =
                    new TableQuery<UnifiedResponse.TWaterMarkedRender>().Where(TableQuery.GenerateFilterCondition(
                        "PartitionKey", QueryComparisons.Equal, $"{ParentAssetId}-{EmbeddedCodeValue}"));

                var wmrList = wmrTable.ExecuteQuery(query);
                if (wmrList != null)
                {
                    if (wmrList.Where(m => m.State == ExecutionStatus.Error.ToString()).Count() > 0)
                    {
                        //Error
                        currentWaterMarkInfo.State = ExecutionStatus.Error;
                        //Update EmbeddedCode
                        currentWaterMarkInfo.State = ExecutionStatus.Error;
                        currentWaterMarkInfo.Details = $"Render with errors";
                    }
                    else
                    {
                        //Not Error
                        int finishcount = wmrList.Where(m => m.State == ExecutionStatus.Finished.ToString()).Count();
                        if (finishcount == wmrList.Count())
                        {
                            //Finish
                            currentWaterMarkInfo.State = ExecutionStatus.Finished;

                        }
                        currentWaterMarkInfo.Details = $"Ready {finishcount} of {wmrList.Count()}";
                    }
                }
                UpdateWaterMarkedAssetInfo(currentWaterMarkInfo, ParentAssetId);
            }

            return currentWaterMarkInfo;
        }
        public MMRKStatus UpdateMMRKStatus(MMRKStatus mmrkStatus)
        {
            //Update MMRK Status
            TMMRKStatus myStatus = new TMMRKStatus(mmrkStatus);
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(myStatus);
            _MMRKSttausTable.Execute(InsertOrReplace);
            return mmrkStatus;
        }
        public async Task<int> EvalPreprocessorNotifications()
        {
            int nNotification = 0;
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("preprocessorout");

            foreach (CloudQueueMessage message in await queue.GetMessagesAsync(20, TimeSpan.FromMinutes(60), null, null))
            {
                try
                {
                    var jNotification = Newtonsoft.Json.Linq.JObject.Parse(message.AsString);
                    // Retrive 
                    string jobRender = $"[{(string)jNotification["JobId"]}]{(string)jNotification["FileName"]}";
                    var MMRK = GetMMRKStatus((string)jNotification["AssetId"], jobRender);
                    //Update MMRK Status
                    MMRK.Details = (string)jNotification["JobOutput"];
                    MMRK.State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), (string)jNotification["Status"]);
                    UpdateMMRKStatus(MMRK);
                }
                catch (Exception X)
                {
                    Trace.TraceError($"EvalPreprocessorNotifications Error: {X.Message}");
                    CloudQueue deadletter = queueClient.GetQueueReference("deadletter");
                    await deadletter.CreateIfNotExistsAsync();
                    await deadletter.AddMessageAsync(message);
                }
                await queue.DeleteMessageAsync(message);
                nNotification += 1;
            }
            return nNotification;
        }

        #endregion
        #region K8S JOBS
        private string GetJobYmal(string JobId, string JOBBASE64, string imagename)
        {
            string path;
            if (Environment.GetEnvironmentVariable("HOME") != null)
            {
                path = Environment.GetEnvironmentVariable("HOME") + @"\site\wwwroot" + @"\Files\jobBase.txt";
            }
            else
            {
                path = @".\Files\jobBase.txt";
            }
            string ymal = System.IO.File.ReadAllText(path);

            ymal = ymal.Replace("[JOBNAME]", "allinone-job-" + JobId);

            ymal = ymal.Replace("[IMAGENAME]", imagename);

            return ymal.Replace("[JOBBASE64]", JOBBASE64);
        }
        public async Task<K8SResult> SubmiteJobK8S(ManifestInfo manifest, int subId)
        {
            //Create Yamal Job definition
            string manifesttxt = Newtonsoft.Json.JsonConvert.SerializeObject(manifest);
            string jobbase64 = System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manifesttxt), Base64FormattingOptions.None);
            string imageName = System.Configuration.ConfigurationManager.AppSettings["imageName"];
            string jobtxt = GetJobYmal(manifest.JobId + "-" + subId.ToString(), jobbase64, imageName);
            HttpContent ymal = new StringContent(jobtxt, Encoding.UTF8, "application/yaml");

            // Submite JOB
            IK8SClient k8sClient = K8SClientFactory.Create();
            var rs = await k8sClient.SubmiteK8SJob(ymal);
            return rs;

        }
        private ManifestInfo GetManifestInfo(int skip, int take, ManifestInfo manifest)
        {
            ManifestInfo aggregateJobManifest = new ManifestInfo()
            {
                AssetId = manifest.AssetId,
                EmbedderNotificationQueue = manifest.EmbedderNotificationQueue,
                EmbeddedCodes = new List<EmbeddedCode>(),
                JobId = manifest.JobId,
                PreprocessorNotificationQueue = manifest.PreprocessorNotificationQueue,
                VideoInformation = new List<VideoInformation>()
            };
            //Add aggregated video info nodes
            aggregateJobManifest.VideoInformation.AddRange(manifest.VideoInformation.Skip(skip).Take(take));
            foreach (var emc in manifest.EmbeddedCodes)
            {
                EmbeddedCode jobemc = new EmbeddedCode()
                {
                    Code = emc.Code,
                    MP4WatermarkedURL = new List<MP4WatermarkedURL>()
                };
                foreach (var vi in aggregateJobManifest.VideoInformation)
                {
                    MP4WatermarkedURL myMMP4WatermarkeInfo = emc.MP4WatermarkedURL.Where(x => x.FileName == vi.FileName).FirstOrDefault();
                    //add to the list
                    jobemc.MP4WatermarkedURL.Add(myMMP4WatermarkeInfo);
                }
                aggregateJobManifest.EmbeddedCodes.Add(jobemc);
            }
            return aggregateJobManifest;

        }
        /// <summary>
        /// Generate watermark Job Json list
        /// </summary>
        /// <param name="aggregationLevel"># of render to agregate on Job with pre step (mmrk) to execute</param>
        /// <param name="manifest">Process manifest</param>
        /// <returns></returns>
        public List<ManifestInfo> GetK8SManifestInfo(int aggregationLevel, int aggregationLevelOnlyEmb, ManifestInfo manifest)
        {
            int renders = manifest.VideoInformation.Count();


            //If MMRK are ready, no agregation
            if (manifest.VideoInformation.FirstOrDefault().MP4URL == "")
            {
                //This Asset already have MMRK ready, son We will use other aggregation level
                aggregationLevel = aggregationLevelOnlyEmb;
            }

            if (aggregationLevel > renders)
            {
                throw new Exception($"Total renders are {renders} can't aggregate {aggregationLevel} level");
            }
            List<ManifestInfo> myManifestJob = new List<ManifestInfo>();

            myManifestJob.Add(GetManifestInfo(0, aggregationLevel, manifest));

            for (int i = aggregationLevel; i < renders; i++)
            {
                myManifestJob.Add(GetManifestInfo(i, 1, manifest));
            }

            return myManifestJob;
        }


        #endregion
    }

}
