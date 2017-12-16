// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.Entities
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ExecutionStatus
    {
        Finished, Error, Running,New,Aborted
    }
    public class TMMRKStatus: TableEntity
    {
        
        public string State { get; set; }
       
        public string FileName { get; set; }
        public string Details { get; set; }
        public string JobId { get; set; }
        public string FileURL { get; set; }

        public TMMRKStatus()
        { }
        public TMMRKStatus(MMRKStatus data)
        {
            this.PartitionKey = data.AssetId;
            //this.RowKey =$"[{data.JobId}]{data.FileName}";
            this.RowKey = $"[{data.JobId}]{data.FileName}";
            FileName = data.FileName;
            State = data.State.ToString();
            JobId = data.JobId;
            Details = data.Details;
            FileURL = data.FileURL;
        }

        public MMRKStatus GetMMRKStatus()
        {
            return new MMRKStatus
            {
                AssetId = PartitionKey,
                JobId = JobId,
                FileName = FileName,
                Details = Details,
                FileURL = FileURL,
                State = (ExecutionStatus)Enum.Parse(typeof(ExecutionStatus), this.State)
            };
        }
    }
    public class MMRKStatus
    {
        public string JobId { get; set; }
        public string AssetId { get; set; }
        public string FileName { get; set; }
        public ExecutionStatus State { get; set; }
        public string Details { get; set; }
        public string FileURL { get; set; }
    }
}
