// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// Author: chgeuer@microsoft.com github.com/chgeuer

namespace embedder
{
    using System;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;
    using System.Runtime.Serialization;

    public class EmbedderJobDTO
    {
        [JsonProperty(propertyName: "JobId")]
        public string JobId { get; set; }

        [JsonProperty(propertyName: "AssetId")]
        public string AssetId { get; set; }

        [JsonProperty(propertyName: "PreprocessorNotificationQueue")]
        public string PreprocessorNotificationQueue { get; set; }

        [JsonProperty(propertyName: "EmbedderNotificationQueue")]
        public string EmbedderNotificationQueue { get; set; }

        [JsonProperty(propertyName: "VideoInformation")]
        public VideoInformation[] PreprocessorItems { get; set; }

        [JsonProperty(propertyName: "EmbeddedCodes")]
        public EmbedderJobs[] EmbedderJobs { get; set; }
    }

    public class DoNotUseAnIntAsStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) { throw new NotImplementedException(); }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) { return int.Parse((string)reader.Value); }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) { writer.WriteValue((string)value.ToString()); }
    }

    public class VideoInformation
    {
        [JsonProperty(propertyName: "FileName")]
        public string FileName { get; set; } 

        [JsonProperty(propertyName: "MP4URL")]
        public string VideoURL { get; set; }

        [JsonProperty(propertyName: "MMRKURL")]
        public string MmrkUrl { get; set; }

        [JsonProperty(propertyName: "vbitrate")]
        [JsonConverter(typeof(DoNotUseAnIntAsStringConverter))]
        public int VideoBitrate { get; set; }

        [JsonProperty(propertyName: "gopsize", ItemConverterType = typeof(DoNotUseAnIntAsStringConverter))]
        [JsonConverter(typeof(DoNotUseAnIntAsStringConverter))]
        public int GOPSize { get; set; }

        [JsonProperty(propertyName: "videoFilter")]
        public string VideoFilter { get; set; }
    }

    public class EmbedderJobs
    {
        [JsonProperty(propertyName: "EmbeddedCode")]
        public string UserId { get; set; }

        [JsonProperty(propertyName: "MP4WatermarkedURL")]
        public EmbedderItems[] EmbedderItems { get; set; }
    }

    public class EmbedderItems
    {
        [JsonProperty(propertyName: "FileName")]
        public string FileName { get; set; }

        [JsonProperty(propertyName: "WaterMarkedMp4")]
        public string WaterMarkedMp4 { get; set; } // "https://warnerbrothersprocess.blob.core.windows.net/watermarked/nb%3Acid%3AUUID%3A5d3a55ac-b93d-43ae-b74c-280c05af63e6-0X0001%2FChile%20Travel%20Promotional%20Video72_480x272_352.mp4?sr=b&sv=2015-12-11&st=2017-06-28T18%3A07%3A50Z&se=2018-06-28T19%3A07%3A00Z&sp=racwd&spr=https&sig=Z0ecOMnaAfZlMdcEUvpAqFMLt55LlJ7Xsg0FBUlhw9o%3D"
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum JobStatus
    {
        Finished = 0, 
        Error = 1
    }

    public interface INotificationMessage { }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum PreprocessorStage
    {
        [EnumMember(Value = "Download plaintext MP4")]
        DownloadMP4 = 0,

        [EnumMember(Value = "Preprocessor Pass #1")]
        Pass1 = 1,

        [EnumMember(Value = "Preprocessor Pass #2")]
        Pass2 = 2,

        [EnumMember(Value = "Upload MMRK file")]
        UploadMMRK = 3
    }

    public class NotificationPreprocessor : INotificationMessage
    {
        [JsonProperty(propertyName: "JobId")]
        public string JobId { get; set; }

        [JsonProperty(propertyName: "AssetId")]
        public string AssetId { get; set; }

        [JsonProperty(propertyName: "FileName")]
        public string FileName { get; set; }

        [JsonProperty(propertyName: "Status")]
        public JobStatus Status { get; set; }

        [JsonProperty(propertyName: "JobOutput")]
        public string JobOutput { get; set; }

        [JsonProperty(propertyName: "Stage")]
        public PreprocessorStage Stage { get; set; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum EmbedderStage
    {
        [EnumMember(Value = "Download MMRK")]
        DownloadMMRK = 4,

        [EnumMember(Value = "Upload watermarked MP4")]
        UploadMP4 = 5
    }

    public class NotificationEmbedder : INotificationMessage
    {
        [JsonProperty(propertyName: "JobId")]
        public string JobId { get; set; }

        [JsonProperty(propertyName: "AssetId")]
        public string AssetId { get; set; }

        [JsonProperty(propertyName: "FileName")]
        public string FileName { get; set; }

        [JsonProperty(propertyName: "EmbeddedCode")]
        public string UserId { get; set; }

        [JsonProperty(propertyName: "Status")]
        public JobStatus Status { get; set; }

        [JsonProperty(propertyName: "JobOutput")]
        public string JobOutput { get; set; }

        [JsonProperty(propertyName: "Stage")]
        public EmbedderStage Stage { get; set; }
    }
}