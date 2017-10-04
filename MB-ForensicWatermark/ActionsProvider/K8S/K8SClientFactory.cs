using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.K8S
{
    public class K8SClientFactory
    {
        public static IK8SClient Create()
        {
            return new K8SClient();
        }
    }
}
