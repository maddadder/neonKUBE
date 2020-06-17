﻿//-----------------------------------------------------------------------------
// FILE:	    WorkflowInvokeReply.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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

using Neon.Common;
using Neon.Temporal;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// <b>proxy --> client:</b> Answers a <see cref="WorkflowInvokeRequest"/>
    /// </summary>
    [InternalProxyMessage(InternalMessageTypes.WorkflowInvokeReply)]
    internal class WorkflowInvokeReply : WorkflowReply
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public WorkflowInvokeReply()
        {
            Type = InternalMessageTypes.WorkflowInvokeReply;
        }

        /// <summary>
        /// The workflow execution result or <c>null</c>.
        /// </summary>
        public byte[] Result
        {
            get => GetBytesProperty(PropertyNames.Result);
            set => SetBytesProperty(PropertyNames.Result, value);
        }

        /// <summary>
        /// Indicates whether the workflow should be exited and then restarted,
        /// with an empty history.  This is useful for very long running looping
        /// workflows that would otherwise end up with very long task histories.
        /// </summary>
        public bool ContinueAsNew
        {
            get => GetBoolProperty(PropertyNames.ContinueAsNew);
            set => SetBoolProperty(PropertyNames.ContinueAsNew, value);
        }

        /// <summary>
        /// Specifies the arguments to use for the new workflow when 
        /// <see cref="ContinueAsNew"/> is <c>true</c>.
        /// </summary>
        public byte[] ContinueAsNewArgs
        {
            get => GetBytesProperty(PropertyNames.ContinueAsNewArgs);
            set => SetBytesProperty(PropertyNames.ContinueAsNewArgs, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's timeout for the restarted
        /// workflow when this value is greater than zero.
        /// </summary>
        public long ContinueAsNewExecutionStartToCloseTimeout
        {
            get => GetLongProperty(PropertyNames.ContinueAsNewExecutionStartToCloseTimeout);
            set => SetLongProperty(PropertyNames.ContinueAsNewExecutionStartToCloseTimeout, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's timeout for the restarted
        /// workflow when this value is greater than zero.
        /// </summary>
        public long ContinueAsNewScheduleToCloseTimeout
        {
            get => GetLongProperty(PropertyNames.ContinueAsNewScheduleToCloseTimeout);
            set => SetLongProperty(PropertyNames.ContinueAsNewScheduleToCloseTimeout, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's timeout for the restarted
        /// workflow when this value is greater than zero.
        /// </summary>
        public long ContinueAsNewScheduleToStartTimeout
        {
            get => GetLongProperty(PropertyNames.ContinueAsNewScheduleToStartTimeout);
            set => SetLongProperty(PropertyNames.ContinueAsNewScheduleToStartTimeout, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's timeout for the restarted
        /// workflow when this value is greater than zero.
        /// </summary>
        public long ContinueAsNewStartToCloseTimeout
        {
            get => GetLongProperty(PropertyNames.ContinueAsNewStartToCloseTimeout);
            set => SetLongProperty(PropertyNames.ContinueAsNewStartToCloseTimeout, value);
        }

        /// <summary>
        ///  Optionally overrides the current workflow scheduled for 
        ///  execution upon restart when this value is not <c>null</c>.
        /// </summary>
        public string ContinueAsNewWorkflow
        {
            get => GetStringProperty(PropertyNames.ContinueAsNewWorkflow);
            set => SetStringProperty(PropertyNames.ContinueAsNewWorkflow, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's task list for the restarted
        /// workflow when this value is not <c>null</c>.
        /// </summary>
        public string ContinueAsNewTaskList
        {
            get => GetStringProperty(PropertyNames.ContinueAsNewTaskList);
            set => SetStringProperty(PropertyNames.ContinueAsNewTaskList, value);
        }

        /// <summary>
        /// Optionally overrides the current workflow's namespace for the restarted
        /// workflow when this value is not <c>null</c>.
        /// </summary>
        public string ContinueAsNewNamespace
        {
            get => GetStringProperty(PropertyNames.ContinueAsNewNamespace);
            set => SetStringProperty(PropertyNames.ContinueAsNewNamespace, value);
        }

        /// <summary>
        /// Optionally indicates that the workflow should be aborted such that it
        /// will be restarted with decision tasks being replayed as required.  The
        /// <b>temporal-proxy</b> should do this via a GOLANG <b>panic("force-replay")</b>
        /// within the workflow function.
        /// </summary>
        public bool ForceReplay
        {
            get => GetBoolProperty(PropertyNames.ForceReplay);
            set => SetBoolProperty(PropertyNames.ForceReplay, value);
        }

        /// <inheritdoc/>
        internal override ProxyMessage Clone()
        {
            var clone = new WorkflowInvokeReply();

            CopyTo(clone);

            return clone;
        }

        /// <inheritdoc/>
        protected override void CopyTo(ProxyMessage target)
        {
            base.CopyTo(target);

            var typedTarget = (WorkflowInvokeReply)target;

            typedTarget.Result                                    = this.Result;
            typedTarget.ContinueAsNew                             = this.ContinueAsNew;
            typedTarget.ContinueAsNewArgs                         = this.ContinueAsNewArgs;
            typedTarget.ContinueAsNewExecutionStartToCloseTimeout = this.ContinueAsNewExecutionStartToCloseTimeout;
            typedTarget.ContinueAsNewTaskList                     = this.ContinueAsNewTaskList;
            typedTarget.ContinueAsNewNamespace                       = this.ContinueAsNewNamespace;
            typedTarget.ContinueAsNewWorkflow                     = this.ContinueAsNewWorkflow;
            typedTarget.ContinueAsNewScheduleToCloseTimeout       = this.ContinueAsNewScheduleToCloseTimeout;
            typedTarget.ContinueAsNewScheduleToStartTimeout       = this.ContinueAsNewScheduleToStartTimeout;
            typedTarget.ContinueAsNewStartToCloseTimeout          = this.ContinueAsNewStartToCloseTimeout;
            typedTarget.ForceReplay                               = this.ForceReplay;
        }
    }
}
