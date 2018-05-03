﻿//-----------------------------------------------------------------------------
// FILE:	    ServiceFile.cs
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
    /// Configuration information for a Docker secret or config that
    /// is mapped into a service container.
    /// </summary>
    public class ServiceFile : INormalizable
    {
        /// <summary>
        /// Path to the target file within the container.
        /// </summary>
        [JsonProperty(PropertyName = "Name", Required = Required.Always)]
        public string Name { get; set; }

        /// <summary>
        /// Identifies the user that owns the file.
        /// </summary>
        [JsonProperty(PropertyName = "UID", Required = Required.Always)]
        public string UID { get; set; }

        /// <summary>
        /// Identifies the group that owns the file.
        /// </summary>
        [JsonProperty(PropertyName = "GID", Required = Required.Always)]
        public string GID { get; set; }

        /// <summary>
        /// The Linux file mode for the file.
        /// </summary>
        [JsonProperty(PropertyName = "Mode", Required = Required.Always)]
        public int Mode { get; set; }

        /// <inheritdoc/>
        public void Normalize()
        {
        }
    }
}
