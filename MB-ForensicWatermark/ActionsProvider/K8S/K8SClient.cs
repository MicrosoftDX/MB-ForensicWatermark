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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.K8S
{
    class K8SClient:IK8SClient
    {
        CloudStorageAccount _WatermarkStorageAccount;

        string CREATEJOBAPIPATH = "/apis/batch/v1/namespaces/default/jobs";
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
        private bool RemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            Trace.TraceInformation($"[SSL] bypass {certificate.Subject} {certificate.Issuer} {sslPolicyErrors}");
            return true;
        }
        private async Task<HttpResponseMessage> CallK8SXXXAsync(string myK8SToken, string pathPlusParameters, HttpMethod HttpVerb = HttpMethod.Get)
        {
            return await CallK8SXXXAsync(myK8SToken, pathPlusParameters, null, HttpVerb);
        }
        private async Task<HttpResponseMessage> CallK8SXXXAsync(string myK8SToken, string pathPlusParameters, HttpContent content, HttpMethod HttpVerb)
        {
            HttpResponseMessage rm = null;
            using (var clientHandler = new HttpClientHandler())
            {
                //Custome Certification callback
                clientHandler.ServerCertificateCustomValidationCallback += RemoteCertificateValidationCallback;
                using (var httpclient = new HttpClient(clientHandler))
                {
                    httpclient.BaseAddress = BASSEADRESSAPI;
                    httpclient.DefaultRequestHeaders.Accept.Clear();
                    httpclient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    httpclient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", myK8SToken);
                    switch (HttpVerb)
                    {
                        case HttpMethod.Post:
                            rm = await httpclient.PostAsync(pathPlusParameters, content);
                            break;
                        case HttpMethod.Get:
                            rm = await httpclient.GetAsync(pathPlusParameters);
                            break;
                        case HttpMethod.Delete:
                            rm = await httpclient.DeleteAsync(pathPlusParameters);
                            break;
                        default:
                            break;
                    }
                }
            }
            return rm;
        }
        public async Task<List<KeyValuePair<string, string>>> GetK8SJobLog(string JobId)
        {
            List<KeyValuePair<string,string>> jobLogData = new List<KeyValuePair<string, string>>();
            JArray podList = null;
            string errorMessage = null;
            var podListRS = await CallK8SXXXAsync(K8SURLTOKEN, $"{PODBYJOBIDPATH}{JobId}", HttpMethod.Get);
            if ((podListRS.IsSuccessStatusCode))
            {
                JObject jsonPodList = Newtonsoft.Json.Linq.JObject.Parse(await podListRS.Content.ReadAsStringAsync());
                podList = (JArray)jsonPodList["items"];
                foreach (var currentPod in podList)
                {
                    //Get LOGS
                    
                    JToken metadata = currentPod.Value<JToken>("metadata");
                    string selfLink = metadata.Value<string>("selfLink");
                    string podName = metadata.Value<string>("name");
                    var rs = await CallK8SXXXAsync(K8SURLTOKEN, $"{selfLink}/log", HttpMethod.Get);
                    if ((rs.IsSuccessStatusCode))
                    {
                        jobLogData.Add(new KeyValuePair<string, string>(podName, await rs.Content.ReadAsStringAsync()));
                    }
                    else
                    {
                        errorMessage = $"[{JobId}] GetK8SJobLog error {rs.StatusCode} {rs.ReasonPhrase}";
                    }
                }
            }
            else
            {
                errorMessage = $"[{JobId}] GetK8SJobLog error {podListRS.StatusCode} {podListRS.ReasonPhrase}";
            }
            if (!string.IsNullOrEmpty(errorMessage))
            {
                throw new Exception(errorMessage);
            }
            return jobLogData;
        }
        private void SaveBlobData(string Data, string BlobName)
        {
            CloudBlobClient blobClient = _WatermarkStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference("watermarktmp");
            container.CreateIfNotExists();
            CloudBlockBlob blockBlob = container.GetBlockBlobReference(BlobName);
            blockBlob.UploadText(Data);
        }
        public async Task<K8SResult> DeleteJobs(string JobId)
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
            var rs = await CallK8SXXXAsync(K8SURLTOKEN,CREATEJOBAPIPATH,yamalJob,HttpMethod.Post);
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
