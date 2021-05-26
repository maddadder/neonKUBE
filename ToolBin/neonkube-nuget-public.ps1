#Requires -Version 7.0 -RunAsAdministrator
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

# Import the global solution include file.

. $env:NF_ROOT/Powershell/includes.ps1

# This needs to run with elevated privileges.

Request-AdminPermissions

# Retrieve any necessary credentials.

$nugetApiKey = Get-SecretPassword "NUGET_PUBLIC_KEY"

#------------------------------------------------------------------------------
# Sets the package version in the specified project file.

function SetVersion
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=$true)]
        [string]$project,
        [Parameter(Position=1, Mandatory=$true)]
        [string]$version
    )

    "$project"
	neon-build pack-version NeonLibraryVersion "$env:NF_ROOT\Lib\$project\$project.csproj"
    ThrowOnExitCode
}

#------------------------------------------------------------------------------
# Builds and publishes the project packages.

function Publish
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=$true)]
        [string]$project,
        [Parameter(Position=1, Mandatory=$true)]
        [string]$version
    )

    $projectPath = [io.path]::combine($env:NF_ROOT, "Lib", "$project", "$project" + ".csproj")

	dotnet pack $projectPath -c Release -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -o "$env:NF_BUILD\nuget"
    ThrowOnExitCode

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

	nuget push -Source nuget.org -ApiKey $nugetApiKey "$env:NF_BUILD\nuget\$project.$libraryVersion$prerelease.nupkg"
    ThrowOnExitCode
}

# Load the library and neonKUBE versions.

$msbuild         = "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
$nfRoot          = "$env:NF_ROOT"
$nfSolution      = "$nfRoot\neonKUBE.sln"
$nfBuild         = "$env:NF_BUILD"
$nfLib           = "$nfRoot\Lib"
$nfTools         = "$nfRoot\Tools"
$nfToolBin       = "$nfRoot\ToolBin"
$libraryVersion  = $(& "$nfToolBin\neon-build" read-version "$nfLib/Neon.Common/Build.cs" NeonLibraryVersion)
$neonkubeVersion = $(& "$nfToolBin\neon-build" read-version "$nfLib/Neon.Common/Build.cs" NeonKubeVersion)

# We need to do a release solution build to ensure that any tools or other
# dependencies are built before we build and publish the individual packages.

Write-Host  ""
Write-Host  "*******************************************************************************"
Write-Host  "***                            BUILD SOLUTION                               ***"
Write-Host  "*******************************************************************************"
Write-Host  ""

& "$msbuild" "$nfSolution" -p:Configuration=Release -restore -m -verbosity:quiet

if (-not $?)
{
    throw "ERROR: BUILD FAILED"
}

# Update the project versions.

SetVersion Neon.Cadence             $libraryVersion
SetVersion Neon.Cassandra           $libraryVersion
SetVersion Neon.Common              $libraryVersion
SetVersion Neon.Couchbase           $libraryVersion
SetVersion Neon.Cryptography        $libraryVersion
SetVersion Neon.Deployment          $libraryVersion
SetVersion Neon.Docker              $libraryVersion
SetVersion Neon.HyperV              $libraryVersion
# SetVersion Neon.Kube                $neonkubeVersion
# SetVersion Neon.Kube.Aws            $neonkubeVersion
# SetVersion Neon.Kube.Azure          $neonkubeVersion
# SetVersion Neon.Kube.BareMetal      $neonkubeVersion
# SetVersion Neon.Kube.Google         $neonkubeVersion
# SetVersion Neon.Kube.Hosting        $neonkubeVersion
# SetVersion Neon.Kube.HyperV         $neonkubeVersion
# SetVersion Neon.Kube.Wsl2           $neonkubeVersion
# SetVersion Neon.Kube.HyperVLocal    $neonkubeVersion
# SetVersion Neon.Kube.Services       $neonkubeVersion
# SetVersion Neon.Kube.XenServer      $neonkubeVersion
# SetVersion Neon.Kube.Xunit          $neonkubeVersion
SetVersion Neon.Service             $libraryVersion
SetVersion Neon.ModelGen            $libraryVersion
SetVersion Neon.ModelGenerator      $libraryVersion
SetVersion Neon.Nats                $libraryVersion
SetVersion Neon.Postgres            $libraryVersion
SetVersion Neon.SSH                 $libraryVersion
SetVersion Neon.Temporal            $libraryVersion
SetVersion Neon.Web                 $libraryVersion
SetVersion Neon.XenServer           $libraryVersion
SetVersion Neon.Xunit               $libraryVersion
SetVersion Neon.Xunit.Cadence       $libraryVersion
SetVersion Neon.Xunit.Couchbase     $libraryVersion
SetVersion Neon.Xunit.Temporal      $libraryVersion
SetVersion Neon.Xunit.YugaByte      $libraryVersion
SetVersion Neon.YugaByte            $libraryVersion

# Build and publish the projects.

Publish Neon.Cadence                $libraryVersion
Publish Neon.Cassandra              $libraryVersion
Publish Neon.Common                 $libraryVersion
Publish Neon.Couchbase              $libraryVersion
Publish Neon.Cryptography           $libraryVersion
Publish Neon.Deployment             $libraryVersion
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
# Publish Neon.Kube.Services          $neonkubeVersion
# Publish Neon.Kube.Wsl2              $neonkubeVersion
# Publish Neon.Kube.XenServer         $neonkubeVersion
# Publish Neon.Kube.Xunit             $neonkubeVersion
Publish Neon.Service                $libraryVersion
Publish Neon.ModelGen               $libraryVersion
Publish Neon.ModelGenerator         $libraryVersion
Publish Neon.Nats                   $libraryVersion
Publish Neon.Postgres               $libraryVersion
Publish Neon.SSH                    $libraryVersion
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

