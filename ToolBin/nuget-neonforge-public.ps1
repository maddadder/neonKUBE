#------------------------------------------------------------------------------
# FILE:         nuget-neonforge-public.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
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

# Publishes RELEASE builds of the NeonForge Nuget packages to the
# local file system and public Nuget.org repositories.

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    # Relaunch as an elevated process:
    Start-Process powershell.exe "-file",('"{0}"' -f $MyInvocation.MyCommand.Path) -Verb RunAs
    exit
}

function SetVersion
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$project
    )

	text pack-version "$env:NF_ROOT\product-version.txt" "$env:NF_ROOT\Lib\$project\$project.csproj"
}

function Publish
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$project
    )

	dotnet pack "$env:NF_ROOT\Lib\$project\$project.csproj" -c Release -o "$env:NF_build\nuget"

	$version = Get-Content "$env:NF_ROOT\product-version.txt" -First 1

	# $todo(jeff.lill):
    #
    # We need to use [nshell run ...]  to retrieve the API key from an encrypted
    # secrets file rather than depending on NUGET_API_KEY environment variable
    # always being set.
    #
    #   https://github.com/nforgeio/neonKUBE/issues/448

	nuget push -Source nuget.org "$env:NF_BUILD\nuget\$project.$version.nupkg"
}

# Update the project versions first.

SetVersion Neon.CodeGen
SetVersion Neon.Common
SetVersion Neon.Couchbase
SetVersion Neon.Cryptography
SetVersion Neon.Docker
SetVersion Neon.HyperV
SetVersion Neon.Kube
SetVersion Neon.Kube.Aws
SetVersion Neon.Kube.Azure
SetVersion Neon.Kube.Google
SetVersion Neon.Kube.Hosting
SetVersion Neon.Kube.HyperV
SetVersion Neon.Kube.HyperVLocal
SetVersion Neon.Kube.Machine
SetVersion Neon.Kube.Service
SetVersion Neon.Kube.XenServer
SetVersion Neon.Web
SetVersion Neon.XenServer
SetVersion Neon.Xunit
SetVersion Neon.Xunit.Kube

# Build and publish the projects.

Publish Neon.CodeGen
Publish Neon.Common
Publish Neon.Couchbase
Publish Neon.Cryptography
Publish Neon.Docker
Publish Neon.HyperV
Publish Neon.Kube
Publish Neon.Kube.Aws
Publish Neon.Kube.Azure
Publish Neon.Kube.Google
Publish Neon.Kube.Hosting
Publish Neon.Kube.HyperV
Publish Neon.Kube.HyperVLocal
Publish Neon.Kube.Machine
Publish Neon.Kube.Service
Publish Neon.Kube.XenServer
Publish Neon.Web
Publish Neon.XenServer
Publish Neon.Xunit
Publish Neon.Xunit.Kube
pause
