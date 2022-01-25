﻿//-----------------------------------------------------------------------------
// FILE:	    HyperVLocalHostingManager.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

using Neon.Collections;
using Neon.Common;
using Neon.Cryptography;
using Neon.HyperV;
using Neon.IO;
using Neon.Net;
using Neon.SSH;
using Neon.Time;

namespace Neon.Kube
{
    /// <summary>
    /// Manages cluster provisioning on the local workstation using Microsoft Hyper-V virtual machines.
    /// This is typically used for development and test purposes.
    /// </summary>
    [HostingProvider(HostingEnvironment.HyperVLocal)]
    public class HyperVLocalHostingManager : HostingManager
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Ensures that the assembly hosting this hosting manager is loaded.
        /// </summary>
        public static void Load()
        {
            // We don't have to do anything here because the assembly is loaded
            // as a byproduct of calling this method.
        }

        //---------------------------------------------------------------------
        // Instance members.

        private const string defaultSwitchName = "external";

        private ClusterProxy                        cluster;
        private string                              nodeImageUri;
        private string                              nodeImagePath;
        private SetupController<NodeDefinition>     controller;
        private string                              driveTemplatePath;
        private string                              vmDriveFolder;
        private LocalHyperVHostingOptions           hostingOptions;
        private string                              switchName;
        private string                              secureSshPassword;

        /// <summary>
        /// Creates an instance that is only capable of validating the hosting
        /// related options in the cluster definition.
        /// </summary>
        public HyperVLocalHostingManager()
        {
        }

        /// <summary>
        /// Creates an instance that is capable of managing and/or provisioning a cluster on the local machine using Hyper-V.
        /// </summary>
        /// <param name="cluster">The cluster being managed.</param>
        /// <param name="nodeImageUri">Optionally specifies the node image URI.</param>
        /// <param name="nodeImagePath">Optionally specifies the path to the local node image file.</param>
        /// <param name="logFolder">
        /// The folder where log files are to be written, otherwise or <c>null</c> or 
        /// empty if logging is disabled.
        /// </param>
        /// <remarks>
        /// <note>
        /// One of <paramref name="nodeImageUri"/> or <paramref name="nodeImagePath"/> must be specified to be able
        /// to provision a cluster but these can be <c>null</c> when you need to manage a cluster lifecycle.
        /// </note>
        /// </remarks>
        public HyperVLocalHostingManager(ClusterProxy cluster, string nodeImageUri = null, string nodeImagePath = null, string logFolder = null)
        {
            Covenant.Requires<ArgumentNullException>(cluster != null, nameof(cluster));

            cluster.HostingManager = this;

            this.cluster        = cluster;
            this.nodeImageUri   = nodeImageUri;
            this.nodeImagePath  = nodeImagePath;
            this.hostingOptions = cluster.Definition.Hosting.HyperVLocal;

            // Determine where we're going to place the VM hard drive files and
            // ensure that the directory exists.

            if (!string.IsNullOrEmpty(cluster.Definition.Hosting.Vm.DiskLocation))
            {
                vmDriveFolder = cluster.Definition.Hosting.Vm.DiskLocation;
            }
            else
            {
                vmDriveFolder = HyperVClient.DefaultDriveFolder;
            }

            Directory.CreateDirectory(vmDriveFolder);
        }

        /// <inheritdoc/>
        public override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        /// <inheritdoc/>
        public override HostingEnvironment HostingEnvironment => HostingEnvironment.HyperVLocal;

        /// <inheritdoc/>
        public override bool RequiresNodeAddressCheck => true;

        /// <inheritdoc/>
        public override void Validate(ClusterDefinition clusterDefinition)
        {
            Covenant.Requires<ArgumentNullException>(clusterDefinition != null, nameof(clusterDefinition));

            if (clusterDefinition.Hosting.Environment != HostingEnvironment.HyperVLocal)
            {
                throw new ClusterDefinitionException($"{nameof(HostingOptions)}.{nameof(HostingOptions.Environment)}] must be set to [{HostingEnvironment.HyperVLocal}].");
            }
        }

