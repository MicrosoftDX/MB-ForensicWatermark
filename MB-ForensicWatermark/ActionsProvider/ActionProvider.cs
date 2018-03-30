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
using Microsoft.WindowsAzure.Storage.Blob;

namespace ActionsProvider
{
    class ReferenceNames
    {
        public static string WaterMarkedRender { get { return "WaterMarkedRender"; } }
        public static string ProcessStatus { get { return "processStatus"; } }
        public static string WaterMarkedAssetInfo { get { return "WaterMarkedAssetInfo"; } }
        public static string AssetProcessLock { get { return "AssetProcessLock"; } }
    }
    class ActionProvider : IActionsProvider
    {
        CloudStorageAccount storageAccount;
        CloudTableClient tableClient;
        CloudTable _ProcessStatusTable;
        CloudTable _MMRKSttausTable;

        CloudTable _AssetStatus;
        CloudTable _WaterMarkedAssetInfo;

        int _embeddedmessagecount = 10;
        public ActionProvider(string strConn, int embeddedmessagecount)
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

            _embeddedmessagecount = embeddedmessagecount;
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
        public List<UnifiedResponse.WaterMarkedRender> GetWaterMarkedRenders(string ParentAssetID, string EmbebedCodeValue)
        {
            List<UnifiedResponse.WaterMarkedRender> myList = new List<UnifiedResponse.WaterMarkedRender>();
            var wmrTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
            wmrTable.CreateIfNotExists();

            TableQuery<UnifiedResponse.TWaterMarkedRender> query =
                new TableQuery<UnifiedResponse.TWaterMarkedRender>().Where(TableQuery.GenerateFilterCondition(
                    "PartitionKey", QueryComparisons.Equal, $"{ParentAssetID}-{EmbebedCodeValue}"));

            var wmrTList = wmrTable.ExecuteQuery(query);

            foreach (var item in wmrTList)
            {
                myList.Add(item.GetWaterMarkedRender());
            }


            return myList;
        }
        public UnifiedResponse.WaterMarkedRender GetWaterMarkedRender(string ParentAssetID, string EmbebedCodeValue, string RenderName)
        {
            UnifiedResponse.WaterMarkedRender x = null;
            UnifiedResponse.WaterMarkedAssetInfo wai = new UnifiedResponse.WaterMarkedAssetInfo()
            {
                AssetID = ParentAssetID,

            };
            var myTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
            myTable.CreateIfNotExists();
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TWaterMarkedRender>($"{ParentAssetID}-{EmbebedCodeValue}", RenderName);
            TableResult retrievedResult = myTable.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                x = ((UnifiedResponse.TWaterMarkedRender)retrievedResult.Result).GetWaterMarkedRender();
            }

            return x;
        }

        public async Task<int> DeleteWatermarkedRenderTmpInfo(List<UnifiedResponse.WaterMarkedRender> WatermarkedRenders)
        {
            int deleted = 0;
            var wmrTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
            wmrTable.CreateIfNotExists();
            try
            {
                TableBatchOperation batchOperation = new TableBatchOperation();
                foreach (var currentRender in WatermarkedRenders)
                {
                    var tRender = new TWaterMarkedRender(currentRender);
                    tRender.ETag = "*";
                    batchOperation.Delete(tRender);
                    deleted += 1;
                }
                var X = await wmrTable.ExecuteBatchAsync(batchOperation);
            }
            catch (Exception X)
            {
                Trace.TraceWarning($"[] DeleteWatermarkedRenderTmpInfo Error {X.Message}");               
            }
            return deleted;
        }

