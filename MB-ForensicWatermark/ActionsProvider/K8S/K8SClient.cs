using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.K8S
{
    class K8SClient:IK8SClient
    {
        CloudStorageAccount _WatermarkStorageAccount;

        string CREATEJOBAPIPATH = "/apis/batch/v1/namespaces/default/jobs";
        string PODAPIPATH = "/api/v1/namespaces/default/pods";
        string PODLOGPATH = "/api/v1/namespaces/default/{0}/log";
        string PODBYJOBIDPATH = "/api/v1/namespaces/default/pods?labelSelector=jobid%3Djobid-";
        string JOBBYJOBIDPATH = "/apis/batch/v1/namespaces/default/jobs?labelSelector=jobid%3Djobid-";
        string K8SURLTOKEN;
        Uri BASSEADRESSAPI;
        public K8SClient(Uri apiUri,string token, string strConnWatermarkSt)
        {
            K8SURLTOKEN = token;
            BASSEADRESSAPI = apiUri;
            _WatermarkStorageAccount =  CloudStorageAccount.Parse(strConnWatermarkSt);
        }
        private async Task<HttpResponseMessage> CallK8SPostAsync(HttpContent ymal, string K8SURLTOKEN, string path)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate
            {

                return true;
            };
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
                        | SecurityProtocolType.Tls11
                        | SecurityProtocolType.Tls12
                        | SecurityProtocolType.Ssl3;
            HttpClient client = new HttpClient
            {

                BaseAddress = BASSEADRESSAPI
            };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", K8SURLTOKEN);
            return await client.PostAsync(path, ymal);
        }
        private async Task<HttpResponseMessage> CallK8SXXXAsync(string K8SURLTOKEN, string pathPlusParameters, HttpMethod HttpVerb = HttpMethod.Get)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate
            {
                return true;
            };
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls
                        | SecurityProtocolType.Tls11
                        | SecurityProtocolType.Tls12
                        | SecurityProtocolType.Ssl3;
            HttpClient client = new HttpClient
            {

                BaseAddress = BASSEADRESSAPI
            };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", K8SURLTOKEN);
            HttpResponseMessage rm = null;
            switch (HttpVerb)
            {
                case HttpMethod.Post:
                    break;
                case HttpMethod.Get:
                    rm = await client.GetAsync(pathPlusParameters);
                    break;
                case HttpMethod.Delete:
                    rm = await client.DeleteAsync(pathPlusParameters);
                    break;
                default:
                    break;
            }


            return rm;
        }
        public async Task<List<KeyValuePair<string, string>>> GetK8SJobLog(string JobNamePrefix)
        {
            List<KeyValuePair<string,string>> jobLogData = new List<KeyValuePair<string, string>>();
           
            var podList = await GetK8SItemList(PODAPIPATH);
            foreach (var pod in podList)
            {
                JToken metadata = pod.Value<JToken>("metadata");
                string podName = metadata.Value<string>("name");
                if (podName.IndexOf(JobNamePrefix) == 0)
                {
                    //my pod
                    string selfLink = metadata.Value<string>("selfLink");
                    var rs = await CallK8SXXXAsync(K8SURLTOKEN, $"{selfLink}/log", HttpMethod.Get);
                    if ((rs.IsSuccessStatusCode))
                    {
                        jobLogData.Add(new KeyValuePair<string, string>(podName, await rs.Content.ReadAsStringAsync()));
                    }
                    else
                    {
                        Trace.TraceError($"GetK8SJobLog error {rs.StatusCode} {rs.ReasonPhrase}");
                    }
                }
            }
            return jobLogData;
        }
        private async Task<JArray> GetK8SItemList(string path)
        {
            JArray jobs = null;
            var rs = await CallK8SXXXAsync(K8SURLTOKEN, path, HttpMethod.Get);
            if (rs.IsSuccessStatusCode)
            {
                JObject j = Newtonsoft.Json.Linq.JObject.Parse(await rs.Content.ReadAsStringAsync());
                jobs = (JArray)j["items"];
            }
            else
            {
                throw new Exception($"Error reading JOB List {rs.ReasonPhrase}");
            }
            return jobs;
        }
        private async Task<bool> SavePodLog(string JobId,string PodName)
        {
            string myPodLog = string.Format(PODLOGPATH, PodName);
            var logR = await CallK8SXXXAsync(K8SURLTOKEN, PodName, HttpMethod.Get);
            if (logR.IsSuccessStatusCode)
            {
                string  log = await logR.Content.ReadAsStringAsync();
            }
            else
            {
                throw new Exception($"Error reading POD List {logR.ReasonPhrase}");
            }
            return logR.IsSuccessStatusCode;
        }
        public async Task<K8SResult> DeletePods(string JobId,string jobName, List<string> StatusFilter )
        {
            List<string> deleteLog = new List<string>();
            K8SResult resultStatus = new K8SResult()
            {
                Code = HttpStatusCode.OK
            };
            try
            {
                //Get pod list
                JArray podList = await GetK8SItemList(PODAPIPATH); 
                if (podList != null)
                {
                    foreach (var currentPod in podList)
                    {
                        string jobStatus = (string)currentPod["status"]["phase"];
                        //Filter by Status, not present
                        if (!StatusFilter.Contains(jobStatus))
                        {
                            string selfLink = (string)currentPod["metadata"]["selfLink"];
                            JObject labels = (JObject)currentPod["metadata"]["labels"];
                            string cjobname = (string)labels["job-name"];
                            string podName=(string)currentPod["metadata"]["name"];
                            if (cjobname.IndexOf(jobName) >= 0)
                            {
                                //1. Save POD LOGS by Job Name
                                await SavePodLog(JobId, podName);
                                //2. Delete
                                var subR = await CallK8SXXXAsync(K8SURLTOKEN, selfLink, HttpMethod.Delete);
                                if (!subR.IsSuccessStatusCode)
                                {
                                    deleteLog.Add($"POD {selfLink} Deleted faild: {subR.StatusCode}:{subR.ReasonPhrase}");
                                }
                                else
                                {
                                    deleteLog.Add($"POD {selfLink} Deleted!");
                                }
                            }

                        }
                    }
                    resultStatus.Content = Newtonsoft.Json.JsonConvert.SerializeObject(deleteLog);
                }
            }
            catch (Exception X)
            {
                resultStatus.Code = HttpStatusCode.InternalServerError;
                resultStatus.IsSuccessStatusCode = false;
                resultStatus.Content = X.Message;
            }
           
            return resultStatus;
        }
        private void SaveBlobData(string Data, string BlobName)
        {
            CloudBlobClient blobClient = _WatermarkStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("watermarktmp");
            container.CreateIfNotExists();
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(BlobName);
            blockBlob.UploadText(Data);
        }
        public async Task<K8SResult> DeletePods(string JobId)
        {
            List<string> deleteLog = new List<string>();
            K8SResult masterResult = new K8SResult()
            {
                Code=HttpStatusCode.OK
            };
            JArray podList = null;
            //1. List PODS 
            var podListRS = await CallK8SXXXAsync(K8SURLTOKEN, $"{PODBYJOBIDPATH}{JobId}", HttpMethod.Get);
            masterResult.Code = podListRS.StatusCode;
            masterResult.IsSuccessStatusCode = podListRS.IsSuccessStatusCode;
            if ((podListRS.IsSuccessStatusCode))
            {
                JObject jsonPodList = Newtonsoft.Json.Linq.JObject.Parse(await podListRS.Content.ReadAsStringAsync());
                podList = (JArray)jsonPodList["items"];
                foreach (var currentPod in podList)
                {
                    string podName= (string)currentPod["metadata"]["name"];
                    string selfLink = (string)currentPod["metadata"]["selfLink"];
                    //2. Save information
                    var GetPodLog = await CallK8SXXXAsync(K8SURLTOKEN, $"{selfLink}/log", HttpMethod.Get);
                    masterResult.Code = GetPodLog.StatusCode;
                    masterResult.IsSuccessStatusCode = GetPodLog.IsSuccessStatusCode;
                    if (GetPodLog.IsSuccessStatusCode)
                    {
                        string log = await GetPodLog.Content.ReadAsStringAsync();
                        SaveBlobData(log, $"{JobId}.{podName}.log");
                    }
                    else
                    {
                        //Error to read POD information
                        masterResult.Code = HttpStatusCode.InternalServerError;
                        masterResult.IsSuccessStatusCode = false;
                        Trace.TraceError($"[{JobId}]Error to POD information : {GetPodLog.ReasonPhrase}");
                        deleteLog.Add($"[{JobId}]Error to POD information : {GetPodLog.ReasonPhrase}");
                    }
                }

                //3. Delete Jobs
                if (masterResult.Code==HttpStatusCode.OK)
                {
                    var jobLisRS = await CallK8SXXXAsync(K8SURLTOKEN, $"{JOBBYJOBIDPATH}{JobId}", HttpMethod.Get);
                    masterResult.Code = jobLisRS.StatusCode;
                    masterResult.IsSuccessStatusCode = jobLisRS.IsSuccessStatusCode;
                    if (jobLisRS.IsSuccessStatusCode)
                    {
                        JObject joblistraw = Newtonsoft.Json.Linq.JObject.Parse(await jobLisRS.Content.ReadAsStringAsync());
                        JArray joblist = (JArray)joblistraw["items"];
                        foreach (var job in joblist)
                        {
                            string jobName= (string)job["metadata"]["name"];
                            string jobSelfLink = (string)job["metadata"]["selfLink"];
                            //Delete
                            var deleteRS = await CallK8SXXXAsync(K8SURLTOKEN, jobSelfLink, HttpMethod.Delete);
                            if (!deleteRS.IsSuccessStatusCode)
                            {
                                masterResult.Code = HttpStatusCode.InternalServerError;
                                masterResult.IsSuccessStatusCode = false;
                                deleteLog.Add($"JOB {jobSelfLink} Deleted faild: {deleteRS.StatusCode}:{deleteRS.ReasonPhrase}");
                                Trace.TraceError($"JOB {jobSelfLink} Deleted faild: {deleteRS.StatusCode}:{deleteRS.ReasonPhrase}");
                            }
                            else
                            {
                                deleteLog.Add($"[{JobId}] Deleted JOB {jobSelfLink}");
                                //DELETE POD
                                foreach (var pod2Delete in podList)
                                {
                                    JObject labels = (JObject)pod2Delete["metadata"]["labels"];
                                    string cjobname = (string)labels["job-name"];
                                    if (cjobname.IndexOf(jobName) >= 0)
                                    {
                                        var deletePodRS = await CallK8SXXXAsync(K8SURLTOKEN, (string)pod2Delete["metadata"]["selfLink"], HttpMethod.Delete);
                                        if (!deletePodRS.IsSuccessStatusCode)
                                        {
                                            masterResult.Code = HttpStatusCode.InternalServerError;
                                            masterResult.IsSuccessStatusCode = false;
                                            deleteLog.Add($"JOB {(string)pod2Delete["metadata"]["selfLink"]} Deleted POD faild: {deleteRS.StatusCode}:{deleteRS.ReasonPhrase}");
                                            Trace.TraceError($"JOB {(string)pod2Delete["metadata"]["selfLink"]} Deleted POD faild: {deleteRS.StatusCode}:{deleteRS.ReasonPhrase}");
                                        }
                                        else
                                        {
                                            deleteLog.Add($"[{JobId}] Deleted POD {(string)pod2Delete["metadata"]["selfLink"]}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Error to read JOB LIST information
                        masterResult.Code = HttpStatusCode.InternalServerError;
                        masterResult.IsSuccessStatusCode = false;
                        Trace.TraceError($"[{JobId}]Error reding job list : {jobLisRS.ReasonPhrase}");
                    }
                }
            }
            else
            {
                //Error to read POD information
                masterResult.Code = HttpStatusCode.InternalServerError;
                masterResult.IsSuccessStatusCode = false;
                deleteLog.Add($"[{JobId}]Error to read POD list : {podListRS.ReasonPhrase}");
                Trace.TraceError($"[{JobId}]Error to read POD list : {podListRS.ReasonPhrase}");
            }
        
            masterResult.Content = Newtonsoft.Json.JsonConvert.SerializeObject(deleteLog);
            return masterResult;
        }
        public async Task<K8SResult> SubmiteK8SJob(HttpContent yamalJob)
        {
            K8SResult r = new K8SResult();
            var rs = await CallK8SPostAsync(yamalJob, K8SURLTOKEN, CREATEJOBAPIPATH);
            r.IsSuccessStatusCode = rs.IsSuccessStatusCode;
            if (rs.IsSuccessStatusCode)
            {
                //TODO: Check Job is really running
                r.Content = await rs.Content.ReadAsStringAsync();
            }
            else
            {
                r.Content = rs.ReasonPhrase;
            }
            return r;
        }
    }
}
