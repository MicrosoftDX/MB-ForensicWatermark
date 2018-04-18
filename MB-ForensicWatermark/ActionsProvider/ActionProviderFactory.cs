// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.



namespace ActionsProvider
{
  
    public class ActionProviderFactory
    {
        public static IActionsProvider GetActionProvider(Microsoft.WindowsAzure.Storage.CloudStorageAccount WaterMarkStorageAcc)
        {
            int embeddedmessagecount = int.Parse(System.Configuration.ConfigurationManager.AppSettings["embeddedmessagecount"] ?? "10");
            if (embeddedmessagecount > 32)
            {
                embeddedmessagecount = 32;
            }
            return new ActionProvider(WaterMarkStorageAcc, embeddedmessagecount);
        }
    }
}
