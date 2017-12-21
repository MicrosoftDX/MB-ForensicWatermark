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
      
        string CREATEJOBAPIPATH = "/apis/batch/v1/namespaces/default/jobs";
        string PODAPIPATH = "/api/v1/namespaces/default/pods";
        string PODLOGPATH = "/api/v1/namespaces/default/{0}/log";
        string K8SURLTOKEN;
        Uri BASSEADRESSAPI;
        public K8SClient(Uri apiUri,string token)
        {
            K8SURLTOKEN = token;
            BASSEADRESSAPI = apiUri;
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
        public async Task<K8SResult> DeletePods(string jobname, string status = "All")
        {
            List<string> deleteLog = new List<string>();
            K8SResult r = new K8SResult()
            {
                Code=HttpStatusCode.OK

            };
            
            var rs = await CallK8SXXXAsync(K8SURLTOKEN, PODAPIPATH, HttpMethod.Get);
            r.Code = rs.StatusCode;
            r.IsSuccessStatusCode = rs.IsSuccessStatusCode;
            if ((rs.IsSuccessStatusCode))
            {
                JObject j = Newtonsoft.Json.Linq.JObject.Parse(await rs.Content.ReadAsStringAsync());
                JArray items = (JArray)j["items"];
                foreach (var item in items)
                {
                    string selfLink = (string)item["metadata"]["selfLink"];
                  
                    if (((string)item["status"]["phase"] == status) || (status == "All"))
                    {
                        JObject labels= (JObject)item["metadata"]["labels"];
                        string cjobname = (string)labels["job-name"];
                        if (cjobname.IndexOf(jobname)>=0)
                        {
                            
                            //Delete
                            var subR = await CallK8SXXXAsync(K8SURLTOKEN, selfLink, HttpMethod.Delete);
                            if (!subR.IsSuccessStatusCode)
                            {
                                //throw new Exception($"{subR.StatusCode}:{subR.ReasonPhrase}");
                                deleteLog.Add($"POD {selfLink} Deleted faild: {subR.StatusCode}:{subR.ReasonPhrase}");
                            }
                            else
                            {
                                deleteLog.Add($"POD {selfLink} Deleted!");
                            }
                        }
                        
                    }
                }
            }
            r.Content = Newtonsoft.Json.JsonConvert.SerializeObject(deleteLog);
            return r;
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
