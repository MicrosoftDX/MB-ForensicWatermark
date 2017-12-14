// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.AMS
{
    public class AMSProviderFactory
    {
        public  static IAMSProvider CreateAMSProvider()
        {
            string TenantId = System.Configuration.ConfigurationManager.AppSettings["TenantId"];
            string ClientId = System.Configuration.ConfigurationManager.AppSettings["ClientId"];
            string ClientSecret = System.Configuration.ConfigurationManager.AppSettings["ClientSecret"];
            Uri AMSApiUri = new Uri( System.Configuration.ConfigurationManager.AppSettings["AMSApiUri"]);
            string WatermarkedStorageConn = System.Configuration.ConfigurationManager.AppSettings["WatermarkedStorageConn"];
            string AMSStorageConStr= System.Configuration.ConfigurationManager.AppSettings["AMSStorageConStr"];
            string PUBLISHWATERKEDCOPY= System.Configuration.ConfigurationManager.AppSettings["PUBLISHWATERKEDCOPY"] ?? "false";
            //SAS URL TTL
            int SASTTL = int.Parse(System.Configuration.ConfigurationManager.AppSettings["SASTTL"] ?? "24");
            return new AMSProvider(TenantId,ClientId,ClientSecret,AMSApiUri, WatermarkedStorageConn, AMSStorageConStr, PUBLISHWATERKEDCOPY, SASTTL);
        }
    }
}
