﻿#Requires -Version 7.0 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         build.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Builds the NATS-STREAMING image.
#
# USAGE: pwsh -file build.ps1 REGISTRY VERSION TAG

param 
(
	[parameter(Mandatory=$true,Position=1)][string] $registry,
	[parameter(Mandatory=$true,Position=2)][string] $version,
	[parameter(Mandatory=$true,Position=3)][string] $tag
)

Log-DebugLine "*** BUILD-0:"
Log-ImageBuild $registry $tag
Log-DebugLine "*** BUILD-1:"

Invoke-CaptureStreams "docker pull nats-streaming:$version-linux" -interleave
Log-DebugLine "*** BUILD-2:"
Log-DebugLine "*** BUILD-3:"

Invoke-CaptureStreams "docker build -t $registry:$tag --build-arg VERSION=$version ." -interleave
Log-DebugLine "*** BUILD-4:"
Log-DebugLine "*** BUILD-5:"
