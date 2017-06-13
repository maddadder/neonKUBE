﻿#------------------------------------------------------------------------------
# FILE:         build.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2016-2017 by NeonForge, LLC.  All rights reserved.
#
# Builds the NeonCluster [neon-couchbase-manager] image.
#
# Usage: powershell -file build.ps1 VERSION [-latest]

param 
(
	[parameter(Mandatory=$True,Position=1)][string] $version,    # like: "1.0.0"
	[switch]$latest = $False
)

#----------------------------------------------------------
# Global Includes
$image_root = "$env:NF_ROOT\\Images"
. $image_root/includes.ps1
#----------------------------------------------------------

"   "
"======================================="
"* NEON-COUCHBASE-MANAGER " + $version
"======================================="

# Build and publish the [neon-couchbase-manager] to a local [bin] folder.

if (Test-Path bin)
{
	rm -r bin
}

Exec { mkdir bin }
Exec { dotnet publish "$src_services_path\\neon-couchbase-manager\\neon-couchbase-manager.csproj" -c Release -o "$pwd\bin" }

# Build the images.

$registry           = "neoncluster/neon-couchbase-manager";
$dockerTemplatePath = "Dockerfile.template";
$dockerFilePath     = "Dockerfile";

Exec { copy $dockerTemplatePath $dockerFilePath }
Exec { text replace-var "-VERSION=$version" $dockerFilePath }
Exec { docker build -f $dockerFilePath -t "${registry}:$version" . }

if ($latest)
{
	Exec { docker tag "${registry}:$version" "${registry}:latest"}
}

Exec { del $dockerFilePath }
Exec { rm -r bin }
