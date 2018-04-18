// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.WindowsAzure.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.AMS
{
    public class AMSProviderFactory
    {
        public  static IAMSProvider CreateAMSProvider(CloudStorageAccount WaterMarkStorageAcc)
        {
            string TenantId = System.Configuration.ConfigurationManager.AppSettings["TenantId"];
            string ClientId = System.Configuration.ConfigurationManager.AppSettings["ClientId"];
            string ClientSecret = System.Configuration.ConfigurationManager.AppSettings["ClientSecret"];
            Uri AMSApiUri = new Uri( System.Configuration.ConfigurationManager.AppSettings["AMSApiUri"]);
            string AMSStorageConStr= System.Configuration.ConfigurationManager.AppSettings["AMSStorageConStr"];
            string PUBLISHWATERKEDCOPY= System.Configuration.ConfigurationManager.AppSettings["PUBLISHWATERKEDCOPY"] ?? "false";
            //SAS URL TTL
            int SASTTL = int.Parse(System.Configuration.ConfigurationManager.AppSettings["SASTTL"] ?? "24");
            return new AMSProvider(TenantId,ClientId,ClientSecret,AMSApiUri, WaterMarkStorageAcc, AMSStorageConStr, PUBLISHWATERKEDCOPY, SASTTL);
        }
    }
}
