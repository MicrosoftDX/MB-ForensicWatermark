// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider
{
    public class ActionProviderFactory
    {
       public static IActionsProvider GetActionProvider()
        {
            string Storageconn = System.Configuration.ConfigurationManager.AppSettings["Storageconn"];
            return new ActionProvider(Storageconn);
        }
    }
}
