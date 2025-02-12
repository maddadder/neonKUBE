﻿//-----------------------------------------------------------------------------
// FILE:	    NodeSshProxy.Extensions.cs
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

// This file includes node configuration methods executed while setting
// up a neonKUBE cluster.

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Neon.Collections;
using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.SSH;
using Neon.Time;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Renci.SshNet;
using Renci.SshNet.Common;

namespace Neon.Kube
{
    /// <summary>
    /// Extends the <see cref="NodeSshProxy{TMetadata}"/> class by adding cluster setup related methods.
    /// </summary>
    public static class NodeSshProxyExtensions
    {
        /// <summary>
        /// Installs the Helm charts as a single ZIP archive written to the 
        /// neonKUBE node's Helm folder.
        /// </summary>
        /// <param name="node">The node instance.</param>
        /// <param name="controller">The setup controller.</param>
        public static void NodeInstallHelmArchive(this ILinuxSshProxy node, ISetupController controller)
        {
            Covenant.Requires<ArgumentNullException>(controller != null, nameof(controller));

            using (var ms = new MemoryStream())
            {
                controller.LogProgress(node, verb: "setup", message: "helm charts (zip)");

                var helmFolder = KubeSetup.Resources.GetDirectory("/Helm");    // $hack(jefflill): https://github.com/nforgeio/neonKUBE/issues/1121

                helmFolder.Zip(ms, searchOptions: SearchOption.AllDirectories, zipOptions: StaticZipOptions.LinuxLineEndings);

                ms.Seek(0, SeekOrigin.Begin);
                node.Upload(LinuxPath.Combine(KubeNodeFolder.Helm, "charts.zip"), ms, permissions: "660");
            }
        }
    }
}
