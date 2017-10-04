using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
        string CREATEJOBAPIPATH = "apis/batch/v1/namespaces/default/jobs";
        string PODAPIPATH = $"/api/v1/namespaces/default/pods";
        string K8SURLTOKEN = System.Configuration.ConfigurationManager.AppSettings["K8SURLTOKEN"];
        Uri BASSEADRESSAPI = new Uri(System.Configuration.ConfigurationManager.AppSettings["K8SURL"]);
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

        public async Task<K8SResult> GetK8SJobData(string jobName)
        {
            K8SResult r = new K8SResult();
            string path = $"/apis/batch/v1/namespaces/default/jobs/{jobName}";
            var rs = await CallK8SXXXAsync(K8SURLTOKEN, path, HttpMethod.Get);
            r.Code = rs.StatusCode;
            r.IsSuccessStatusCode = rs.IsSuccessStatusCode;
            if (rs.IsSuccessStatusCode)
            {
                r.Content = await rs.Content.ReadAsStringAsync();
            }
            else
            {
                r.Content = rs.ReasonPhrase;
            }
            return r;
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
