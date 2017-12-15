// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.Entities
{

    public class VideoInformation
    {
        public string FileName { get; set; }
        public string MP4URL { get; set; }
        public string MMRKURL { get; set; }
        public string vbitrate { get; set; }
        public string gopsize { get; set; }
        public string videoFilter { get; set; }
    }

    public class MP4WatermarkedURL
    {
        public string FileName { get; set; }
        public string WaterMarkedMp4 { get; set; }
    }

    public class EmbeddedCode
    {
        public string Code { get; set; }
        public List<MP4WatermarkedURL> MP4WatermarkedURL { get; set; }
    }

    public class ManifestInfo
    {
        public string JobId { get; set; }
        public string AssetID { get; set; }
        public string PreprocessorNotificationQueue { get; set; }
        public string EmbedderNotificationQueue { get; set; }
        public List<VideoInformation> VideoInformation { get; set; }
        public List<EmbeddedCode> EmbeddedCodes { get; set; }
    }

   
}