        /// <inheritdoc/>
        public override void AddProvisioningSteps(SetupController<NodeDefinition> controller)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));
            Covenant.Requires<NotSupportedException>(cluster != null, $"[{nameof(HyperVLocalHostingManager)}] was created with the wrong constructor.");

            var clusterLogin = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);

            this.controller        = controller;
            this.secureSshPassword = clusterLogin.SshPassword;

            // We need to ensure that the cluster has at least one ingress node.

            KubeHelper.EnsureIngressNodes(cluster.Definition);

            // Update the node labels with the actual capabilities of the 
            // virtual machines being provisioned.

            foreach (var node in cluster.Definition.Nodes)
            {
                node.Labels.PhysicalMachine = Environment.MachineName;
                node.Labels.ComputeCores    = cluster.Definition.Hosting.Vm.Cores;
                node.Labels.ComputeRam      = (int)(ClusterDefinition.ValidateSize(cluster.Definition.Hosting.Vm.Memory, typeof(HostingOptions), nameof(HostingOptions.Vm.Memory))/ ByteUnits.MebiBytes);
                node.Labels.StorageSize     = ByteUnits.ToGiB(node.Vm.GetMemory(cluster.Definition));
            }

            // Add the provisioning steps to the controller.

            controller.MaxParallel = 1; // We're only going to provision one VM at a time on the local Hyper-V.

            controller.AddGlobalStep("initialize",
                controller =>
                {
                    var clusterLogin = controller.Get<ClusterLogin>(KubeSetupProperty.ClusterLogin);

                    this.secureSshPassword = clusterLogin.SshPassword;

                    // If the cluster is being deployed to the internal [neonkube] switch, we need to
                    // check to see whether the switch already exists, and if it does, we'll need to
                    // ensure that it's configured correctly with a virtual address and NAT.  We're
                    // going to fail setup when an existing switch isn't configured correctly.

                    if (cluster.Definition.Hosting.HyperVLocal.UseInternalSwitch)
                    {
                        using (var hyperv = new HyperVClient())
                        {
                            controller.SetGlobalStepStatus($"check: [{KubeConst.HyperVLocalInternalSwitchName}] virtual switch");

                            var localHyperVOptions = cluster.Definition.Hosting.HyperVLocal;
                            var @switch            = hyperv.GetSwitch(KubeConst.HyperVLocalInternalSwitchName);
                            var address            = hyperv.GetIPAddress(localHyperVOptions.NeonDesktopNodeAddress.ToString());
                            var nat                = hyperv.GetNATByName(KubeConst.HyperVLocalInternalSwitchName);

                            if (@switch != null)
                            {
                                if (@switch.Type != VirtualSwitchType.Internal)
                                {
                                    throw new KubeException($"The existing [{@switch.Name}] Hyper-V virtual switch is misconfigured.  It's type must be [internal].");
                                }

                                if (address != null && !address.InterfaceName.Equals(@switch.Name))
                                {
                                    throw new KubeException($"The existing [{@switch.Name}] Hyper-V virtual switch is misconfigured.  The [{localHyperVOptions.NeonKubeInternalSubnet}] IP address is not assigned to this switch.");
                                }

                                if (nat.Subnet != localHyperVOptions.NeonKubeInternalSubnet)
                                {
                                    throw new KubeException($"The existing [{@switch.Name}] Hyper-V virtual switch is misconfigured.  The [{nat.Name}] NAT subnet is not set to [{localHyperVOptions.NeonKubeInternalSubnet}].");
                                }
                            }
                        }
                    }
                });

            if (!controller.Get<bool>(KubeSetupProperty.DisableImageDownload, false))
            {
                controller.AddGlobalStep($"hyper-v node image",
                    async state =>
                    {
                        // Download the GZIPed VHDX template if it's not already present and has a valid
                        // MD5 hash file.
                        //
                        // Note that we're going to name the file the same as the file name from the URI.

                        string driveTemplateName;

                        if (!string.IsNullOrEmpty(nodeImageUri))
                        {
                            var driveTemplateUri = new Uri(nodeImageUri);

                            driveTemplateName = Path.GetFileNameWithoutExtension(driveTemplateUri.Segments.Last());
                            driveTemplatePath = Path.Combine(KubeHelper.NodeImageFolder, driveTemplateName);

                            await KubeHelper.DownloadNodeImageAsync(nodeImageUri, driveTemplatePath,
                                (progressType, progress) =>
                                {
                                    controller.SetGlobalStepStatus($"{NeonHelper.EnumToString(progressType)}: VHDX [{progress}%] [{driveTemplateName}]");

                                    return !controller.CancelPending;
                                });
                        }
                        else
                        {
                            Covenant.Assert(File.Exists(nodeImagePath), $"Missing file: {nodeImagePath}");

                            driveTemplateName = Path.GetFileName(nodeImagePath);
                            driveTemplatePath = nodeImagePath;
                        }
                    });
            }

            controller.AddGlobalStep("configure hyper-v", async controller => await PrepareHyperVAsync());
            controller.AddNodeStep("provision virtual machines", (controller, node) => ProvisionVM(node));
        }

        /// <inheritdoc/>
        public override void AddPostProvisioningSteps(SetupController<NodeDefinition> controller)
        {
            // We need to add any required OpenEBS cStor disks after the node has been otherwise
            // prepared.  We need to do this here because if we created the data and OpenEBS disks
            // when the VM is initially created, the disk setup scripts executed during prepare
            // won't be able to distinguish between the two disks.
            //
            // At this point, the data disk should be partitioned, formatted, and mounted so
            // the OpenEBS disk will be easy to identify as the only unpartitioned disk.

            controller.AddNodeStep("openebs",
                (state, node) =>
                {
                    using (var hyperv = new HyperVClient())
                    {
                        var vmName   = GetVmName(node.Metadata);
                        var diskSize = node.Metadata.Vm.GetOpenEbsDisk(cluster.Definition);
                        var diskPath = Path.Combine(vmDriveFolder, $"{vmName}-openebs.vhdx");

                        node.Status = "openebs: checking";

                        if (hyperv.GetVmDrives(vmName).Count < 2)
                        {
                            // The disk doesn't already exist.

                            node.Status = "openebs: stop VM";
                            hyperv.StopVm(vmName);

                            node.Status = "openebs: add cStor disk";
                            hyperv.AddVmDrive(vmName,
                                new VirtualDrive()
                                {
                                    Path = diskPath,
                                    Size = diskSize
                                });

                            node.Status = "openebs: restart VM";
                            hyperv.StartVm(vmName);
                        }
                    }
                },
                (state, node) => node.Metadata.OpenEbsStorage);
        }

        /// <inheritdoc/>
        public override void AddDeprovisoningSteps(SetupController<NodeDefinition> controller)
        {
            // Deprovisioning is easy for Hyper-V.  All we need to do is to turn off
            // and remove the virtual machines.

            controller.AddNodeStep("turn-off virtual machines",
                (controller, node) =>
                {
                    node.Status = "turning off";

                    using (var hyperv = new HyperVClient())
                    {
                        var vmName = GetVmName(node.Metadata);

                        hyperv.StopVm(vmName, turnOff: true);
                    }
                });

            controller.AddNodeStep("remove virtual machines",
                (controller, node) =>
                {
                    node.Status = "removing";

                    using (var hyperv = new HyperVClient())
                    {
                        var vmName = GetVmName(node.Metadata);

                        hyperv.RemoveVm(vmName);
                    }
                });
        }

        /// <inheritdoc/>
        public override (string Address, int Port) GetSshEndpoint(string nodeName)
        {
            return (Address: cluster.GetNode(nodeName).Address.ToString(), Port: NetworkPorts.SSH);
        }

        /// <inheritdoc/>
        public override bool RequiresAdminPrivileges => true;

        /// <inheritdoc/>
        public override string GetDataDisk(LinuxSshProxy node)
        {
            Covenant.Requires<ArgumentNullException>(node != null, nameof(node));

            // This hosting manager doesn't currently provision a separate data disk.

            return "PRIMARY";
        }

        /// <summary>
        /// Returns the name to use for naming the virtual machine hosting the node.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <returns>The virtual machine name.</returns>
        private string GetVmName(NodeDefinition node)
        {
            return $"{cluster.Definition.Hosting.Vm.GetVmNamePrefix(cluster.Definition)}{node.Name}";
        }

        /// <summary>
        /// Attempts to extract the cluster node name from a virtual machine name.
        /// </summary>
        /// <param name="machineName">The virtual machine name.</param>
        /// <returns>
        /// The extracted node name if the virtual machine belongs to this 
        /// cluster or else the empty string.
        /// </returns>
        private string ExtractNodeName(string machineName)
        {
            var clusterPrefix = cluster.Definition.Hosting.Vm.GetVmNamePrefix(cluster.Definition);

            if (machineName.StartsWith(clusterPrefix))
            {
                return machineName.Substring(clusterPrefix.Length);
            }
            else
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Performs any required Hyper-V initialization before cluster nodes can be provisioned.
        /// </summary>
        /// <returns>The tracking <see cref="Task"/>.</returns>
        private async Task PrepareHyperVAsync()
        {
            // Handle any necessary Hyper-V initialization.

            using (var hyperv = new HyperVClient())
            {
                // Manage the Hyper-V virtual switch.  This will be an internal switch
                // when [UseInternalSwitch=TRUE] otherwise this will be external.

                if (hostingOptions.UseInternalSwitch)
                {
                    switchName = KubeConst.HyperVLocalInternalSwitchName;

                    controller.SetGlobalStepStatus($"configure: [{switchName}] internal switch");

                    // We're going to create an internal switch named [neonkube] configured
                    // with the standard private subnet and a NAT to enable external routing.

                    var @switch = hyperv.GetSwitch(switchName);

                    if (@switch == null)
                    {
                        // The internal switch doesn't exist yet, so create it.  Note that
                        // this switch requires a virtual NAT.

                        controller.SetGlobalStepStatus($"add: [{switchName}] internal switch with NAT for [{hostingOptions.NeonKubeInternalSubnet}]");
                        hyperv.NewInternalSwitch(switchName, hostingOptions.NeonKubeInternalSubnet, addNAT: true);
                        controller.SetGlobalStepStatus();
                    }

                    controller.SetGlobalStepStatus();
                }
                else
                {
                    // We're going to create an external Hyper-V switch if there
                    // isn't already an external switch.

                    controller.SetGlobalStepStatus("scan: network adapters");

                    var externalSwitch = hyperv.ListSwitches().FirstOrDefault(@switch => @switch.Type == VirtualSwitchType.External);

                    if (externalSwitch == null)
                    {
                        hyperv.NewExternalSwitch(switchName = defaultSwitchName, NetHelper.ParseIPv4Address(cluster.Definition.Network.Gateway));
                    }
                    else
                    {
                        switchName = externalSwitch.Name;
                    }
                }

                // Ensure that the cluster virtual machines exist and are stopped,
                // taking care to issue a warning if any machines already exist 
                // and we're not doing [force] mode.

                controller.SetGlobalStepStatus("scan: virtual machines");

                var existingMachines = hyperv.ListVms();
                var conflicts        = string.Empty;
                var conflictCount    = 0;

                controller.SetGlobalStepStatus("stop: virtual machines");

                foreach (var machine in existingMachines)
                {
                    var nodeName    = ExtractNodeName(machine.Name);
                    var drivePath   = Path.Combine(vmDriveFolder, $"{machine.Name}.vhdx");
                    var isClusterVM = cluster.FindNode(nodeName) != null;

                    if (isClusterVM)
                    {
                        // We're going to report errors when one or more machines already exist.

                        if (conflicts.Length > 0)
                        {
                            conflicts += ", ";
                        }

                        conflicts += nodeName;
                    }
                }

                if (conflictCount == 1)
                {
                    throw new HyperVException($"[{conflicts}] virtual machine already exists.");
                }
                else if (conflictCount > 1)
                {
                    throw new HyperVException($"[{conflicts}] virtual machines already exist.");
                }

                controller.SetGlobalStepStatus();
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Creates a Hyper-V virtual machine for a cluster node.
        /// </summary>
        /// <param name="node">The target node.</param>
        private void ProvisionVM(NodeSshProxy<NodeDefinition> node)
        {
            using (var hyperv = new HyperVClient())
            {
                var vmName = GetVmName(node.Metadata);

                // Decompress the VHDX template file to the virtual machine's
                // virtual hard drive file.

                var driveTemplateInfoPath = driveTemplatePath + ".info";
                var osDrivePath           = Path.Combine(vmDriveFolder, $"{vmName}.vhdx");

                using (var input = new FileStream(driveTemplatePath, FileMode.Open, FileAccess.Read))
                {
                    using (var output = new FileStream(osDrivePath, FileMode.Create, FileAccess.ReadWrite))
                    {
                        using (var decompressor = new GZipStream(input, CompressionMode.Decompress))
                        {
                            var     buffer = new byte[64 * 1024];
                            long    cbRead = 0;
                            int     cb;

                            while (true)
                            {
                                cb     = decompressor.Read(buffer, 0, buffer.Length);
                                cbRead = input.Position;

                                if (cb == 0)
                                {
                                    break;
                                }

                                output.Write(buffer, 0, cb);

                                var percentComplete = (int)((double)cbRead / (double)input.Length * 100.0);

                                controller.SetGlobalStepStatus($"decompress: node VHDX [{percentComplete}%]");
                            }

                            controller.SetGlobalStepStatus($"decompress: node VHDX [100%]");
                        }
                    }

                    controller.SetGlobalStepStatus();
                }

                // Create the virtual machine.

                var processors  = node.Metadata.Vm.GetCores(cluster.Definition);
                var memoryBytes = node.Metadata.Vm.GetMemory(cluster.Definition);
                var osDiskBytes = node.Metadata.Vm.GetOsDisk(cluster.Definition);

                node.Status = $"create: virtual machine";
                hyperv.AddVm(
                    vmName,
                    processorCount: processors,
                    diskSize:       osDiskBytes.ToString(),
                    memorySize:     memoryBytes.ToString(),
                    drivePath:      osDrivePath,
                    switchName:     switchName);

                // Create a temporary ISO with the [neon-init.sh] script, mount it
                // to the VM and then boot the VM for the first time.  The script on the
                // ISO will be executed automatically by the [neon-init] service
                // preinstalled on the VM image and the script will configure the secure 
                // SSH password and then the network.
                //
                // This ensures that SSH is not exposed to the network before the secure
                // password has been set.

                var tempIso = (TempFile)null;

                try
                {
                    // Create a temporary ISO with the prep script and mount it
                    // to the node VM.

                    node.Status = $"mount: neon-init iso";
                    tempIso     = KubeHelper.CreateNeonInitIso(node.Cluster.Definition, node.Metadata, secureSshPassword);

                    hyperv.InsertVmDvd(vmName, tempIso.Path);

                    // Start the VM for the first time with the mounted ISO.  The network
                    // configuration will happen automatically by the time we can connect.

                    node.Status = $"start: virtual machine";
                    hyperv.StartVm(vmName);

                    // Update the node credentials to use the secure password and then wait for the node to boot.

                    node.UpdateCredentials(SshCredentials.FromUserPassword(KubeConst.SysAdminUser, secureSshPassword));
                    node.WaitForBoot();

                    // Extend the primary partition and file system to fill 
                    // the virtual drive.  Note that we're not going to do
                    // this if the specified drive size is less than or equal
                    // to the node template's drive size (because that
                    // would fail).
                    //
                    // Note that there should only be one partitioned disk at
                    // this point: the OS disk.

                    var partitionedDisks = node.ListPartitionedDisks();
                    var osDisk           = partitionedDisks.Single();

                    if (osDiskBytes > KubeConst.MinNodeDiskSizeGiB)
                    {
                        node.Status = $"resize: OS disk";

                        var response = node.SudoCommand($"growpart {osDisk} 2", RunOptions.None);

                        // Ignore errors reported when the partition is already at its
                        // maximum size and cannot be grown:
                        //
                        //      https://github.com/nforgeio/neonKUBE/issues/1352

                        if (!response.Success && !response.AllText.Contains("NOCHANGE:"))
                        {
                            response.EnsureSuccess();
                        }

                        node.SudoCommand($"resize2fs {osDisk}2", RunOptions.FaultOnError);
                    }
                }
                finally
                {
                    // Be sure to delete the ISO file so these don't accumulate.

                    tempIso?.Dispose();
                }
            }
        }

        //---------------------------------------------------------------------
        // Cluster life cycle methods

        /// <inheritdoc/>
        public override async Task StartClusterAsync(bool noWait = false)
        {
            Covenant.Requires<NotSupportedException>(cluster != null, $"[{nameof(HyperVLocalHostingManager)}] was created with the wrong constructor.");

            // We just need to start any cluster VMs that aren't already running.

            using (var hyperv = new HyperVClient())
            {
                Parallel.ForEach(cluster.Definition.Nodes,
                    nodeDefinition =>
                    {
                        var vmName = GetVmName(nodeDefinition);
                        var vm     = hyperv.GetVm(vmName);

                        if (vm == null)
                        {
                            // We may see this when the cluster definition doesn't match the 
                            // deployed cluster VMs.  We're just going to ignore this situation.

                            return;
                        }

                        switch (vm.State)
                        {
                            case VirtualMachineState.Off:
                            case VirtualMachineState.Saved:

                                hyperv.StartVm(vmName);
                                break;

                            case VirtualMachineState.Running:
                            case VirtualMachineState.Starting:

                                break;

                            default:
                            case VirtualMachineState.Paused:
                            case VirtualMachineState.Unknown:

                                throw new NotImplementedException($"Unexpected VM state: {vmName}:{vm.State}");
                        }
                    });
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task StopClusterAsync(StopMode stopMode = StopMode.Graceful, bool noWait = false)
        {
            Covenant.Requires<NotSupportedException>(cluster != null, $"[{nameof(HyperVLocalHostingManager)}] was created with the wrong constructor.");

            // We just need to stop any running cluster VMs.

            using (var hyperv = new HyperVClient())
            {
                Parallel.ForEach(cluster.Definition.Nodes,
                    nodeDefinition =>
                    {
                        var vmName = GetVmName(nodeDefinition);
                        var vm     = hyperv.GetVm(vmName);

                        if (vm == null)
                        {
                            // We may see this when the cluster definition doesn't match the 
                            // deployed cluster VMs.  We're just going to ignore this situation.

                            return;
                        }

                        switch (vm.State)
                        {
                            case VirtualMachineState.Off:

                                break;

                            case VirtualMachineState.Saved:

                                throw new NotSupportedException($"Cannot shutdown the saved (hibernating) virtual machine: {vmName}");

                            case VirtualMachineState.Running:
                            case VirtualMachineState.Starting:

                                hyperv.StopVm(vmName);
                                break;

                            default:
                            case VirtualMachineState.Paused:
                            case VirtualMachineState.Unknown:

                                throw new NotImplementedException($"Unexpected VM state: {vmName}:{vm.State}");
                        }
                    });
            }

            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task RemoveClusterAsync(bool noWait = false, bool removeOrphansByPrefix = false)
        {
            Covenant.Requires<NotSupportedException>(cluster != null, $"[{nameof(HyperVLocalHostingManager)}] was created with the wrong constructor.");

            // All we need to do for Hyper-V clusters is turn off and remove the cluster VMs.
            // Note that we're just turning nodes off to save time and because we're going
            // to be deleting them all anyway.
            //
            // We're going to leave any virtual switches alone.

            await StopClusterAsync(stopMode: StopMode.TurnOff);

            using (var hyperv = new HyperVClient())
            {
                // Remove all of the cluster VMs.

                Parallel.ForEach(cluster.Definition.Nodes,
                    nodeDefinition =>
                    {
                        var vmName = GetVmName(nodeDefinition);
                        var vm     = hyperv.GetVm(vmName);

                        if (vm == null)
                        {
                            // We may see this when the cluster definition doesn't match the 
                            // deployed cluster VMs or when the cluster doesn't mexist.  We're
                            // just going to ignore this situation.

                            return;
                        }

                        hyperv.RemoveVm(vmName);
                    });

                // Remove any potentially orphaned VMs when enabled and a prefix is specified.

                if (removeOrphansByPrefix && !string.IsNullOrEmpty(cluster.Definition.Deployment.Prefix))
                {
                    var prefix = cluster.Definition.Deployment.Prefix + "-";

                    Parallel.ForEach(hyperv.ListVms(),
                        vm =>
                        {
                            if (vm.Name.StartsWith(prefix))
                            {
                                hyperv.RemoveVm(vm.Name);
                            }
                        });
                }
            }
        }

        /// <inheritdoc/>
        public override async Task StopNodeAsync(string nodeName, StopMode stopMode = StopMode.Graceful)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(nodeName), nameof(nodeName));

            if (!cluster.Definition.NodeDefinitions.TryGetValue(nodeName, out var nodeDefinition))
            {
                throw new InvalidOperationException($"Node [{nodeName}] is not present in the cluster.");
            }

            using (var hyperv = new HyperVClient())
            {
                var vmName = GetVmName(nodeDefinition);
                var vm     = hyperv.GetVm(vmName);

                if (vm == null)
                {
                    throw new InvalidOperationException($"Cannot find virtual machine for node [{nodeName}].");
                }

                switch (vm.State)
                {
                    case VirtualMachineState.Off:
                    case VirtualMachineState.Paused:
                    case VirtualMachineState.Saved:

                        // We're treating all of these states as: OFF

                        return;

                    case VirtualMachineState.Running:

                        // Drop out to actually stop the node below.

                        break;

                    default:
                    case VirtualMachineState.Starting:
                    case VirtualMachineState.Unknown:

                        throw new InvalidOperationException($"Cannot stop node [{nodeName}] when it is in the [{vm.State}] state.");
                }

                if (stopMode == StopMode.Sleep)
                {
                    hyperv.SaveVm(vmName);
                }
                else
                {
                    hyperv.StopVm(vmName, stopMode == StopMode.TurnOff);
                }

                await Task.CompletedTask;
            }
        }

        /// <inheritdoc/>
        public override async Task StartNodeAsync(string nodeName)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(nodeName), nameof(nodeName));

            if (!cluster.Definition.NodeDefinitions.TryGetValue(nodeName, out var nodeDefinition))
            {
                throw new InvalidOperationException($"Node [{nodeName}] is not present in the cluster.");
            }

            using (var hyperv = new HyperVClient())
            {
                var vmName = GetVmName(nodeDefinition);
                var vm     = hyperv.GetVm(vmName);

                if (vm == null)
                {
                    throw new InvalidOperationException($"Cannot find virtual machine for node [{nodeName}].");
                }

                switch (vm.State)
                {
                    case VirtualMachineState.Off:
                    case VirtualMachineState.Paused:
                    case VirtualMachineState.Saved:

                        hyperv.StartVm(vmName);
                        break;
                }

                await Task.CompletedTask;
            }
        }

        /// <inheritdoc/>
        public override async Task<string> GetNodeImageAsync(string nodeName, string folder)
        {
            Covenant.Requires<NotSupportedException>(cluster != null, $"[{nameof(HyperVLocalHostingManager)}] was created with the wrong constructor.");
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(nodeName), nameof(nodeName));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(folder), nameof(folder));

            if (!cluster.Definition.NodeDefinitions.TryGetValue(nodeName, out var nodeDefinition))
            {
                throw new InvalidOperationException($"Node [{nodeName}] is not present in the cluster.");
            }

            using (var hyperv = new HyperVClient())
            {
                var vmName = GetVmName(nodeDefinition);
                var vm     = hyperv.GetVm(vmName);

                if (vm == null)
                {
                    throw new InvalidOperationException($"Cannot find virtual machine for node [{nodeName}].");
                }

                if (vm.State != VirtualMachineState.Off)
                {
                    throw new InvalidOperationException($"Node [{nodeName}] current state is [{vm.State}].  The node must be stopped first.");
                }

                var drives = hyperv.GetVmDrives(vmName);

                if (drives.Count != 1)
                {
                    throw new InvalidOperationException($"Node [{nodeName}] has [{drives.Count}] drives.  Only nodes with a single drive are supported.");
                }

                var sourceImagePath = drives.First();
                var targetImagePath = Path.GetFullPath(Path.Combine(folder, $"{nodeName}.vhdx"));

                Directory.CreateDirectory(folder);
                NeonHelper.DeleteFile(targetImagePath);
                File.Copy(sourceImagePath, targetImagePath);
                hyperv.CompactDrive(targetImagePath);

                return await Task.FromResult(targetImagePath);
            }
        }
    }
}
