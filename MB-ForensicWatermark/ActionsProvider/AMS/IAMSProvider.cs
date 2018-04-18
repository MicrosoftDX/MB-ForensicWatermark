// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace ActionsProvider.AMS
{
    public interface IAMSProvider
    {
        
        void DeleteAsset(string AssetId);
        Task<ManifestInfo> GetK8SJobManifestAsync(string AssetID, string JobID, List<string> codes);
        Task<List<WMAssetOutputMessage>> AddWatermarkedRendersFiletoAsset(string assetId, List<UnifiedResponse.WaterMarkedRender> Renders, string WMEmbedCode);
        Task<WMAssetOutputMessage> CreateEmptyWatermarkedAsset(string ProcessId,string SourceAssetId, string WMEmbedCode);
        Task<int> DeleteWatermakedBlobRendersAsync(string JobId, string AssetId);
        Task<WMAssetOutputMessage> GenerateManifest(string SourceAssetId,bool setAsPrimary=true);
    }
}
