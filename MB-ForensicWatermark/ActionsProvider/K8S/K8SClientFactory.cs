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
            string K8SURLTOKEN = System.Configuration.ConfigurationManager.AppSettings["K8SURLTOKEN"];
            if (string.IsNullOrEmpty(K8SURLTOKEN))
                throw new Exception($"Configuration Error K8SURLTOKEN is mising");
            Uri BASSEADRESSAPI = null;
            try
            {
                BASSEADRESSAPI = new Uri(System.Configuration.ConfigurationManager.AppSettings["K8SURL"]);
            }
            catch (Exception X)
            {

                throw new Exception($"Configuration Error BASSEADRESSAPI: {X.Message}");
            }
            string WatermarkStorageAccountStr = System.Configuration.ConfigurationManager.AppSettings["Storageconn"];

            return new K8SClient(BASSEADRESSAPI, K8SURLTOKEN, WatermarkStorageAccountStr);
        }
    }
}