        public async Task<int> EvalPEmbeddedNotifications(string JobId)
        {
            int nNotification = 0;
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("embeddernotification");

            //Process more than 32 message
            for (int i = 0; i < _embeddedmessagecount; i++)
            {
                bool swMetaLoop = true;
                foreach (CloudQueueMessage message in await queue.GetMessagesAsync(_embeddedmessagecount, TimeSpan.FromMinutes(10), null, null))
                {
                    swMetaLoop = false;
                    //Trace.TraceInformation($"[{JobId}] {message.AsString}");
                    try
                    {
                        NotificationEmbedder rawdata = Newtonsoft.Json.JsonConvert.DeserializeObject<NotificationEmbedder>(message.AsString);
                        rawdata.JobID = rawdata.JobID.Split('-')[0];
                        Trace.TraceInformation($"JobId={JobId} and messageJobId={rawdata.JobID}");
                        // This message if for JOB
                        WaterMarkedRender data = GetWaterMarkedRender(rawdata.AssetID, rawdata.EmbebedCode, rawdata.FileName);
                        string url = data.MP4URL;
                        //Adding Idempotent control for exceptional case of recieve multiple time 
                        //notifications from K8S container (fail on container)
                        if (data.State == ExecutionStatus.Running)
                        {
                            //WatermarkRender is on right state(first message)
                            data = new WaterMarkedRender(rawdata, url);
                            var outputData = UpdateWaterMarkedRender(data);
                            Trace.TraceInformation($"{JobId} processesd Message {rawdata.Status} ");
                        }
                        else
                        {
                            //poison message, means Embbedecode was already porcessed
                            Trace.TraceInformation($"{rawdata.JobID} Idenpotent Message Control  {message.AsString}");
                            await SendToDeadLetterQueue(queueClient, message.AsString);
                        }
                    }
                    catch (Exception X)
                    {
                        Trace.TraceError($"[{JobId}] EvalPEmbeddedNotifications Error: {X.Message}");
                        await SendToDeadLetterQueue(queueClient, message.AsString);
                    }

                    await queue.DeleteMessageAsync(message);
                    nNotification += 1;
                }

                //No more Message
                if (swMetaLoop)
                {
                    Trace.TraceInformation($"[{JobId}] No more messages");
                    break;
                }
                
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
        
        public async Task<bool> GetAssetProcessLock(string AssetId, string JobID, TimeSpan timeOut, TimeSpan delay)
        {
            bool Locked = false;
            int currentRetry = 0;
            CloudTable tableLock = tableClient.GetTableReference(ReferenceNames.AssetProcessLock);
            AssetProcessLock myLock = new AssetProcessLock(AssetId, JobID);
            DateTime endRunAt = DateTime.Now.Add(timeOut);

            for (; ; )
            {
                try
                {
                    // Insert LOCK on traffice light table.
                    await tableLock.CreateIfNotExistsAsync();
                    TableOperation CreateLock = TableOperation.Insert(myLock);
                    await tableLock.ExecuteAsync(CreateLock);
                    Locked = true;
                    // Return or break.
                    break;
                }
                catch (Exception)
                {
                    Trace.TraceInformation($"[{JobID}] Tried to lock Asset {AssetId} and failed");
                    currentRetry++;
                    if (DateTime.Now > endRunAt)
                    {
                        break;
                    }
                }
                // Wait to retry the operation.
                await Task.Delay(delay);
            }
            return Locked;
        }
        public async Task ReleaseAssetProcessLock(string AssetId, string JobID)
        {
            try
            {
                //await to delete lock 
                CloudTable tableLock = tableClient.GetTableReference(ReferenceNames.AssetProcessLock);
                AssetProcessLock myLock = new AssetProcessLock(AssetId, JobID);
                myLock.ETag = "*";
                TableOperation deleteOperation = TableOperation.Delete(myLock);
                await tableLock.ExecuteAsync(deleteOperation);
            }
            catch (Exception X)
            {
                throw new Exception($"[{JobID}] Error RealeaseAssetProcessLock for Asset {AssetId} : {X.Message}");
            }
        }
        public void UpdateUnifiedProcessStatus(UnifiedResponse.UnifiedProcessStatus curretnData)
        {
            //Update Asset
            var asset = curretnData.AssetStatus;
            UpdateAssetStatus(asset);
            //Update JobInformation
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(new UnifiedResponse.TJobStatus(curretnData.JobStatus, curretnData.AssetStatus.AssetId));
            _ProcessStatusTable.Execute(InsertOrReplace);
            //Update all Enbebed
            foreach (var data in curretnData.EmbebedCodesList)
            {
                UpdateWaterMarkedAssetInfo(data, curretnData.AssetStatus.AssetId);
            }

        }
        private UnifiedResponse.JobStatus GetJobStatus(string AssetId, string JobID)
        {
            UnifiedResponse.JobStatus data = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TJobStatus>(AssetId, JobID);
            TableResult retrievedResult = _ProcessStatusTable.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                data = ((UnifiedResponse.TJobStatus)retrievedResult.Result).GetJobStatus();
            }
            return data;
        }
        public UnifiedResponse.UnifiedProcessStatus GetUnifiedProcessStatus(string AssetId, string JobID)
        {
            UnifiedResponse.UnifiedProcessStatus Manifest = new UnifiedResponse.UnifiedProcessStatus
            {
                AssetStatus = GetAssetStatus(AssetId),
                JobStatus = GetJobStatus(AssetId, JobID),
                EmbebedCodesList = new List<UnifiedResponse.WaterMarkedAssetInfo>()

            };
            foreach (var ecode in Manifest.JobStatus.EmbebedCodeList)
            {
                Manifest.EmbebedCodesList.Add(GetWaterMarkedAssetInfo(AssetId, ecode));
            }
            return Manifest;
        }
        public UnifiedResponse.UnifiedProcessStatus StartNewProcess(string AssetId, string JobId, string[] EmbebedCodeList)
        {
            //unique embbeder codes
            string[] UniqueEmbbederList = EmbebedCodeList.Distinct().ToArray();
            if (UniqueEmbbederList.Count() != EmbebedCodeList.Count())
            {
                throw new Exception($"[{JobId}] StartNewProcess Embbeder codes are not unique. Removed repeated");
            }
            //NEW Process
            UnifiedResponse.UnifiedProcessStatus newProcess = new UnifiedResponse.UnifiedProcessStatus
            {
                EmbebedCodesList = new List<UnifiedResponse.WaterMarkedAssetInfo>(),
                //2. JobStatus
                JobStatus = new UnifiedResponse.JobStatus()
                {
                    Details = "Queue",
                    Duration = null,
                    FinishTime = null,
                    JobID = JobId,
                    StartTime = DateTime.Now,
                    State = ExecutionStatus.Running,
                    EmbebedCodeList = EmbebedCodeList
                },

                AssetStatus = GetAssetStatus(AssetId) ?? new UnifiedResponse.AssetStatus() { AssetId = AssetId, State = ExecutionStatus.New }
            };

            //Status
            ExecutionStatus EmbebedStatus = ExecutionStatus.New;

            switch (newProcess.AssetStatus.State)
            {
                case ExecutionStatus.Error:
                    newProcess.JobStatus.State = ExecutionStatus.Error;
                    newProcess.JobStatus.Details = "Invalid Asset becouse MMRK Files Generation failed";
                    newProcess.JobStatus.FinishTime = DateTime.Now;
                    newProcess.JobStatus.Duration = DateTime.Now.Subtract(newProcess.JobStatus.StartTime);
                    EmbebedStatus = ExecutionStatus.Aborted;

                    break;
                case ExecutionStatus.Running:
                    newProcess.JobStatus.Details = "Already MMRK Files Generation Runnig";
                    newProcess.JobStatus.State = ExecutionStatus.Error;
                    newProcess.JobStatus.FinishTime = DateTime.Now;
                    newProcess.JobStatus.Duration = DateTime.Now.Subtract(newProcess.JobStatus.StartTime);
                    EmbebedStatus = ExecutionStatus.Aborted;

                    break;

                case ExecutionStatus.New:
                case ExecutionStatus.Finished:
                    if (newProcess.AssetStatus.State == ExecutionStatus.New)
                    {
                        //New Asset
                        //Asset has not oter process running
                        newProcess.AssetStatus.State = ExecutionStatus.Running;
                        EmbebedStatus = ExecutionStatus.Running;
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
                            EmbebedStatus = ExecutionStatus.Aborted;
                        }
                        else
                        {
                            //Asset has not other process running
                            EmbebedStatus = ExecutionStatus.Running;
                            newProcess.JobStatus.State = ExecutionStatus.Running;
                        }
                    }
                    break;
                default:
                    break;
            }
            //Embebedecodes only if Process is Running
            if (newProcess.JobStatus.State == ExecutionStatus.Running)
            {
               
                foreach (var code in EmbebedCodeList)
                {
                    newProcess.EmbebedCodesList.Add(
                        new UnifiedResponse.WaterMarkedAssetInfo()
                        {
                            ParentAssetID = AssetId,
                            State = EmbebedStatus,
                            EmbebedCodeValue = code,
                            AssetID = "",
                            Details = "Just Start"
                        });
                }
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
            foreach (var watermaekAssetInfo in processState.EmbebedCodesList)
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
        public List<MMRKStatus> GetMMRKStatusList(string AssetID)
        {
            List<MMRKStatus> ret = new List<MMRKStatus>();
            TableQuery<TMMRKStatus> query = new TableQuery<TMMRKStatus>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, AssetID));
            var mmrkList = _MMRKSttausTable.ExecuteQuery(query);
            foreach (var item in mmrkList)
            {
                ret.Add(item.GetMMRKStatus());

            }
            return ret;
        }
        public void UpdateWaterMarkedAssetInfo(WaterMarkedAssetInfo data, string ParentAssetId)
        {
            TableOperation InsertOrReplace = TableOperation.InsertOrReplace(new UnifiedResponse.TWaterMarkedAssetInfo(data, ParentAssetId));
            _WaterMarkedAssetInfo.Execute(InsertOrReplace);
        }

