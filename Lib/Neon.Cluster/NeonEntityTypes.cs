﻿//-----------------------------------------------------------------------------
// FILE:	    NeonEntityTypes.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2017 by NeonForge, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Data;

namespace Neon.Cluster
{
    /// <summary>
    /// Defines the <see cref="IEntity.Type"/> values for common Neon entities.
    /// </summary>
    public static class NeonEntityTypes
    {
        /// <summary>
        /// Maps to: <see cref="Neon.Cluster.CouchbaseSettings"/>.
        /// </summary>
        public const string CouchbaseSettings = "neon.couchbase-settings";

        /// <summary>
        /// Maps to: <see cref="Neon.Cluster.RabbitMQSettings"/>.
        /// </summary>
        public const string RabbitMQSettings = "neon.rabbitmq-settings";
    }
}
