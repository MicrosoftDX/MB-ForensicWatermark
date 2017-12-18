using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.K8S
{
    public interface IK8SClient
    {
        Task<List<KeyValuePair<string, string>>> GetK8SJobLog(string JobNamePrefix);
        Task<K8SResult> DeletePods(string JobName,string status = "All");
        Task<K8SResult> SubmiteK8SJob(HttpContent yamalJob);
    }
}
