﻿//-----------------------------------------------------------------------------
// FILE:	    GrpcGetSwitchRequest.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright © 2005-2022 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Net;

using ProtoBuf.Grpc;

namespace Neon.Kube.GrpcProto.Desktop
{
    /// <summary>
    /// Returns information about a specific switch.  This returns a <see cref="GrpcGetSwitchReply"/>.
    /// </summary>
    [DataContract]
    public class GrpcGetSwitchRequest
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public GrpcGetSwitchRequest()
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="switchName">Specifies the switch name.</param>
        public GrpcGetSwitchRequest(string switchName)
        {
            this.SwitchName = switchName;
        }

        /// <summary>
        /// Identifies the desired switch.
        /// </summary>
        [DataMember(Order = 1)]
        public string? SwitchName { get; set; }
    }
}
