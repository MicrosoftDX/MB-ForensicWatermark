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
        Task<ManifestInfo> GetK8SJobManifestAsync(string AssetID, string JobId, List<string> codes);
        Task<WMAssetOutputMessage> AddWatermarkedMediaFiletoAsset(string WatermarkedAssetId, string WMEmbedCode, string MMRKURL);
        Task<WMAssetOutputMessage> CreateEmptyWatermarkedAsset(string ProcessId,string SourceAssetId, string WMEmbedCode);
        void DeleteWatermakedBlobRenders(string AssetId);
        Task<WMAssetOutputMessage> GenerateManifest(string SourceAssetId,bool setAsPrimary=true);
    }
}
