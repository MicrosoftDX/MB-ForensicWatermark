using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.K8S
{
    enum HttpMethod
    {
        Post, Get, Delete
    }
    public class K8SResult
    {
        public HttpStatusCode Code { get; set; }
        public string Content { get; set; }
        public bool IsSuccessStatusCode { get; set; }
    }
}
