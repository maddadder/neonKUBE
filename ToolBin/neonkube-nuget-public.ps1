#------------------------------------------------------------------------------
# FILE:         neon-nuget-public.ps1
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

# Publishes RELEASE builds of the NeonForge Nuget packages to the
# local file system and public Nuget.org repositories.

# Import the global project include file.

. $env:NF_ROOT/Powershell/includes.ps1

# Handle permission elevation if necessary.

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
        [string]$project,
        [Parameter(Position=1, Mandatory=2)]
        [string]$version
    )

    "$project"
	neon-build pack-version "$env:NF_ROOT\neonLIBRARY-version.txt" "$env:NF_ROOT\Lib\$project\$project.csproj"
    ThrowOnExitCode
}

function Publish
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$project,
        [Parameter(Position=1, Mandatory=2)]
        [string]$version
    )

	dotnet pack "$env:NF_ROOT\Lib\$project\$project.csproj" -c Release -o "$env:NF_BUILD\nuget"

    if (Test-Path "$env:NF_ROOT\Lib\$project\prerelease.txt")
    {
        $prerelease = Get-Content "$env:NF_ROOT\Lib\$project\prerelease.txt" -First 1
        $prerelease = $prerelease.Trim()

        if ($prerelease -ne "")
        {
            $prerelease = "-" + $prerelease
        }
    }
    else
    {
        $prerelease = ""
    }

	nuget push -Source nuget.org "$env:NF_BUILD\nuget\$project.$libraryVersion$prerelease.nupkg"
    ThrowOnExitCode
}

# Load the library and neonKUBE versions.

$libraryVersion  = Get-Content "$env:NF_ROOT\neonLIBRARY-version.txt" -First 1
$neonkubeVersion = Get-Content "$env:NF_ROOT\neonKUBE-version.txt" -First 1

# Copy the version from [$/product-version] into [$/Lib/Neon/Common/Build.cs]

neon-build build-version

# Update the project versions.

SetVersion Neon.Cadence             $libraryVersion
SetVersion Neon.Cassandra           $libraryVersion
SetVersion Neon.Common              $libraryVersion
SetVersion Neon.Couchbase           $libraryVersion
SetVersion Neon.Cryptography        $libraryVersion
SetVersion Neon.Docker              $libraryVersion
SetVersion Neon.HyperV              $libraryVersion
# SetVersion Neon.Kube                $neonkubeVersion
# SetVersion Neon.Kube.Aws            $neonkubeVersion
# SetVersion Neon.Kube.Azure          $neonkubeVersion
# SetVersion Neon.Kube.BareMetal      $neonkubeVersion
# SetVersion Neon.Kube.Google         $neonkubeVersion
# SetVersion Neon.Kube.Hosting        $neonkubeVersion
# SetVersion Neon.Kube.HyperV         $neonkubeVersion
# SetVersion Neon.Kube.HyperVLocal    $neonkubeVersion
# SetVersion Neon.Kube.Wsl2           $neonkubeVersion
# SetVersion Neon.Kube.XenServer      $neonkubeVersion
# SetVersion Neon.Kube.Xunit          $neonkubeVersion
SetVersion Neon.Service             $libraryVersion
SetVersion Neon.ModelGen            $libraryVersion
SetVersion Neon.ModelGenerator      $libraryVersion
SetVersion Neon.Nats                $libraryVersion
SetVersion Neon.Postgres            $libraryVersion
SetVersion Neon.SSH                 $libraryVersion
SetVersion Neon.SSH.NET             $libraryVersion
SetVersion Neon.Temporal            $libraryVersion
SetVersion Neon.Web                 $libraryVersion
SetVersion Neon.XenServer           $libraryVersion
SetVersion Neon.Xunit               $libraryVersion
SetVersion Neon.Xunit.Cadence       $libraryVersion
SetVersion Neon.Xunit.Couchbase     $libraryVersion
SetVersion Neon.Kube.Xunit          $libraryVersion
SetVersion Neon.Xunit.Temporal      $libraryVersion
SetVersion Neon.Xunit.YugaByte      $libraryVersion
SetVersion Neon.YugaByte            $libraryVersion

# Build and publish the projects.

Publish Neon.Cadence                $libraryVersion
Publish Neon.Cassandra              $libraryVersion
Publish Neon.Common                 $libraryVersion
Publish Neon.Couchbase              $libraryVersion
Publish Neon.Cryptography           $libraryVersion
Publish Neon.Docker                 $libraryVersion
Publish Neon.HyperV                 $libraryVersion
# Publish Neon.Kube                   $neonkubeVersion
# Publish Neon.Kube.Aws               $neonkubeVersion
# Publish Neon.Kube.Azure             $neonkubeVersion
# Publish Neon.Kube.BareMetal         $neonkubeVersion
# Publish Neon.Kube.Google            $neonkubeVersion
# Publish Neon.Kube.Hosting           $neonkubeVersion
# Publish Neon.Kube.HyperV            $neonkubeVersion
# Publish Neon.Kube.HyperVLocal       $neonkubeVersion
# Publish Neon.Kube.Wsl2              $neonkubeVersion
# Publish Neon.Kube.XenServer         $neonkubeVersion
# Publish Neon.Kube.Xunit             $neonkubeVersion
Publish Neon.Service                $libraryVersion
Publish Neon.ModelGen               $libraryVersion
Publish Neon.ModelGenerator         $libraryVersion
Publish Neon.Nats                   $libraryVersion
Publish Neon.Postgres               $libraryVersion
Publish Neon.SSH                    $libraryVersion
Publish Neon.SSH.NET                $libraryVersion
Publish Neon.Temporal               $libraryVersion
Publish Neon.Web                    $libraryVersion
Publish Neon.XenServer              $libraryVersion
Publish Neon.Xunit                  $libraryVersion
Publish Neon.Xunit.Cadence          $libraryVersion
Publish Neon.Xunit.Couchbase        $libraryVersion
Publish Neon.Xunit.Temporal         $libraryVersion
Publish Neon.Xunit.YugaByte         $libraryVersion
Publish Neon.YugaByte               $libraryVersion

""
"** Package publication completed"
""

