﻿//-----------------------------------------------------------------------------
// FILE:	    IngressProtocol.cs
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using YamlDotNet.Serialization;

using Neon.Common;
using Neon.IO;
using Neon.Net;

namespace Neon.Kube
{
    /// <summary>
    /// <para>
    /// Enumerates the network protocols supported by neonKUBE for ingress traffic.
    /// </para>
    /// <note>
    /// Kubernetes/Istio does not currently support protocols like UCP or ICMP.
    /// </note>
    /// </summary>
    public enum IngressProtocol
    {
        /// <summary>
        /// HTTP
        /// </summary>
        [EnumMember(Value = "http")]
        Http,

        /// <summary>
        /// HTTPS
        /// </summary>
        [EnumMember(Value = "https")]
        Https,

        /// <summary>
        /// TCP
        /// </summary>
        [EnumMember(Value = "tcp")]
        Tcp,

        /// <summary>
        /// UDP
        /// </summary>
        [EnumMember(Value = "udp")]
        Udp
    }
}
