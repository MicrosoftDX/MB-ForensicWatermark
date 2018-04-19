// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using ActionsProvider.K8S;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace ActionsProvider
{
    public interface IActionsProvider
    {
        Task<MMRKStatus> UpdateMMRKStatus(MMRKStatus mmrkStatus);
        Task<UnifiedResponse.UnifiedProcessStatus> StartNewProcess(string AssetId, string JobId, string[] EmbebedCodeList);
        Task<MMRKStatus> GetMMRKStatus(string AsssetId, string JobRender);
        List<MMRKStatus> GetMMRKStatusList(string AssetID);
        UnifiedResponse.AssetStatus GetAssetStatus(string AssetId);
        Task<int> EvalPreprocessorNotifications(string JobId);
        Task<int> EvalPEmbeddedNotifications(string JobId);
        UnifiedResponse.AssetStatus EvalAssetStatus(string AssetId);
        Task<UnifiedResponse.WaterMarkedRender> UpdateWaterMarkedRender(UnifiedResponse.WaterMarkedRender renderData);
        UnifiedResponse.WaterMarkedRender GetWaterMarkedRender(string ParentAssetID, string EmbebedCodeValue, string RenderName);
        List<UnifiedResponse.WaterMarkedRender> GetWaterMarkedRenders(string ParentAssetID, string EmbebedCodeValue);
        UnifiedResponse.WaterMarkedAssetInfo EvalWaterMarkedAssetInfo(string ParentAssetID, string EmbebedCodeValue);
        Task UpdateUnifiedProcessStatus(UnifiedResponse.UnifiedProcessStatus curretnData);
        UnifiedResponse.UnifiedProcessStatus GetUnifiedProcessStatus(string AssetId, string JobID);
        Task<K8SResult> SubmiteJobK8S(ManifestInfo manifest, int subId);
        Task<UnifiedResponse.UnifiedProcessStatus> UpdateJob(UnifiedResponse.UnifiedProcessStatus currentData, ExecutionStatus AssetState, ExecutionStatus JobState, string JobStateDetails, ExecutionStatus watermarkState, string WaterMarkCopiesStatusDetails);
        List<ManifestInfo> GetK8SManifestInfo(int aggregationLevel, int aggregationLevelOnlyEmb, ManifestInfo manifest);
        Task<int> DeleteWatermarkedRenderTmpInfo(List<UnifiedResponse.WaterMarkedRender> WatermarkedRenders);
        void UpdateWaterMarkedAssetInfo(UnifiedResponse.WaterMarkedAssetInfo data, string ParentAssetId);
        Task UpdateWaterMarkedRender(List<UnifiedResponse.WaterMarkedRender> renderList);
        Task<bool> GetAssetProcessLock(string AssetId, string JobID, TimeSpan timeOut, TimeSpan delay);
        Task ReleaseAssetProcessLock(string AssetId, string JobID);
    }
}
