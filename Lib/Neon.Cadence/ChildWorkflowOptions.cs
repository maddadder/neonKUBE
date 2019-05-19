﻿//-----------------------------------------------------------------------------
// FILE:	    ChildWorkflowOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;

using Newtonsoft.Json;
using YamlDotNet.Serialization;

using Neon.Cadence;
using Neon.Common;
using Neon.Retry;
using Neon.Time;

namespace Neon.Cadence.Internal
{
    /// <summary>
    /// Specifies the options to use when executing a child workflow.
    /// </summary>
    internal class ChildWorkflowOptions
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ChildWorkflowOptions()
        {
        }

        /// <summary>
        /// Optionally specifies the domain where the child workflow will run. 
        /// This defaults to the parent workflow's domain.
        /// </summary>
        public string Domain { get; set; } = null;

        /// <summary>
        /// Optionally specifies the workflow ID to assign to the child workflow.
        /// A UUID will be generated by default.
        /// </summary>
        public string WorkflowID { get; set; } = null;

        /// <summary>
        /// Optionally specifies the tasklist where the child workflow will be
        /// scheduled.  This defaults to the parent's tasklist.
        /// </summary>
        public string TaskList { get; set; } = null;

        /// <summary>
        /// Specifies the maximum time the child workflow may run from start
        /// to finish.  This is required.
        /// </summary>
        public TimeSpan ExecutionStartToCloseTimeout { get; set; }

        /// <summary>
        /// Optionally specifies the decision task timeout for the child workflow.
        /// This defaults to <b>10 seconds</b>.
        /// </summary>
        public TimeSpan TaskStartToCloseTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optionally specifies what happens to the child workflow when the parent is terminated.
        /// This defaults to <see cref="ChildWorkflowPolicy.ChildWorkflowPolicyAbandon"/>.
        /// </summary>
        public ChildWorkflowPolicy ChildPolicy { get; set; } = ChildWorkflowPolicy.ChildWorkflowPolicyAbandon;

        /// <summary>
        /// Optionally specifies whether to wait for the canceled child workflow to be ended
        /// before returning to the parent.  This defaults to <c>false</c>.
        /// WaitForCancellation - Whether to wait for cancelled child workflow to be ended (child workflow can be ended
        /// as: completed/failed/timedout/terminated/canceled)
        /// Optional: default false
        /// </summary>
        public bool WaitForCancellation { get; set; } = false;

        /// <summary>
        /// Controls how Cadence handles workflows that attempt to reuse workflow IDs.
        /// This defaults to <see cref="WorkflowIDReusePolicy.WorkflowIDReusePolicyAllowDuplicateFailedOnly"/>.
        /// </summary>
        public int WorkflowIdReusePolicy { get; set; } = (int)WorkflowIDReusePolicy.WorkflowIDReusePolicyAllowDuplicateFailedOnly;

        /// <summary>
        /// Optionally specifies a retry policy.
        /// </summary>
        public InternalRetryPolicy RetryPolicy { get; set; } = null;

        /// <summary>
        /// Optionally specifies a recurring schedule for the workflow.  See <see cref="CronSchedule"/>
        /// for more information.
        /// </summary>
        public CronSchedule CronSchedule { get; set; }

        /// <summary>
        /// Converts this instance into the corresponding internal object.
        /// </summary>
        /// <returns>The equivalent <see cref="InternalChildWorkflowOptions"/>.</returns>
        internal InternalChildWorkflowOptions ToInternal()
        {
            return new InternalChildWorkflowOptions()
            {
                Domain                       = this.Domain,
                WorkflowID                   = this.WorkflowID,
                TaskList                     = this.TaskList,
                ExecutionStartToCloseTimeout = this.ExecutionStartToCloseTimeout.Ticks * 100,
                TaskStartToCloseTimeout      = this.TaskStartToCloseTimeout.Ticks * 100,
                ChildPolicy                  = (int)this.ChildPolicy,
                WaitForCancellation          = this.WaitForCancellation,
                WorkflowIdReusePolicy        = (int)this.WorkflowIdReusePolicy,
                RetryPolicy                  = this.RetryPolicy,
                CronSchedule                 = this.CronSchedule.ToInternal()
            };
        }
    }
}
