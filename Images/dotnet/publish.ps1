﻿#------------------------------------------------------------------------------
# FILE:         publish.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.
#
# Builds the [nhive/dotnet] images and pushes them to Docker Hub.
#
# NOTE: You must be logged into Docker Hub.
#
# Usage: powershell -file ./publish.ps1 [-all]

param 
(
	[switch]$all = $False,
    [switch]$nopush = $False
)

#----------------------------------------------------------
# Global Includes
$image_root = "$env:NF_ROOT\\Images"
. $image_root/includes.ps1
#----------------------------------------------------------

function Build
{
	param
	(
		[parameter(Mandatory=$True, Position=1)][string] $version,
		[switch]$latest = $False
	)

	$registry = "nhive/dotnet"
	$date     = UtcDate
	$branch   = GitBranch

	if (IsProd)
	{
		$tag = "$version-$date"
	}
	else
	{
		$tag = "$branch-$version"
	}

	# Build and publish the images.

	. ./build.ps1 -registry $registry -version $version -tag $tag
    PushImage "${registry}:$tag"

	if (IsProd)
	{
		Exec { docker tag "${registry}:$tag" "${registry}:$version" }
		PushImage "${registry}:$version"
	}

	if ($latest)
	{
		if (IsProd)
		{
			Exec { docker tag "${registry}:$tag" "${registry}:latest" }
			PushImage "${registry}:latest"
		}
		else
		{
			Exec { docker tag "${registry}:$tag" "${registry}:${branch}-latest" }
			PushImage "${registry}:${branch}-latest"
		}
	}
}

$noImagePush = $nopush

if ($all)
{
    # I'm not sure if these older .NET Core 2.0.x builds will work anymore
    # after we upgraded to 2.1.  There probably isn't a reason to rebuild
    # these again though, because neonHIVE was never released to the public
    # on .NET Core 2.1.
    #
	# Build 2.0.3-runtime
	# Build 2.0.4-runtime
	# Build 2.0.5-runtime -latest
}

Build 2.1
Build 2.1.3 -latest
