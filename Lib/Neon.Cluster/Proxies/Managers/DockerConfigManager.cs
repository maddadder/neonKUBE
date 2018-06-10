﻿//-----------------------------------------------------------------------------
// FILE:	    DockerConfigManager.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

namespace Neon.Cluster
{
    /// <summary>
    /// Handles Docker config related operations for a <see cref="ClusterProxy"/>.
    /// </summary>
    public sealed class DockerConfigManager
    {
        private ClusterProxy cluster;

        /// <summary>
        /// Internal constructor.
        /// </summary>
        /// <param name="cluster">The parent <see cref="ClusterProxy"/>.</param>
        internal DockerConfigManager(ClusterProxy cluster)
        {
            Covenant.Requires<ArgumentNullException>(cluster != null);

            this.cluster = cluster;
        }

        /// <summary>
        /// Determines whether a Docker config exists.
        /// </summary>
        /// <param name="configName">The config name.</param>
        /// <returns><c>true</c> if the config exists.</returns>
        /// <exception cref="ClusterException">Thrown if the operation failed.</exception>
        public bool Exists(string configName)
        {
            var manager  = cluster.GetHealthyManager();
            var response = manager.DockerCommand(RunOptions.None, "docker config inspect", configName);

            if (response.ExitCode == 0)
            {
                return true;
            }
            else
            {
                // $todo(jeff.lill): 
                //
                // I'm trying to distinguish between a a failure because the config doesn't
                // exist and other potential failures (e.g. Docker is not running).
                //
                // This is a bit fragile.

                if (response.ErrorText.StartsWith("Status: Error: no such config:", StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }
                else
                {
                    throw new ClusterException(response.ErrorSummary);
                }
            }
        }

        /// <summary>
        /// Creates or updates a cluster Docker string config.
        /// </summary>
        /// <param name="configName">The config name.</param>
        /// <param name="value">The config value.</param>
        /// <param name="options">Optional command run options.</param>
        /// <exception cref="ClusterException">Thrown if the operation failed.</exception>
        public void Set(string configName, string value, RunOptions options = RunOptions.None)
        {
            Covenant.Requires<ArgumentException>(ClusterDefinition.IsValidName(configName));
            Covenant.Requires<ArgumentNullException>(value != null);

            Set(configName, Encoding.UTF8.GetBytes(value), options);
        }

        /// <summary>
        /// Creates or updates a cluster Docker binary config.
        /// </summary>
        /// <param name="configName">The config name.</param>
        /// <param name="value">The config value.</param>
        /// <param name="options">Optional command run options.</param>
        /// <exception cref="ClusterException">Thrown if the operation failed.</exception>
        public void Set(string configName, byte[] value, RunOptions options = RunOptions.None)
        {
            Covenant.Requires<ArgumentException>(ClusterDefinition.IsValidName(configName));
            Covenant.Requires<ArgumentNullException>(value != null);

            var bundle = new CommandBundle("./create-config.sh");

            bundle.AddFile("config.dat", value);
            bundle.AddFile("create-config.sh",
$@"#!/bin/bash

if docker config inspect {configName} ; then
    echo ""Config already exists; not setting it again.""
else
    cat config.dat | docker config create {configName} -

    # It appears that Docker configs may not be available
    # immediately after they are created.  So, we're going 
    # wait for a while until we can inspect the new config.

    count=0

    while [ $count -le 30 ]
    do
        if docker config inspect {configName} ; then
            exit 0
        fi
        
        sleep 1
        count=$(( count + 1 ))
    done

    echo ""Created config [{configName}] is not ready after 30 seconds."" >&2
    exit 1
fi
",
                isExecutable: true);

            var response = cluster.GetHealthyManager().SudoCommand(bundle, options);

            if (response.ExitCode != 0)
            {
                throw new ClusterException(response.ErrorSummary);
            }
        }

        /// <summary>
        /// Deletes a cluster Docker config.
        /// </summary>
        /// <param name="configName">The config name.</param>
        /// <param name="options">Optional command run options.</param>
        /// <exception cref="ClusterException">Thrown if the operation failed.</exception>
        public void Remove(string configName, RunOptions options = RunOptions.None)
        {
            Covenant.Requires<ArgumentException>(ClusterDefinition.IsValidName(configName));

            var bundle = new CommandBundle("./delete-config.sh");

            bundle.AddFile("delete-config.sh",
$@"#!/bin/bash
docker config inspect {configName}

if [ ""$?"" != ""0"" ] ; then
    echo ""Config doesn't exist.""
else
    docker config rm {configName}
fi
",              isExecutable: true);

            var response = cluster.GetHealthyManager().SudoCommand(bundle, RunOptions.None);

            if (response.ExitCode != 0)
            {
                throw new ClusterException(response.ErrorSummary);
            }
        }
    }
}
