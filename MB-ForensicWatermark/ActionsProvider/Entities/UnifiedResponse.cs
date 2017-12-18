// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ActionsProvider.Entities;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.UnifiedResponse
{

    public class AssetStatus
    {
        public string AssetId { get; set; }
        public ExecutionStatus State { get; set; }
    }
    public class TAssetStatus : TableEntity
    {
       
        public string State { get; set; }

        public TAssetStatus(AssetStatus data)
        {
            this.PartitionKey = "Flags";
            this.RowKey = data.AssetId;
            this.State = data.State.ToString();
        }
        public TAssetStatus()
        { }

        public AssetStatus GetAssetStatus()
        {
            return new AssetStatus() {
                AssetId = this.RowKey,
                State= (ExecutionStatus) Enum.Parse(typeof(ExecutionStatus), this.State)
            };
        }
    }
    public class JobStatus
    {
        public string JobID { get; set; }
        public ExecutionStatus State { get; set; }
        public string Details { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? FinishTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public string[] EmbebedCodeList { get; set; }
    }
    public class TJobStatus : TableEntity
    {
        public TJobStatus()
        { }

        public TJobStatus(JobStatus Data, string AssetID)
        {
            PartitionKey = AssetID;
            RowKey = Data.JobID;
            State = Data.State.ToString();
            Details = Data.Details;
            StartTime = Data.StartTime;
            FinishTime = Data.FinishTime;
            Duration = Data.Duration.ToString();
            EmbebedCodeList =string.Join(";", Data.EmbebedCodeList);
        }
        public string EmbebedCodeList { get; set; }

        public string State { get; set; }
        public string Details { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? FinishTime { get; set; }
        public string Duration { get; set; }

        public JobStatus GetJobStatus()
        {
            var X= new JobStatus()
            {
                Details = Details,
                //Duration = TimeSpan.Parse( Duration.ToString() ?? "",
                FinishTime = FinishTime,
                StartTime = StartTime,
                JobID = RowKey,
                State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                EmbebedCodeList = EmbebedCodeList.Split(';')
            };
            if (!string.IsNullOrEmpty( Duration))
            {
                X.Duration = TimeSpan.Parse(Duration);
            }
            return X;
        }
    }
    public class WaterMarkedAssetInfo
    {
        public string EmbebedCodeValue { get; set; }
        public ExecutionStatus State { get; set; }
        public string ParentAssetID { get; set; }
        public string AssetID { get; set; }
        public string Details { get; set; }
    }
    public class TWaterMarkedAssetInfo :TableEntity
    {
        public TWaterMarkedAssetInfo ()
        { }
        public TWaterMarkedAssetInfo (WaterMarkedAssetInfo Data, string ParentAssetID)
        {
            this.PartitionKey = ParentAssetID;
            this.State = Data.State.ToString();
            this.RowKey = Data.EmbebedCodeValue;
            this.AssetID = Data.AssetID;
            this.Details = Data.Details;
            this.EmbebedCode = Data.EmbebedCodeValue;
        }
        public string Details { get; set; }
        public string EmbebedCode { get; set; }
        public string State { get; set; }
        public string AssetID { get; set; }
        public WaterMarkedAssetInfo GetWaterMarkedAsssetInfo()
        {
            return new WaterMarkedAssetInfo()
            {
                ParentAssetID = PartitionKey,
                EmbebedCodeValue = RowKey,
                State= (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                AssetID =AssetID,
                Details=Details
            };
        }
       
    }
    public class WaterMarkedRender
    {
        public string EmbebedCodeValue { get; set; }
        public string ParentAssetID { get; set; }
         public string RenderName { get; set; }
        public string MP4URL{ get; set; }
        public string Details { get; set; }

        public ExecutionStatus State { get; set; }

        public WaterMarkedRender()
        { }
        public WaterMarkedRender(NotificationEmbedder textData,string MP4URL)
        {
            this.Details = $"[{textData.JobID}] {textData.JobOutput}"; 
            this.EmbebedCodeValue = textData.EmbebedCode;
            this.MP4URL = MP4URL;
            this.ParentAssetID = textData.AssetID;
            this.RenderName = textData.FileName;
            this.State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), textData.Status);
            
        }
    }
    public class TWaterMarkedRender:TableEntity
    {
        
        public string EmbebedCodeValue { get; set; }
        public string State { get; set; }
        public string ParentAssetID { get; set; }
        public string MP4URL { get; set; }
        public string Details { get; set; }

        public TWaterMarkedRender()
        { }
        public  TWaterMarkedRender(WaterMarkedRender Data)
        {
            PartitionKey = $"{Data.ParentAssetID}-{Data.EmbebedCodeValue}";
            RowKey = Data.RenderName;
            //Properties
            EmbebedCodeValue = Data.EmbebedCodeValue;
            State = Data.State.ToString();
            ParentAssetID = Data.ParentAssetID;
            MP4URL = Data.MP4URL;
            Details = Data.Details;
        }
        public WaterMarkedRender GetWaterMarkedRender()
        {
            return new WaterMarkedRender()
            {
                EmbebedCodeValue=this.EmbebedCodeValue,
                MP4URL=this.MP4URL,
                ParentAssetID=ParentAssetID,
                RenderName=RowKey,
                State= (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                Details=Details
            }
            ;
        }
    }
    public class NotificationEmbedder
    {
        public string JobID { get; set; }
        public string AssetID { get; set; }
        public string FileName { get; set; }
        public string EmbebedCode { get; set; }
        public string Status { get; set; }
        public string JobOutput { get; set; }

    }
    public class UnifiedProcessStatus
    {
        public AssetStatus AssetStatus { get; set; }
        public JobStatus JobStatus { get; set; }
        public List<WaterMarkedAssetInfo> EmbebedCodesList { get; set; }
    }
}