        private UnifiedResponse.WaterMarkedAssetInfo GetWaterMarkedAssetInfo(string AssetId, string EmbebedCodeValue)
        {
            UnifiedResponse.WaterMarkedAssetInfo z = null;
            TableOperation retrieveOperation = TableOperation.Retrieve<UnifiedResponse.TWaterMarkedAssetInfo>(AssetId, EmbebedCodeValue);
            TableResult retrievedResult = _WaterMarkedAssetInfo.Execute(retrieveOperation);
            if (retrievedResult.Result != null)
            {
                z = ((UnifiedResponse.TWaterMarkedAssetInfo)retrievedResult.Result).GetWaterMarkedAsssetInfo();
            }

            return z;
        }
        public UnifiedResponse.WaterMarkedAssetInfo EvalWaterMarkedAssetInfo(string ParentAssetID, string EmbebedCodeValue)
        {
            UnifiedResponse.WaterMarkedAssetInfo currentWaterMarkInfo = GetWaterMarkedAssetInfo(ParentAssetID, EmbebedCodeValue);

            if (currentWaterMarkInfo.State == ExecutionStatus.Running)
            {
                var wmrTable = tableClient.GetTableReference(ReferenceNames.WaterMarkedRender);
                wmrTable.CreateIfNotExists();

                TableQuery<UnifiedResponse.TWaterMarkedRender> query =
                    new TableQuery<UnifiedResponse.TWaterMarkedRender>().Where(TableQuery.GenerateFilterCondition(
                        "PartitionKey", QueryComparisons.Equal, $"{ParentAssetID}-{EmbebedCodeValue}"));

                var wmrList = wmrTable.ExecuteQuery(query);
                if (wmrList != null)
                {
                    var wmErrorList = wmrList.Where(m => m.State == ExecutionStatus.Error.ToString());
                    if (wmErrorList.Count() > 0)
                    {
                        //Error
                        currentWaterMarkInfo.State = ExecutionStatus.Error;
                        //Update EmbebedCode
                        currentWaterMarkInfo.State = ExecutionStatus.Error;
                        currentWaterMarkInfo.Details = "";
                        foreach (var render in wmErrorList)
                        {
                            currentWaterMarkInfo.Details += $"{render.EmbebedCodeValue}: {render.Details} {Environment.NewLine}";
                        }
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
                UpdateWaterMarkedAssetInfo(currentWaterMarkInfo, ParentAssetID);
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
        private async Task SendToDeadLetterQueue(CloudQueueClient queueClient , string messageData)
        {
            //Trace.TraceInformation($"Send to SendToDeadLetterQueue: {message.AsString}");
            CloudQueue deadletter = queueClient.GetQueueReference("deadletter");
            await deadletter.CreateIfNotExistsAsync();
            await deadletter.AddMessageAsync(new CloudQueueMessage(messageData));
        }
        public async Task<int> EvalPreprocessorNotifications(string JobId)
        {
            int nNotification = 0;
            CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
            CloudQueue queue = queueClient.GetQueueReference("preprocessorout");

            foreach (CloudQueueMessage message in await queue.GetMessagesAsync(10, TimeSpan.FromMinutes(10), null, null))
            {
                Trace.TraceInformation($"[{JobId}] {message.AsString}");
                try
                {
                    var jNotification = Newtonsoft.Json.Linq.JObject.Parse(message.AsString);
                    // Retrive 
                    string jobid = (string)jNotification["JobID"];
                    jobid = jobid.Split('-')[0];
                    string jobRender = $"[{jobid}]{(string)jNotification["FileName"]}";
                    var MMRK = GetMMRKStatus((string)jNotification["AssetID"], jobRender);

                    if ((MMRK.State == ExecutionStatus.Running))
                    {
                        //Update MMRK Status
                        MMRK.Details = (string)jNotification["JobOutput"];
                        MMRK.State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), (string)jNotification["Status"]);
                        UpdateMMRKStatus(MMRK);
                    }
                    else
                    {
                        Trace.TraceInformation($"[{MMRK.JobID}] Idenpotent Message Control  {message.AsString}");
                        await SendToDeadLetterQueue(queueClient, message.AsString);
                    }
                }
                catch (Exception X)
                {
                    Trace.TraceError($"[JobId] EvalPreprocessorNotifications Error: {X.Message}");
                    await SendToDeadLetterQueue(queueClient,message.AsString);
                }
                await queue.DeleteMessageAsync(message);
                nNotification += 1;
            }
            return nNotification;
        }

        #endregion
        #region K8S JOBS
        private string GetJobYmal(string JobID, string JOBBASE64, string imagename, string PARALLELEMBEDDERS)
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
            //JobID lowercase becouse K8S Job Name don't support uppercases
            ymal = ymal.Replace("[JOBNAME]", "allinone-job-" + JobID.ToLower());
            //Same JOb ID for all jobs and pods, Label is original JobID
            ymal= ymal.Replace("[JOBID]",  JobID.Substring(0,JobID.IndexOf("-")));
            ymal = ymal.Replace("[IMAGENAME]", imagename);
            ymal = ymal.Replace("[PARALLELEMBEDDERS]", PARALLELEMBEDDERS);
            return ymal.Replace("[JOBBASE64]", JOBBASE64);
        }
        string GetBlobSasUri(Microsoft.WindowsAzure.Storage.Blob.CloudBlockBlob blockBlob)
        {
            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24);
            sasConstraints.Permissions = SharedAccessBlobPermissions.Read;
            string sasBlobToken = blockBlob.GetSharedAccessSignature(sasConstraints);
            return blockBlob.Uri + sasBlobToken;
        }
        private string SaveBlobData(string Data, string BlobName)
        {
            string sas = null;
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("watermarktmp");
            container.CreateIfNotExists();
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(BlobName);
            blockBlob.UploadText(Data);
            sas = GetBlobSasUri(blockBlob);
            return sas;
        }
        public async Task<K8SResult> SubmiteJobK8S(ManifestInfo manifest, int subId)
        {
            //Create Yamal Job definition
            manifest.JobID = $"{manifest.JobID}-{subId}";
            string manifesttxt = Newtonsoft.Json.JsonConvert.SerializeObject(manifest);
            //Save JOB data on Blob Storage And Generate a SASURL
            string jobbase64 = SaveBlobData(manifesttxt,$"{manifest.JobID}.json");
            string imageName = System.Configuration.ConfigurationManager.AppSettings["imageName"];
            string PARALLELEMBEDDERS = System.Configuration.ConfigurationManager.AppSettings["PARALLELEMBEDDERS"] ?? "5";
            string jobtxt = GetJobYmal(manifest.JobID, jobbase64, imageName, PARALLELEMBEDDERS);
            HttpContent ymal = new StringContent(jobtxt, Encoding.UTF8, "application/yaml");
            SaveBlobData(jobtxt, $"{manifest.JobID}.ymal");
            // Submite JOB
            IK8SClient k8sClient = K8SClientFactory.Create();
            var rs = await k8sClient.SubmiteK8SJob(ymal);
            if (!rs.IsSuccessStatusCode)
            {
                Trace.TraceError($"[{manifest.JobID}]SubmiteJobK8S : {jobtxt}");
            }
            return rs;

        }
        private ManifestInfo GetManifestInfo(int skip, int take, ManifestInfo manifest)
        {
            ManifestInfo aggregateJobManifest = new ManifestInfo()
            {
                AssetID = manifest.AssetID,
                EmbedderNotificationQueue = manifest.EmbedderNotificationQueue,
                EnbebedCodes = new List<EnbebedCode>(),
                JobID = manifest.JobID,
                PreprocessorNotificationQueue = manifest.PreprocessorNotificationQueue,
                VideoInformation = new List<VideoInformation>()
            };
            //Add aggregated video info nodes
            aggregateJobManifest.VideoInformation.AddRange(manifest.VideoInformation.Skip(skip).Take(take));
            foreach (var emc in manifest.EnbebedCodes)
            {
                EnbebedCode jobemc = new EnbebedCode()
                {
                    EmbebedCode = emc.EmbebedCode,
                    MP4WatermarkedURL = new List<MP4WatermarkedURL>()
                };
                foreach (var vi in aggregateJobManifest.VideoInformation)
                {
                    MP4WatermarkedURL myMMP4WatermarkeInfo = emc.MP4WatermarkedURL.Where(x => x.FileName == vi.FileName).FirstOrDefault();
                    //add to the list
                    jobemc.MP4WatermarkedURL.Add(myMMP4WatermarkeInfo);
                }
                aggregateJobManifest.EnbebedCodes.Add(jobemc);
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
