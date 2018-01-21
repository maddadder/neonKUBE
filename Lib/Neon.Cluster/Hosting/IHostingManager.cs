﻿//-----------------------------------------------------------------------------
// FILE:	    IHostingManager.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

using ICSharpCode.SharpZipLib.Zip;
using Renci.SshNet;
using Renci.SshNet.Common;

// $todo(jeff.lill):
//
// Have [NodeProxy.Manager] return a healthy manager rather than just the
// first one.

namespace Neon.Cluster
{
    /// <summary>
    /// Interface describing the hosting environment managers.
    /// </summary>
    public interface IHostingManager : IDisposable
    {
        /// <summary>
        /// Returns <c>true</c> if the provisioning operation actually does nothing.
        /// </summary>
        bool IsProvisionNOP { get; }

        /// <summary>
        /// Creates and initializes the cluster resources such as the virtual machines,
        /// networks, load balancers, network security groups, public IP addresses etc.
        /// </summary>
        /// <param name="force">
        /// Indicates that any existing resources (such as virtual machines) 
        /// are to be replaced or overwritten during privisioning.  The actual interpretation
        /// of this parameter is specific to each hosting manager implementation.
        /// </param>
        /// <returns><c>true</c> if the operation was successful.</returns>
        bool Provision(bool force);

        /// <summary>
        /// Returns the FQDN or IP address (as a string) and the port to use
        /// to establish a SSH connection to a node while provisioning is in
        /// progress.
        /// </summary>
        /// <param name="nodeName">The target node's name.</param>
        /// <returns>A <b>(string Address, int Port)</b> tuple.</returns>
        /// <remarks>
        /// Hosting platforms such as Azure that may not assign public IP addresses
        /// to cluster nodes will return the IP address of the load balancer and
        /// a temporary NAT port for the node.
        /// </remarks>
        (string Address, int Port) GetSshEndpoint(string nodeName);

        /// <summary>
        /// Adds any necessary post-provisioning steps to the step controller.
        /// </summary>
        /// <param name="controller">The target setup controller.</param>
        void AddPostProvisionSteps(SetupController controller);

        /// <summary>
        /// Adds any necessary post-VPN steps to the step controller.
        /// </summary>
        /// <param name="controller">The target setup controller.</param>
        void AddPostVpnSteps(SetupController controller);

        /// <summary>
        /// Returns the endpoints currently exposed to the public for the deployment.
        /// </summary>
        /// <returns>The list of <see cref="HostedEndpoint"/> instances.</returns>
        List<HostedEndpoint> GetPublicEndpoints();

        /// <summary>
        /// Returns <c>true</c> if the cluster manager is able to update the the deployment's load balancer and security rules.
        /// </summary>
        bool CanUpdatePublicEndpoints { get; }

        /// <summary>
        /// Updates the deployment's load balancer and security rules to allow traffic 
        /// for the specified endpoints into the cluster.
        /// </summary>
        /// <param name="endpoints">The endpoints.</param>
        void UpdatePublicEndpoints(List<HostedEndpoint> endpoints);
    }
}
