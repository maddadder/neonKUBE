﻿//-----------------------------------------------------------------------------
// FILE:	    ServiceConfig.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Neon.Docker
{
    /// <summary>
    /// Service config.
    /// </summary>
    public class ServiceConfig : INormalizable
    {
        /// <summary>
        /// The Docker secret ID.
        /// </summary>
        [JsonProperty(PropertyName = "ConfigID", Required = Required.Always)]
        public string ConfigId { get; set; }

        /// <summary>
        /// The secret name.
        /// </summary>
        [JsonProperty(PropertyName = "ConfigName", Required = Required.Always)]
        public string ConfigName { get; set; }

        /// <summary>
        /// Secret file information.
        /// </summary>
        [JsonProperty(PropertyName = "File", Required = Required.Always)]
        public ServiceFile File { get; set; }

        /// <inheritdoc/>
        public void Normalize()
        {
            File?.Normalize();
        }
    }
}
