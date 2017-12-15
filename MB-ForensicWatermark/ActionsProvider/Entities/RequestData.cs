using ActionsProvider.UnifiedResponse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.Entities
{
    public class RequestData
    {
        public class AssetRequest
        {
            public string AssetId { get; set; }
        }
        public class JobRequest
        {
            public string JobId { get; set; }
        }
        public class CheckAssetStatus: AssetRequest{ }
        public class BaseStatusData
        {
            public string AssetId { get; set; }
            public string JobId { get; set; }
        }
        public class StartNewJob: BaseStatusData
        {
            public string[] EmbeddedCodes { get; set; }
        }
        public class GetPreprocessorJobData: BaseStatusData
        {
            public List<string> Codes { get; set; }
        }
        public class UpdateWaterMarkCode
        {
            public EmbeddedCode EmbeddedCode { get; set; }
            public string ParentAssetId { get; set; }
        }
        public class UpdateJob
        {
            public UnifiedProcessStatus Manifest { get; set; }
            public ExecutionStatus AssetStatus { get; set; }
            public ExecutionStatus JobState { get; set; }
            public string JobStateDetails { get; set; }
            public string WaterMarkCopiesStatusDetails { get; set; }
            public ExecutionStatus WaterMarkCopiesStatus { get; set; }
        }
        public class GetUnifiedProcessStatus : BaseStatusData { }

    }
}
