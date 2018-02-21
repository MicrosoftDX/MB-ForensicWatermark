// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider;
using ActionsProvider.AMS;
using ActionsProvider.Entities;
using ActionsProvider.K8S;
using ActionsProvider.UnifiedResponse;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading.Tasks;

namespace WaterMarkingActions
{
    public static class WaterMArkActions
    {
        [FunctionName("StartNewJob")]
        public static async Task<HttpResponseMessage> StartNewJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            string AssetID = BodyData.AssetID;
            string JobID = BodyData.JobID;
            string[] EmbebedCodes = BodyData.EmbebedCodes.ToObject<string[]>();
            //Save Status
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            try
            {
                var status = myActions.StartNewProcess(AssetID, JobID, EmbebedCodes);
                watch.Stop();
                log.Info($"[Time] Method StartNewJob {watch.ElapsedMilliseconds} [ms]");
                return req.CreateResponse(HttpStatusCode.OK, status, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {

                log.Error($"{X.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
        }

        [FunctionName("GetPreprocessorJobData")]
        public static async Task<HttpResponseMessage> GetJobManifest([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            string AssetId = BodyData.AssetId;
            string JobID = BodyData.JobID;
            List<string> codes = BodyData.Codes.ToObject<List<string>>();
            IAMSProvider myAMS = AMSProviderFactory.CreateAMSProvider();
            try
            {
                ManifestInfo jobdata = await myAMS.GetK8SJobManifestAsync(AssetId, JobID, codes);
                return req.CreateResponse(HttpStatusCode.OK, jobdata, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {

                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
        }

        [FunctionName("UpdateMMRKStatus")]
        public static async Task<HttpResponseMessage> UpdateMMRKStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            //Create JobStatus from body data
            MMRKStatus MMRK = BodyData.ToObject<MMRKStatus>();

            //Save Status
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            if (MMRK.FileURL == "{NO UPDATE}")
            {
                string jobRender = $"[{MMRK.JobID}]{MMRK.FileName}";
                var currentMMRKStatus = myActions.GetMMRKStatus(MMRK.AssetID, jobRender);
                string url = currentMMRKStatus.FileURL;
                MMRK.FileURL = url;
            }
            myActions.UpdateMMRKStatus(MMRK);
            return req.CreateResponse(HttpStatusCode.OK, MMRK, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("EvalAssetStatus")]
        public static async Task<HttpResponseMessage> EvalAssetStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            UnifiedProcessStatus manifest = BodyData.ToObject<UnifiedProcessStatus>();

            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            //1. Update EvalPreprocessorNotifications
            int nNotification = await myActions.EvalPreprocessorNotifications(manifest.JobStatus.JobID);
            
            //2. Eval Asset Status
            var OriginalAssetStatus = manifest.AssetStatus.State;
            manifest.AssetStatus = myActions.EvalAssetStatus(manifest.AssetStatus.AssetId);
            if (OriginalAssetStatus != manifest.AssetStatus.State)
            {
                //3. Update Manifest/ all process
                myActions.UpdateUnifiedProcessStatus(manifest);
                //4. Log and replay
                log.Info($"[{manifest.JobStatus.JobID}] Preprocessor Notifications processed {nNotification} / Updated AssetId {manifest.AssetStatus.AssetId} Status {manifest.AssetStatus.State.ToString()} / previus status {OriginalAssetStatus}");
            }
            else
            {
                log.Info($"[{manifest.JobStatus.JobID}] Preprocessor Notifications processed {nNotification} / No Change AssetId {manifest.AssetStatus.AssetId} Status {OriginalAssetStatus}");
            }
            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("UpdateWaterMarkCode")]
        public static async Task<HttpResponseMessage> UpdateWaterMarkCode([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            EnbebedCode myCode = BodyData.EnbebedCode.ToObject<EnbebedCode>();
            string ParentAssetID = BodyData.ParentAssetID;

            foreach (var info in myCode.MP4WatermarkedURL)
            {
                WaterMarkedRender data = new WaterMarkedRender()
                {
                    Details = "Submited",
                    EmbebedCodeValue = myCode.EmbebedCode,
                    MP4URL = info.WaterMarkedMp4,
                    RenderName = info.FileName,
                    ParentAssetID = ParentAssetID,
                    State = ExecutionStatus.Running

                };
                myActions.UpdateWaterMarkedRender(data);
                var outputData = myActions.UpdateWaterMarkedRender(data);
            }
            return req.CreateResponse(HttpStatusCode.OK, new { Status = ExecutionStatus.Finished.ToString() }, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("EvalEnbebedCodes")]
        public static async Task<HttpResponseMessage> EvalEnbebedCodes([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            UnifiedProcessStatus manifest = BodyData.ToObject<UnifiedProcessStatus>();
            string ParentAssetID = manifest.AssetStatus.AssetId;
            try
            {
                //1. Process Embbeded Notifications (From share Queue)
                int nNotification = await myActions.EvalPEmbeddedNotifications(manifest.JobStatus.JobID);
                log.Info($"Embedded Notifications processed {nNotification}");
                //2. Eval Each Watermark Render status
                List<WaterMarkedAssetInfo> UpdatedInfo = new List<WaterMarkedAssetInfo>();
                foreach (var item in manifest.EmbebedCodesList)
                {
                    //2.1 EvalWatermarkeAssetInfo 
                    var watermarkedRender = myActions.EvalWaterMarkedAssetInfo(ParentAssetID, item.EmbebedCodeValue);
                    UpdatedInfo.Add(watermarkedRender);
                }
                //Replace New WaterMarkAssetInfo
                manifest.EmbebedCodesList = UpdatedInfo;
                myActions.UpdateUnifiedProcessStatus(manifest);
            }
            catch (Exception X)
            {
                log.Error($"[{manifest.JobStatus.JobID}] Error on EvalEnbebedCodes {X.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError, manifest, JsonMediaTypeFormatter.DefaultMediaType);
            }
            watch.Stop();
            log.Info($"[Time] Method EvalEnbebedCodes {watch.ElapsedMilliseconds} [ms]");
            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("CreateWaterMArkedAssets")]
        public static async Task<HttpResponseMessage> CreateWaterMArkedAssets([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();

            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            UnifiedProcessStatus manifest = BodyData.ToObject<UnifiedProcessStatus>();
            string ParentAssetID = manifest.AssetStatus.AssetId;
            try
            {
                IAMSProvider help = AMSProviderFactory.CreateAMSProvider();
                //Max number of Asset to create per iteration, to avoid Function Time out.
                int maxAssetCreate = int.Parse(System.Configuration.ConfigurationManager.AppSettings["maxAssetCreate"] ?? "10");
                int accAssetCreate = 0;
                //List only Finished without AsstID
                foreach (var watermarkedInfo in manifest.EmbebedCodesList)
                {
                    bool swError = false;
                    if ((watermarkedInfo.State == ExecutionStatus.Finished) && (string.IsNullOrEmpty(watermarkedInfo.AssetID) && (accAssetCreate<=maxAssetCreate)))
                    {
                        var watchAsset = System.Diagnostics.Stopwatch.StartNew();
                        ////Inject all Renders on New asset
                        var AssetWatermarkedRenders = myActions.GetWaterMarkedRenders(ParentAssetID, watermarkedInfo.EmbebedCodeValue);
                        if (AssetWatermarkedRenders.Count()>0)
                        {
                            //Create new asset per embbeded code
                            var xx = await help.CreateEmptyWatermarkedAsset(manifest.JobStatus.JobID, ParentAssetID, watermarkedInfo.EmbebedCodeValue);
                            watermarkedInfo.AssetID = xx.WMAssetId;
                            //Renders exist, so Process
                            foreach (var render in AssetWatermarkedRenders)
                            {
                                string url = render.MP4URL;
                                var r = await help.AddWatermarkedMediaFiletoAsset(watermarkedInfo.AssetID, watermarkedInfo.EmbebedCodeValue, url);
                                if (r.Status != "MMRK File Added")
                                {
                                    //Error
                                    watermarkedInfo.State = ExecutionStatus.Error;
                                    watermarkedInfo.Details = $"Error adding {render.RenderName} deatils: {r.StatusMessage}";
                                    //Delete Asset
                                    help.DeleteAsset(watermarkedInfo.AssetID);
                                    watermarkedInfo.AssetID = "";
                                    log.Error($"[{manifest.JobStatus.JobID}] Asset deleted  {r.Status}");
                                    //Abort
                                    swError = true;
                                    break;
                                }
                            }
                            //If not Error Create Manifest
                            if (!swError)
                            {
                                //Create New Manifest and set it as primary file.
                                var manifestResult=await help.GenerateManifest(watermarkedInfo.AssetID);
                                if (manifestResult.Status!= "OK")
                                {
                                    //Error Creating Manifest
                                    help.DeleteAsset(watermarkedInfo.AssetID);
                                    watermarkedInfo.State = ExecutionStatus.Error;
                                    watermarkedInfo.Details = $"Error  Creating MAnifest deatils: {manifestResult.StatusMessage}";
                                    log.Error($"[{manifest.JobStatus.JobID}] Error at GenerateManifest  {watermarkedInfo.Details}");
                                }
                                else
                                {
                                    //Delete Watermarked MP4 renders
                                    await myActions.DeleteWatermarkedRenderTmpInfo(AssetWatermarkedRenders);
                                    //One Asset created
                                    accAssetCreate += 1;
                                }
                            }
                        }
                        else
                        {
                            //Watermarked Copy error, It has not renders.
                            watermarkedInfo.State = ExecutionStatus.Error;
                            watermarkedInfo.Details = $"Error adding {watermarkedInfo.EmbebedCodeValue} renders deatils: Has not renders MP4";
                            watermarkedInfo.AssetID = "";
                            log.Error($"[{manifest.JobStatus.JobID}] Error processing Wateramark copy {watermarkedInfo.EmbebedCodeValue} error: {watermarkedInfo.Details}");
                        }
                        //Update WatermarkedCopy
                        myActions.UpdateWaterMarkedAssetInfo(watermarkedInfo, manifest.AssetStatus.AssetId);
                        //TimeTrack
                        watchAsset.Stop();
                        log.Info($"[Time][{manifest.JobStatus.JobID}] Asset Creation {watchAsset.ElapsedMilliseconds} [ms] code {watermarkedInfo.EmbebedCodeValue} assetID: {watermarkedInfo.AssetID}");
                    }
                    if (watch.ElapsedMilliseconds>=100000)
                    {
                        log.Warning($"[{manifest.JobStatus.JobID}] Asset Creation time limite achieved, break loop with {accAssetCreate-1} copies");
                        break;
                    }
                }
            }
            catch (Exception X)
            {
                log.Error($"{X.Message}");
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
            watch.Stop();
            log.Info($"[Time] Method CreateWaterMArkedAssets {watch.ElapsedMilliseconds} [ms]");
            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("EvalJobProgress")]
        public static async Task<HttpResponseMessage> EvalJobProgress([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            UnifiedProcessStatus myProcessStatus = BodyData.ToObject<UnifiedProcessStatus>();
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            int watingCopies = 0;
            //Check AssetStatus
            log.Info($"[{myProcessStatus.JobStatus.JobID}] Job Status: {myProcessStatus.JobStatus.State.ToString()} / Asset Status {myProcessStatus.AssetStatus.State.ToString()}");
            switch (myProcessStatus.AssetStatus.State)
            {
                case ExecutionStatus.Error:
                    //Finished with error
                    myProcessStatus.JobStatus.State = ExecutionStatus.Error;
                    myProcessStatus.JobStatus.FinishTime = DateTime.Now;
                    myProcessStatus.JobStatus.Duration = DateTime.Now.Subtract(myProcessStatus.JobStatus.StartTime);
                    myActions.UpdateUnifiedProcessStatus(myProcessStatus);
                    break;
                case ExecutionStatus.Running:
                    //Same status for JOB
                    //No Action
                    myProcessStatus.JobStatus.State = ExecutionStatus.Running;
                    var mmrklist = myActions.GetMMRKStatusList(myProcessStatus.AssetStatus.AssetId);
                    int mmrkTotal = mmrklist.Count();
                    int mmrkFinished = mmrklist.Where(m => m.State == ExecutionStatus.Finished).Count();
                    myProcessStatus.JobStatus.Details = $"Generating MMRK files {mmrkFinished} of {mmrkTotal}, last update {DateTime.Now.ToString()}";
                    myActions.UpdateUnifiedProcessStatus(myProcessStatus);
                    break;
                case ExecutionStatus.Finished:
                    //Check EmbebedCodeList, Count running renders plus Finished render without AssetID (not created asset yet)
                    int nRunning = myProcessStatus.EmbebedCodesList.Where(emc => (emc.State == ExecutionStatus.Running) || ((emc.AssetID=="") &&(emc.State==ExecutionStatus.Finished))).Count();
                    //Check Errros
                    int nErrors = myProcessStatus.EmbebedCodesList.Where(emc => emc.State == ExecutionStatus.Error).Count();
                    //Total
                    int total = myProcessStatus.EmbebedCodesList.Count();

                    log.Info($"Current EMC Running {nRunning}");
                    if (nRunning == 0)
                    {
                        myProcessStatus.JobStatus.State = ExecutionStatus.Finished;
                        myProcessStatus.JobStatus.FinishTime = DateTime.Now;
                        myProcessStatus.JobStatus.Duration = DateTime.Now.Subtract(myProcessStatus.JobStatus.StartTime);
                        myProcessStatus.JobStatus.Details = $"Process Finished. Total Watermarked copies {total}, Success {total-nErrors} & Errors {nErrors}";
                    }
                    else
                    {
                        myProcessStatus.JobStatus.State = ExecutionStatus.Running;
                        myProcessStatus.JobStatus.Details = $"Process Running. Total Watermark copies {total}, Success {total - nRunning - nErrors} & Errors {nErrors}";
                        watingCopies = myProcessStatus.EmbebedCodesList.Where(emc => ((emc.AssetID == "") && (emc.State == ExecutionStatus.Finished))).Count();
                    }
                    myActions.UpdateUnifiedProcessStatus(myProcessStatus);
                    log.Info($"Updated Manifest JOB Status {myProcessStatus.JobStatus.State.ToString()}");
                    break;
            }
            //Control Signal to LogicApp, to accelearete
            var myResponse = req.CreateResponse(HttpStatusCode.OK, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
            myResponse.Headers.Add("watingCopies", watingCopies.ToString());

            watch.Stop();
            log.Info($"[Time] Method EvalJobProgress {watch.ElapsedMilliseconds} [ms]");
            return myResponse;
        }
        [FunctionName("GetUnifiedProcessStatus")]
        public static async Task<HttpResponseMessage> GetUnifiedProcessStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            string AssetId = BodyData.AssetId;
            string JobID = BodyData.JobID;

            UnifiedProcessStatus myProcessStatus = null;
            try
            {
                 myProcessStatus = myActions.GetUnifiedProcessStatus(AssetId, JobID);
            }
            catch (Exception X)
            {
                log.Error(X.Message);
                return req.CreateResponse(HttpStatusCode.NotFound, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
            watch.Stop();
            log.Info($"[Time] GetUnifiedProcessStatus {watch.ElapsedMilliseconds} [ms]");
            return req.CreateResponse(HttpStatusCode.OK, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("SubmiteWaterMarkJob")]
        public static async Task<HttpResponseMessage> SubmiteWaterMarkJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            ManifestInfo manifest = BodyData.ToObject<ManifestInfo>();
            int K8SJobAggregation = int.Parse(System.Configuration.ConfigurationManager.AppSettings["K8SJobAggregation"]);
            int K8SJobAggregationOnlyEmb = int.Parse(System.Configuration.ConfigurationManager.AppSettings["K8SJobAggregationOnlyEmb"] ?? "1");
            try
            {
                //Get JOBList to Send to K8S cluster
                List<ManifestInfo> jobList = myActions.GetK8SManifestInfo(K8SJobAggregation, K8SJobAggregationOnlyEmb, manifest);
                //Sumbite to K8S cluster
                int jobSubId = 1;
                foreach (var job in jobList)
                {
                    var ret = await myActions.SubmiteJobK8S(job, jobSubId);
                    log.Info($"{job.VideoInformation.FirstOrDefault().FileName} CODE {ret.Code.ToString()}");
                    jobSubId += 1;
                    if (!ret.IsSuccessStatusCode)
                    {
                        log.Error($"K8S Summition Error {ret.Code.ToString()} {ret.Content}");
                        throw new Exception($"K8S Summition Error {ret.Code.ToString()} {ret.Content}");
                    }
                }
                return req.CreateResponse(HttpStatusCode.OK, jobList, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {

                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }

        }
        [FunctionName("UpdateJob")]
        public static async Task<HttpResponseMessage> UpdateJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            UnifiedProcessStatus Manifest = BodyData.Manifest.ToObject<UnifiedProcessStatus>();
            ExecutionStatus AssetStatus = BodyData.AssetStatus.ToObject<ExecutionStatus>();
            ExecutionStatus JobState = BodyData.JobState.ToObject<ExecutionStatus>();
            string JobStateDetails = BodyData.JobStateDetails;
            ExecutionStatus WaterMarkCopiesStatus = BodyData.WaterMarkCopiesStatus.ToObject<ExecutionStatus>();
            string WaterMarkCopiesStatusDetails = BodyData.WaterMarkCopiesStatusDetails;
            switch (Manifest.AssetStatus.State)
            {
                case ExecutionStatus.Finished:
                    AssetStatus = ExecutionStatus.Finished;
                    break;
                case ExecutionStatus.Error:
                    break;
                case ExecutionStatus.Running:
                    break;
                case ExecutionStatus.New:
                    break;
                case ExecutionStatus.Aborted:
                    break;
                default:
                    break;
            }
            var updatedManifest = myActions.UpdateJob(Manifest, AssetStatus, JobState, JobStateDetails, WaterMarkCopiesStatus, WaterMarkCopiesStatusDetails);

            return req.CreateResponse(HttpStatusCode.OK, Manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("DeleteWatermarkedRenders")]
        public static async Task<HttpResponseMessage> DeleteWatermarkedRenders([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
            IAMSProvider myAMShelp = AMSProviderFactory.CreateAMSProvider();

            string AssetId = BodyData.AssetId;
            string JobId = BodyData.JobId;
            try
            {
                string KeepWatermakedBlobs = System.Configuration.ConfigurationManager.AppSettings["KeepWatermakedBlobs"] ?? "false";
                int DeletedBlobs = 0;
                if (!(KeepWatermakedBlobs != "false"))
                {
                    //Delete all information generated by the Job
                    DeletedBlobs=await myAMShelp.DeleteWatermakedBlobRendersAsync(JobId,AssetId);
                }
                else
                    log.Info($"[{JobId}]Blob not deleted {AssetId}");
                watch.Stop();
                log.Info($"[Time] Method DeleteWatermarkedRenders {watch.ElapsedMilliseconds} [ms]");
                return req.CreateResponse(HttpStatusCode.OK, new { DeletedBlob = DeletedBlobs }, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
        }

        [FunctionName("DeleteSucceededPods")]
        public static async Task<HttpResponseMessage> DeleteSucceededPods([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            HttpResponseMessage myResponse = null;
            bool DeleteSucceededPods = int.Parse(System.Configuration.ConfigurationManager.AppSettings["DeleteSucceededPods"] ?? "1")==1;
            if (DeleteSucceededPods)
            {
                dynamic BodyData = await req.Content.ReadAsAsync<object>();
                string JobId = BodyData.JobId;
                IK8SClient k = K8SClientFactory.Create();
                try
                {
                    var r = await k.DeleteJobs(JobId);
                    myResponse= req.CreateResponse(r.Code, r, JsonMediaTypeFormatter.DefaultMediaType);
                }
                catch (Exception X)
                {
                    myResponse= req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
                }
            }
            else
            {
                return req.CreateResponse(HttpStatusCode.OK, new { mesagge="Ignored"} , JsonMediaTypeFormatter.DefaultMediaType);
            }
            watch.Stop();
            log.Info($"[Time] Method GetK8SProcessLog {watch.ElapsedMilliseconds} [ms]");
            return myResponse;
        }
        [FunctionName("GetK8SProcessLog")]
        public static async Task<HttpResponseMessage> GetK8SProcessLog([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            HttpResponseMessage myResponse = null;
            string JobId =  req.GetQueryNameValuePairs().FirstOrDefault(q => string.Compare(q.Key, "JobId", true) == 0).Value;
            if (string.IsNullOrEmpty(JobId))
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError, new { Error = "Parameter JobId is null" }, JsonMediaTypeFormatter.DefaultMediaType);
            }

            var K8S = K8SClientFactory.Create();
            try
            {
                
                var ResultList = await K8S.GetK8SJobLog(JobId);
               
                if (ResultList.Count == 0)
                {
                    //Not Found
                    myResponse= req.CreateResponse(HttpStatusCode.NotFound, ResultList, JsonMediaTypeFormatter.DefaultMediaType);
                }
                else
                {
                    //LOG
                    myResponse= req.CreateResponse(HttpStatusCode.OK, ResultList, JsonMediaTypeFormatter.DefaultMediaType);
                }
            }
            catch (Exception X)
            {
                log.Error(X.Message);
                myResponse= req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }

            watch.Stop();
            log.Info($"[Time] Method GetK8SProcessLog {watch.ElapsedMilliseconds} [ms]");
            return myResponse;
        }
        [FunctionName("CancelJobTimeOut")]
        public static async Task<HttpResponseMessage> CancelJobTimeOut([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            dynamic BodyData = await req.Content.ReadAsAsync<object>();
                     
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            UnifiedProcessStatus myProcessStatus = BodyData.ToObject<UnifiedProcessStatus>();
            try
            {
                myProcessStatus = myActions.GetUnifiedProcessStatus(myProcessStatus.AssetStatus.AssetId, myProcessStatus.JobStatus.JobID);
                myProcessStatus.JobStatus.State = ExecutionStatus.Finished;
                myProcessStatus.JobStatus.Details = $"Embedder copies time out, check copies status.";
                myProcessStatus.JobStatus.FinishTime = DateTime.Now;
                myProcessStatus.JobStatus.Duration = DateTime.Now.Subtract(myProcessStatus.JobStatus.StartTime);

                //Update all running copies like Errro time out
                foreach (var copy in myProcessStatus.EmbebedCodesList.Where(c=>(c.State==ExecutionStatus.Running)))
                {
                    copy.State = ExecutionStatus.Error;
                    copy.Details = $"Timeout error: {copy.Details}";
                }
                //Update All Finished  copies without AssetID (No ASSET copy created)
                foreach (var copy in myProcessStatus.EmbebedCodesList.Where(c => (c.State == ExecutionStatus.Finished) && (string.IsNullOrEmpty(c.AssetID))))
                {
                    copy.State = ExecutionStatus.Error;
                    copy.Details = $"Timeout error: {copy.Details}";
                }
                myActions.UpdateUnifiedProcessStatus(myProcessStatus);
            }
            catch (Exception X)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
            watch.Stop();
            log.Info($"[Time] Method CancelJobTimeOut {watch.ElapsedMilliseconds} [ms]");
            return req.CreateResponse(HttpStatusCode.OK, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
        }
    }
}