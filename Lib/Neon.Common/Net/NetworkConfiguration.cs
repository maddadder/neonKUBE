﻿//-----------------------------------------------------------------------------
// FILE:	    NetworkConfiguration.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Retry;

namespace Neon.Net
{
    /// <summary>
    /// Retured by <see cref="NetHelper.GetConfiguration()"/> with the current network
    /// settings including: Routable IP address, network CIDR, network gateway and the
    /// DNS server IP addresses.
    /// </summary>
    public class NetworkConfiguration
    {
        /// <summary>
        /// The routable IP address of the current machine.
        /// </summary>
        public IPAddress Address { get; set; }

        /// <summary>
        /// The CIDR for the local network.
        /// </summary>
        public NetworkCidr Cidr { get; set; }

        /// <summary>
        /// The IP address of the local network gateway.
        /// </summary>
        public IPAddress Gateway { get; set; }

        /// <summary>
        /// The IP address of the local network's DNS servers.
        /// </summary>
        public IPAddress[] DnsServers { get; set; }
    }
}
