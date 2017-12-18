// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.



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
