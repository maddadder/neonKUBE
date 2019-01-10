﻿//-----------------------------------------------------------------------------
// FILE:	    DnsAnswer.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Net;
using System.Net;

namespace Neon.Kube
{
    /// <summary>
    /// Holds information about a DNS lookup persisted to Consul as part of
    /// the neonHIVE Local DNS implementation.  These records hold the
    /// answers generated by the <b>neon-dns-mon</b> service.
    /// </summary>
    public class DnsAnswer
    {
        private string  hostname;

        /// <summary>
        /// The target hostname.
        /// </summary>
        [JsonProperty(PropertyName = "Hostname", Required = Required.Always)]
        public string Hostname
        {
            get { return hostname; }

            set
            {
                Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(value));

                hostname = value.ToLowerInvariant();
            }
        }

        /// <summary>
        /// The current IP address for the target.
        /// </summary>
        [JsonProperty(PropertyName = "Address", Required = Required.Always)]
        public IPAddress Address { get; set; }
    }
}
