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
        public string JobId { get; set; }
        public ExecutionStatus State { get; set; }
        public string Details { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? FinishTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public string[] EmbeddedCodeList { get; set; }
    }
    public class TJobStatus : TableEntity
    {
        public TJobStatus()
        { }

        public TJobStatus(JobStatus Data, string AssetId)
        {
            PartitionKey = AssetId;
            RowKey = Data.JobId;
            State = Data.State.ToString();
            Details = Data.Details;
            StartTime = Data.StartTime;
            FinishTime = Data.FinishTime;
            Duration = Data.Duration.ToString();
            EmbeddedCodeList =string.Join(";", Data.EmbeddedCodeList);
        }
        public string EmbeddedCodeList { get; set; }

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
                JobId = RowKey,
                State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                EmbeddedCodeList = EmbeddedCodeList.Split(';')
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
        public string EmbeddedCodeValue { get; set; }
        public ExecutionStatus State { get; set; }
        public string ParentAssetId { get; set; }
        public string AssetID { get; set; }
        public string Details { get; set; }
    }
    public class TWaterMarkedAssetInfo :TableEntity
    {
        public TWaterMarkedAssetInfo ()
        { }
        public TWaterMarkedAssetInfo (WaterMarkedAssetInfo Data, string ParentAssetId)
        {
            this.PartitionKey = ParentAssetId;
            this.State = Data.State.ToString();
            this.RowKey = Data.EmbeddedCodeValue;
            this.AssetID = Data.AssetID;
            this.Details = Data.Details;
            this.EmbeddedCode = Data.EmbeddedCodeValue;
        }
        public string Details { get; set; }
        public string EmbeddedCode { get; set; }
        public string State { get; set; }
        public string AssetID { get; set; }
        public WaterMarkedAssetInfo GetWaterMarkedAsssetInfo()
        {
            return new WaterMarkedAssetInfo()
            {
                ParentAssetId = PartitionKey,
                EmbeddedCodeValue = RowKey,
                State= (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                AssetID =AssetID,
                Details=Details
            };
        }
       
    }
    public class WaterMarkedRender
    {
        public string EmbeddedCodeValue { get; set; }
        public string ParentAssetId { get; set; }
         public string RenderName { get; set; }
        public string MP4URL{ get; set; }
        public string Details { get; set; }

        public ExecutionStatus State { get; set; }

        public WaterMarkedRender()
        { }
        public WaterMarkedRender(NotificationEmbedder textData,string MP4URL)
        {
            this.Details = $"[{textData.JobId}] {textData.JobOutput}"; 
            this.EmbeddedCodeValue = textData.EmbeddedCode;
            this.MP4URL = MP4URL;
            this.ParentAssetId = textData.AssetID;
            this.RenderName = textData.FileName;
            this.State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), textData.Status);
            
        }
    }
    public class TWaterMarkedRender:TableEntity
    {
        
        public string EmbeddedCodeValue { get; set; }
        public string State { get; set; }
        public string ParentAssetId { get; set; }
        public string MP4URL { get; set; }
        public string Details { get; set; }

        public TWaterMarkedRender()
        { }
        public  TWaterMarkedRender(WaterMarkedRender Data)
        {
            PartitionKey = $"{Data.ParentAssetId}-{Data.EmbeddedCodeValue}";
            RowKey = Data.RenderName;
            //Properties
            EmbeddedCodeValue = Data.EmbeddedCodeValue;
            State = Data.State.ToString();
            ParentAssetId = Data.ParentAssetId;
            MP4URL = Data.MP4URL;
            Details = Data.Details;
        }
        public WaterMarkedRender GetWaterMarkedRender()
        {
            return new WaterMarkedRender()
            {
                EmbeddedCodeValue=this.EmbeddedCodeValue,
                MP4URL=this.MP4URL,
                ParentAssetId=ParentAssetId,
                RenderName=RowKey,
                State= (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State),
                Details=Details
            }
            ;
        }
    }
    public class NotificationEmbedder
    {
        public string JobId { get; set; }
        public string AssetID { get; set; }
        public string FileName { get; set; }
        public string EmbeddedCode { get; set; }
        public string Status { get; set; }
        public string JobOutput { get; set; }

    }
    public class UnifiedProcessStatus
    {
        public AssetStatus AssetStatus { get; set; }
        public JobStatus JobStatus { get; set; }
        public List<WaterMarkedAssetInfo> EmbeddedCodesList { get; set; }
    }
}
