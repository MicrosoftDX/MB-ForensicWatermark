// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ActionsProvider.Entities
{
    public class WMAssetOutputMessage
    {
        public string WMAssetId { get; set; }
        public string MMRKURLAdded { get; set; }
        public string EmbedCode { get; set; }
        public string Status { get; set; }
        public string StatusMessage { get; set; }
    }
}
