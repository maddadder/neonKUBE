﻿#Requires -Version 7.1.3 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         build.ps1
# CONTRIBUTOR:  Marcus Bowyer
# COPYRIGHT:    Copyright © 2005-2022 by NEONFORGE LLC.  All rights reserved.
#
# Builds the Neon [neon-node-agent] image.
#
# USAGE: pwsh -f build.ps1 REGISTRY VERSION TAG

param 
(
	[parameter(Mandatory=$True,Position=1)][string] $registry,
	[parameter(Mandatory=$True,Position=2)][string] $tag,
	[parameter(Mandatory=$True,Position=3)][string] $config = "Release"
)

$appname      = "neon-node-agent"
$organization = SdkRegistryOrg

# Build and publish the app to a local [bin] folder.

DeleteFolder bin

$result = mkdir bin
ThrowOnExitCode

dotnet publish "$nkServices\$appname\$appname.csproj" -c $config -o "$pwd\bin"
ThrowOnExitCode

# Split the build binaries into [__app] (application) and [__dep] dependency subfolders
# so we can tune the image layers.

core-layers $appname "$pwd\bin"
ThrowOnExitCode

# Build the image.

$result = Invoke-CaptureStreams "docker build -t ${registry}:${tag} --build-arg `"APPNAME=$appname`" --build-arg `"ORGANIZATION=$organization`" ." -interleave

# Clean up

DeleteFolder bin
