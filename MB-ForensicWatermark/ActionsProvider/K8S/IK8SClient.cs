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
        Task<K8SResult> GetK8SJobData(string jobName);
        Task<K8SResult> DeletePods(string JobName,string status = "All");
        Task<K8SResult> SubmiteK8SJob(HttpContent yamalJob);
    }
}
