// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using ActionsProvider.K8S;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider
{
    public interface IActionsProvider
    {

        MMRKStatus UpdateMMRKStatus(MMRKStatus mmrkStatus);
        UnifiedResponse.UnifiedProcessStatus StartNewProcess(string AssetId, string JobId, string[] EmbeddedCodeList);
        MMRKStatus GetMMRKStatus(string AsssetId, string JobRender);
        List<MMRKStatus> GetMMRKStatusList(string AssetID);
        UnifiedResponse.AssetStatus GetAssetStatus(string AssetId);
        Task<int> EvalPreprocessorNotifications();
        Task<int> EvalPEmbeddedNotifications();
        UnifiedResponse.AssetStatus EvalAssetStatus(string AssetId);
        UnifiedResponse.WaterMarkedRender UpdateWaterMarkedRender(UnifiedResponse.WaterMarkedRender renderData);
        UnifiedResponse.WaterMarkedRender GetWaterMarkedRender(string ParentAssetId, string EmbeddedCodeValue, string RenderName);
        List<UnifiedResponse.WaterMarkedRender> GetWaterMarkedRenders(string ParentAssetId, string EmbeddedCodeValue);
        UnifiedResponse.WaterMarkedAssetInfo EvalWaterMarkedAssetInfo(string ParentAssetId, string EmbeddedCodeValue);
        void UpdateUnifiedProcessStatus(UnifiedResponse.UnifiedProcessStatus curretnData);
        UnifiedResponse.UnifiedProcessStatus GetUnifiedProcessStatus(string AssetId, string JobId);
        Task<K8SResult> SubmiteJobK8S(ManifestInfo manifest, int subId);
        UnifiedResponse.UnifiedProcessStatus UpdateJob(UnifiedResponse.UnifiedProcessStatus currentData, ExecutionStatus AssetState, ExecutionStatus JobState, string JobStateDetails, ExecutionStatus watermarkState, string WaterMarkCopiesStatusDetails);
        List<ManifestInfo> GetK8SManifestInfo(int aggregationLevel, int aggregationLevelOnlyEmb, ManifestInfo manifest);

    }
}
