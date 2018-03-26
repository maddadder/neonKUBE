﻿//-----------------------------------------------------------------------------
// FILE:	    VaultOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.Net;
using Neon.Time;

namespace Neon.Cluster
{
    /// <summary>
    /// Describes the HashiCorp Vault options for a neonCLUSTER.
    /// </summary>
    public class VaultOptions
    {
        private const string    defaultVersion      = "0.9.0";
        private const int       defaultKeyCount     = 1;
        private const int       defaultKeyThreshold = 1;
        private const string    defaultLease        = "0";

        /// <summary>
        /// Default constructor.
        /// </summary>
        public VaultOptions()
        {
        }

        /// <summary>
        /// The version of the <b>neoncluster/vault</b> image to be installed.  
        /// This defaults to a reasonable recent version.
        /// </summary>
        [JsonProperty(PropertyName = "Version", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultVersion)]
        public string Version { get; set; } = defaultVersion;

        /// <summary>
        /// The number of unseal keys to be generated by Vault when it is initialized.
        /// This defaults to <b>1</b>.
        /// </summary>
        [JsonProperty(PropertyName = "KeyCount", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultKeyCount)]
        public int KeyCount { get; set; } = defaultKeyCount;

        /// <summary>
        /// The minimum number of unseal keys that will be required to unseal Vault.
        /// This defaults to <b>1</b>.
        /// </summary>
        [JsonProperty(PropertyName = "KeyThreshold", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultKeyThreshold)]
        public int KeyThreshold { get; set; } = defaultKeyThreshold;

        /// <summary>
        /// Specifies whether the cluster should automatically unseal the Vault
        /// after a cluster, manager node, or Vault service restart.  This defaults
        /// to <c>true</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The HashiCorp Vault service does not keep the keys used to encrypt its
        /// persisted storage on disk by default.  This is done to ensure very high
        /// security.  The downside is that when Vault services are restarted, they'll
        /// come up in the <b>sealed</b> state, which means they won't be able to
        /// service requests until it's been <b>unsealed</b> manually.
        /// </para>
        /// <para>
        /// By default, neonCLUSTERs will configure its <b>neon-cluster-manager</b> to
        /// automatically unseal Vault servers by passing the Vault keys to <b>neon-cluster-manager</b>
        /// as an environment variable.  This will be reasonably secure and in the future
        /// we hope to use Docker secrets which will be more secure.
        /// </para>
        /// <para>
        /// For clusters with very high security requirements, you can disable this
        /// by setting <see cref="AutoUnseal"/>=<c>false</c>.  This means your operators
        /// will need to manually unseal restarted Vault instances using the <b>neon-cli</b>
        /// command:
        /// </para>
        /// <c>neon vault unseal</c>
        /// </remarks>
        [JsonProperty(PropertyName = "AutoUnseal", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(true)]
        public bool AutoUnseal { get; set; }

        /// <summary>
        /// Returns the direct URI to a Vault service instance on a specific manager node.
        /// </summary>
        /// <param name="managerName">The name of the target manager node.</param>
        public string GetDirectUri(string managerName)
        {
            return $"https://{managerName}.{NeonHosts.Vault}:{Port}";
        }

        /// <summary>
        /// Returns the proxied URI to the cluster's Vault service.
        /// </summary>
        [JsonIgnore]
        public string Uri
        {
            get { return $"https://{NeonHosts.Vault}:{NeonHostPorts.ProxyVault}"; }
        }

        /// <summary>
        /// <para>
        /// The maximum allowed TTL for a Vault token or secret.  This limit will
        /// be silently enforced by Vault.  This can be expressed as hours with an "<b>h</b>"
        /// suffix, minutes using "<b>m</b>" and seconds using "<b>s</b>".  You can also
        /// combine these like "<b>10h30m10s</b>".  This defaults to "<b>maximum</b> or
        /// about <b>290 years</b> (essentially infinity).
        /// </para>
        /// <note>
        /// This default was choosen so that clusters won't have to worry about tokens
        /// and secrets expiring at inopportune times, taking services down.   High security
        /// deployments may wish to override this globally or set the leases for specific
        /// tokens and secrets explicitly.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "MaximimLease", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultLease)]
        public string MaximimLease { get; set; } = defaultLease;

        /// <summary>
        /// <para>
        /// The default allowed TTL for a new Vault token or secret if no other duration
        /// is specified .  This can be expressed as hours with an "<b>h</b>" suffix, 
        /// minutes using "<b>m</b>" and seconds using "<b>s</b>".  You can also
        /// combine these like "<b>10h30m10s</b>".  This defaults to "<b>maximum</b> or
        /// about <b>290 years</b> (essentially infinity).
        /// </para>
        /// <note>
        /// This default was choosen so that clusters won't have to worry about tokens
        /// and secrets expiring at inopportune times, taking services down.   High security
        /// deployments may wish to override this globally or set the leases for specific
        /// tokens and secrets explicitly.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "DefaultLease", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultLease)]
        public string DefaultLease { get; set; } = defaultLease;

        /// <summary>
        /// Returns the Vault port.
        /// </summary>
        [JsonIgnore]
        public int Port
        {
            get { return NetworkPorts.Vault; }
        }

        /// <summary>
        /// Validates the options and also ensures that all <c>null</c> properties are
        /// initialized to their default values.
        /// </summary>
        /// <param name="clusterDefinition">The cluster definition.</param>
        /// <exception cref="ClusterDefinitionException">Thrown if the definition is not valid.</exception>
        [Pure]
        public void Validate(ClusterDefinition clusterDefinition)
        {
            Covenant.Requires<ArgumentNullException>(clusterDefinition != null);

            if (string.IsNullOrWhiteSpace(Version))
            {
                throw new ClusterDefinitionException($"Invalid version [{nameof(VaultOptions)}.{nameof(Version)}={Version}].");
            }

            if (KeyCount <= 0)
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(KeyCount)}] must be greater than zero.");
            }

            if (KeyThreshold <= 0)
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(KeyThreshold)}] must be greater than zero.");
            }

            if (KeyThreshold > KeyCount)
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(KeyThreshold)}] cannot be greater than [{nameof(VaultOptions)}.{nameof(KeyCount)}].");
            }

            if (!GoTimeSpan.TryParse(MaximimLease, out var goMaximumLease))
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(MaximimLease)}={MaximimLease}] is not a valid GO duration.");
            }

            if (!GoTimeSpan.TryParse(DefaultLease, out var goDefaultLease))
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(DefaultLease)}={DefaultLease}] is not a valid GO duration.");
            }

            // Treat zero lease values as essentially unlimited.

            if (goMaximumLease.TimeSpan == TimeSpan.Zero)
            {
                goMaximumLease = GoTimeSpan.MaxValue;
                MaximimLease   = goMaximumLease.ToString();
            }

            if (goDefaultLease.TimeSpan == TimeSpan.Zero)
            {
                goDefaultLease = GoTimeSpan.MaxValue;
                DefaultLease   = goDefaultLease.ToString();
            }

            if (goDefaultLease.TimeSpan > goMaximumLease.TimeSpan)
            {
                throw new ClusterDefinitionException($"[{nameof(VaultOptions)}.{nameof(DefaultLease)}= {DefaultLease}] is greater than [{nameof(MaximimLease)}={MaximimLease}].");
            }
        }
    }
}
