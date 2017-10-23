// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using Microsoft.Azure.WebJobs.Host;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace ActionsProvider.AMS
{
    public interface IAMSProvider
    {
        
        void DeleteAsset(string AssetId);
        Task<ManifestInfo> GetK8SJobManifestAsync(string AssetID, string JobID, List<string> codes);
        Task<WMAssetOutputMessage> AddWatermarkedMediaFiletoAsset(string WatermarkedAssetId, string WMEmbedCode, string MMRKURL, TraceWriter log);
        Task<WMAssetOutputMessage> CreateEmptyWatermarkedAsset(string ProcessId,string SourceAssetId, string WMEmbedCode, TraceWriter log);
        void DeleteWatermakedBlobRenders(string AssetId, TraceWriter log);
        Task<WMAssetOutputMessage> GenerateManifest(string SourceAssetId,bool setAsPrimary=true);
    }
}
