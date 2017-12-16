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
    public static class WaterMarkActions
    {
        /// <summary>
        /// Check the status of an asset
        /// </summary>
        /// <param name="req"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("CheckAssetStatus")]
        public static async Task<HttpResponseMessage> CheckAssetStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.CheckAssetStatus>();
            string AssetId = BodyData.AssetId;
            //Save Status
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            var status = myActions.GetAssetStatus(AssetId);
            if(status == null)
            {
                return req.CreateResponse(HttpStatusCode.NotFound,$"Asset with id '{AssetId}' was not watermarked yet.", JsonMediaTypeFormatter.DefaultMediaType);
            }
            return req.CreateResponse(HttpStatusCode.OK, status, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("StartNewJob")]
        public static async Task<HttpResponseMessage> StartNewJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.StartNewJob>();
            string AssetId = BodyData.AssetId;
            string JobId = BodyData.JobId;
            string[] EmbeddedCodes = BodyData.EmbeddedCodes;
            //Save Status
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            var status = myActions.StartNewProcess(AssetId, JobId, EmbeddedCodes);
            return req.CreateResponse(HttpStatusCode.OK, status, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("GetPreprocessorJobData")]
        public static async Task<HttpResponseMessage> GetJobManifest([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.GetPreprocessorJobData>();
            string AssetId = BodyData.AssetId;
            string JobId = BodyData.JobId;
            List<string> codes = BodyData.EmbeddedCodes;
            IAMSProvider myAMS = AMSProviderFactory.CreateAMSProvider();
            try
            {
                ManifestInfo jobdata = await myAMS.GetK8SJobManifestAsync(AssetId, JobId, codes);
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
            string content = await req.Content.ReadAsStringAsync();

            //Create JobStatus from body data
            MMRKStatus MMRK = content.GetBodyData<MMRKStatus>();
            if(MMRK == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, MMRK, JsonMediaTypeFormatter.DefaultMediaType);
            }
            //Save Status
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            if (MMRK.FileURL == "{NO UPDATE}")
            {
                string jobRender = $"[{MMRK.JobId}]{MMRK.FileName}";
                var currentMMRKStatus = myActions.GetMMRKStatus(MMRK.AssetId, jobRender);
                string url = currentMMRKStatus.FileURL;
                MMRK.FileURL = url;
            }
            myActions.UpdateMMRKStatus(MMRK);
            return req.CreateResponse(HttpStatusCode.OK, MMRK, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("EvalAssetStatus")]
        public static async Task<HttpResponseMessage> EvalAssetStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            string content = await req.Content.ReadAsStringAsync();
            UnifiedProcessStatus manifest = content.GetBodyData<UnifiedProcessStatus>();
           if(manifest == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, manifest, JsonMediaTypeFormatter.DefaultMediaType);
            }

            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            //1. Update EvalPreprocessorNotifications
            int nNotification = await myActions.EvalPreprocessorNotifications();
            log.Info($"Preprocessor Notifications processed {nNotification}");
            //2. Eval Asset Status
            manifest.AssetStatus = myActions.EvalAssetStatus(manifest.AssetStatus.AssetId);
            //3. Update Manifest/ all process
            myActions.UpdateUnifiedProcessStatus(manifest);
            //4. Log and replay
            log.Info($"Updated Actions AssetId {manifest.AssetStatus.AssetId} status {manifest.AssetStatus.ToString()}");
            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("UpdateWaterMarkCode")]
        public static async Task<HttpResponseMessage> UpdateWaterMarkCode([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.UpdateWaterMarkCode>();
            if (BodyData == null )
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, BodyData, JsonMediaTypeFormatter.DefaultMediaType);
            }
            EmbeddedCode myCode = BodyData.EmbeddedCode;
            string ParentAssetId = BodyData.ParentAssetId;

            foreach (var info in myCode.MP4WatermarkedURL)
            {
                WaterMarkedRender data = new WaterMarkedRender()
                {
                    Details = "Submited",
                    EmbeddedCodeValue = myCode.Code,
                    MP4URL = info.WaterMarkedMp4,
                    RenderName = info.FileName,
                    ParentAssetId = ParentAssetId,
                    State = ExecutionStatus.Running

                };
                myActions.UpdateWaterMarkedRender(data);
                var outputData = myActions.UpdateWaterMarkedRender(data);
            }
            return req.CreateResponse(HttpStatusCode.OK, new { Status = ExecutionStatus.Finished.ToString() }, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("EvalEmbeddedCodes")]
        public static async Task<HttpResponseMessage> EvalEmbeddedCodes([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            string content = await req.Content.ReadAsStringAsync();
            UnifiedProcessStatus manifest = content.GetBodyData<UnifiedProcessStatus>();
            if (manifest == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, manifest, JsonMediaTypeFormatter.DefaultMediaType);
            }
            string ParentAssetId = manifest.AssetStatus.AssetId;
            //1. Process Embbeded Notifications
            int nNotification = await myActions.EvalPEmbeddedNotifications();
            log.Info($"Embedded Notifications processed {nNotification}");
            //2. Eval Each Watermark Render status
            List<WaterMarkedAssetInfo> UpdatedInfo = new List<WaterMarkedAssetInfo>();
            foreach (var item in manifest.EmbeddedCodesList)
            {

                UpdatedInfo.Add(myActions.EvalWaterMarkedAssetInfo(ParentAssetId, item.EmbeddedCodeValue));

            }
            //Replace New WaterMarkAssetInfo
            manifest.EmbeddedCodesList = UpdatedInfo;
            //
            myActions.UpdateUnifiedProcessStatus(manifest);

            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("CreateWaterMarkedAssets")]
        public static async Task<HttpResponseMessage> CreateWaterMarkedAssets([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();

            string content = await req.Content.ReadAsStringAsync();
            UnifiedProcessStatus manifest = content.GetBodyData<UnifiedProcessStatus>();
            if (manifest == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, manifest, JsonMediaTypeFormatter.DefaultMediaType);
            }
            string ParentAssetId = manifest.AssetStatus.AssetId;

            List<WaterMarkedAssetInfo> UpdatedInfo = new List<WaterMarkedAssetInfo>();

            try
            {

            
            //List only Finished without AssetId
            foreach (var watermarkedInfo in manifest.EmbeddedCodesList)
            {
                if ((watermarkedInfo.State == ExecutionStatus.Finished) && (string.IsNullOrEmpty(watermarkedInfo.AssetId)))
                {
                    //Create new asset per embbeded code
                    IAMSProvider help = AMSProviderFactory.CreateAMSProvider();
                    var xx = await help.CreateEmptyWatermarkedAsset(manifest.JobStatus.JobId, ParentAssetId, watermarkedInfo.EmbeddedCodeValue);

                    watermarkedInfo.AssetId = xx.WMAssetId;
                    ////Inject all Renders on New asset
                    foreach (var render in myActions.GetWaterMarkedRenders(ParentAssetId, watermarkedInfo.EmbeddedCodeValue))
                    {
                        string url = render.MP4URL;
                        var r = await help.AddWatermarkedMediaFiletoAsset(watermarkedInfo.AssetId, watermarkedInfo.EmbeddedCodeValue, url);
                        if (r.Status != "MMRK File Added")
                        {
                            //Error
                            watermarkedInfo.State = ExecutionStatus.Error;
                            watermarkedInfo.Details = $"Error adding {render.RenderName} details: {r.StatusMessage}";
                            //Delete Asset
                            help.DeleteAsset(watermarkedInfo.AssetId);
                            watermarkedInfo.AssetId = "";
                            //Abort
                            break;
                        }
                    }
                    //Create New Manifest and set it as primary file.
                    await help.GenerateManifest(watermarkedInfo.AssetId);
                }
                UpdatedInfo.Add(watermarkedInfo);
            }
            //Replace New WaterMarkAssetInfo
            manifest.EmbeddedCodesList = UpdatedInfo;

            myActions.UpdateUnifiedProcessStatus(manifest);
            }
            catch (Exception X)
            {

                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
            return req.CreateResponse(HttpStatusCode.OK, manifest, JsonMediaTypeFormatter.DefaultMediaType);
        }

        [FunctionName("EvalJobProgress")]
        public static async Task<HttpResponseMessage> EvalJobProgress([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {

            
            string content = await req.Content.ReadAsStringAsync();
            UnifiedProcessStatus myProcessStatus = content.GetBodyData<UnifiedProcessStatus>();
            if (myProcessStatus == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
            }
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            //Check AssetStatus

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

                    //var jobinfo = myActions.GetJobK8SDetail(myProcessStatus.JobStatus.JobId + "-1");
                    //myProcessStatus.JobStatus.Details += Newtonsoft.Json.JsonConvert.SerializeObject(jobinfo, Newtonsoft.Json.Formatting.Indented);

                    break;
                case ExecutionStatus.Finished:
                    //Check EmbeddedCodeList
                    int nRunning = myProcessStatus.EmbeddedCodesList.Where(emc => emc.State == ExecutionStatus.Running).Count();
                    log.Info($"Current EMC Running {nRunning}");
                    if (nRunning == 0)
                    {
                        myProcessStatus.JobStatus.State = ExecutionStatus.Finished;
                        myProcessStatus.JobStatus.FinishTime = DateTime.Now;
                        myProcessStatus.JobStatus.Duration = DateTime.Now.Subtract(myProcessStatus.JobStatus.StartTime);
                        myProcessStatus.JobStatus.Details = "Finished";
                    }
                    else
                    {
                        int total = myProcessStatus.EmbeddedCodesList.Count();
                        myProcessStatus.JobStatus.State = ExecutionStatus.Running;
                        myProcessStatus.JobStatus.Details = $"Watermarked copies {(total - nRunning)} of {total}";

                    }
                    myActions.UpdateUnifiedProcessStatus(myProcessStatus);
                    log.Info($"Updated Manifest JOB Status {myProcessStatus.JobStatus.State.ToString()}");
                    break;
            }

            return req.CreateResponse(HttpStatusCode.OK, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
        }
        [FunctionName("GetUnifiedProcessStatus")]
        public static async Task<HttpResponseMessage> GetUnifiedProcessStatus([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.GetUnifiedProcessStatus>();
            if (BodyData == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, BodyData, JsonMediaTypeFormatter.DefaultMediaType);
            }
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            string AssetId = BodyData.AssetId;
            string JobId = BodyData.JobId;
                      
            try
            {
                var myProcessStatus = myActions.GetUnifiedProcessStatus(AssetId, JobId);
                return req.CreateResponse(HttpStatusCode.OK, myProcessStatus, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {
                log.Error(X.Message);
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
        }
        [FunctionName("SubmiteWaterMarkJob")]
        public static async Task<HttpResponseMessage> SubmiteWaterMarkJob([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();
            string content = await req.Content.ReadAsStringAsync();
            ManifestInfo manifest = content.GetBodyData<ManifestInfo>();
            if (manifest == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, manifest, JsonMediaTypeFormatter.DefaultMediaType);
            }
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
            
            IActionsProvider myActions = ActionProviderFactory.GetActionProvider();

            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.UpdateJob>();
            if (BodyData == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, BodyData, JsonMediaTypeFormatter.DefaultMediaType);
            }

            ExecutionStatus AssetStatus = BodyData.AssetStatus;
            UnifiedProcessStatus Manifest = BodyData.Manifest;
            ExecutionStatus JobState = BodyData.JobState;
            string JobStateDetails = BodyData.JobStateDetails;

            ExecutionStatus WaterMarkCopiesStatus = BodyData.WaterMarkCopiesStatus;
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
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.AssetRequest>();
            if (BodyData == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, BodyData, JsonMediaTypeFormatter.DefaultMediaType);
            }
            IAMSProvider myAMShelp = AMSProviderFactory.CreateAMSProvider();

            string AssetId = BodyData.AssetId;
            try
            {
                string KeepWatermakedBlobs = System.Configuration.ConfigurationManager.AppSettings["KeepWatermakedBlobs"] ?? "false";

                if (!(KeepWatermakedBlobs != "false"))
                {
                    myAMShelp.DeleteWatermakedBlobRenders(AssetId);

                }
                else
                    log.Info($"Blob not deleted {AssetId}");
                return req.CreateResponse(HttpStatusCode.OK, new { result = "ok" }, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {

                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
           


            
        }

        [FunctionName("DeleteSucceededPods")]
        public static async Task<HttpResponseMessage> DeleteSucceededPods([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            string content = await req.Content.ReadAsStringAsync();
            var BodyData = content.GetBodyData<RequestData.JobRequest>();
            if (BodyData == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest, BodyData, JsonMediaTypeFormatter.DefaultMediaType);
            }

            IK8SClient k = K8SClientFactory.Create();
            string prefixName = $"allinone-job-{BodyData.JobId}";
            try
            {
                var r = await k.DeletePods(prefixName, "Succeeded");
                //var r = await k.DeletePods(JobId, prefixName, new List<string>());

                return req.CreateResponse(r.Code, r, JsonMediaTypeFormatter.DefaultMediaType);
            }
            catch (Exception X)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
        }
        [FunctionName("GetK8SProcessLog")]
        public static async Task<HttpResponseMessage> GetK8SProcessLog([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]HttpRequestMessage req, TraceWriter log, ExecutionContext context)
        {
            string JobId =  req.GetQueryNameValuePairs().FirstOrDefault(q => string.Compare(q.Key, "JobId", true) == 0).Value;
            if (string.IsNullOrEmpty(JobId))
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError, new { Error = "Parameter JobId is null" }, JsonMediaTypeFormatter.DefaultMediaType);
            }

            var K8S = K8SClientFactory.Create();
            try
            {
                string jobName = $"allinone-job-{JobId}";
                var ResultList = await K8S.GetK8SJobLog(jobName);
               
                if (ResultList.Count == 0)
                {
                    //Not Found
                    return req.CreateResponse(HttpStatusCode.NotFound, ResultList, JsonMediaTypeFormatter.DefaultMediaType);
                }
                else
                {
                    //LOG
                    return req.CreateResponse(HttpStatusCode.OK, ResultList, JsonMediaTypeFormatter.DefaultMediaType);
                }
            }
            catch (Exception X)
            {
                log.Error(X.Message);
                return req.CreateResponse(HttpStatusCode.InternalServerError, X, JsonMediaTypeFormatter.DefaultMediaType);
            }
           
        }

    }
}