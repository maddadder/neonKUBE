﻿//-----------------------------------------------------------------------------
// FILE:	    GroupCommand.cs
// CONTRIBUTOR: Marcus Bowyer
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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;

using Newtonsoft.Json;

namespace Neon.SignalR
{
    internal class GroupCommand
    {
        /// <summary>
        /// The ID of the group command.
        /// </summary>
        [JsonProperty(PropertyName = "Id", Required = Required.Always, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public int Id { get; set; }

        /// <summary>
        /// The name of the server that sent the command.
        /// </summary>
        [JsonProperty(PropertyName = "ServerName", Required = Required.Always, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string ServerName { get; set; } = null;

        /// <summary>
        /// The action to be performed on the group.
        /// </summary>
        [JsonProperty(PropertyName = "Action", Required = Required.Always, DefaultValueHandling = DefaultValueHandling.Include)]
        public GroupAction Action { get; set; }

        /// <summary>
        /// Gets the group on which the action is performed.
        /// </summary>
        [JsonProperty(PropertyName = "GroupName", Required = Required.Always, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string GroupName { get; set; } = null;

        /// <summary>
        /// Gets the ID of the connection to be added or removed from the group.
        /// </summary>
        [JsonProperty(PropertyName = "ConnectionId", Required = Required.Always, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public string ConnectionId { get; set; } = null;

        /// <summary>
        /// Writes a <see cref="GroupCommand"/> to a byte[].
        /// </summary>
        /// <param name="id"></param>
        /// <param name="serverName"></param>
        /// <param name="action"></param>
        /// <param name="groupName"></param>
        /// <param name="connectionId"></param>
        /// <returns></returns>
        public static byte[] Write(int id, string serverName, GroupAction action, string groupName, string connectionId)
        {
            var command = new GroupCommand()
            {
                Id           = id,
                ServerName   = serverName,
                Action       = action,
                GroupName    = groupName,
                ConnectionId = connectionId
            };

            var s = NeonHelper.JsonSerialize(command);

            return NeonHelper.JsonSerializeToBytes(command);
        }

        /// <summary>
        /// Reads an <see cref="GroupCommand"/> from a byte array.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static GroupCommand Read(byte[] message)
        {
            var s = NeonHelper.JsonDeserialize<dynamic>(message);
            return NeonHelper.JsonDeserialize<GroupCommand>(message);
        }
    }
}
