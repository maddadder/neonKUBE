﻿#------------------------------------------------------------------------------
# FILE:         publish.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2005-2020 by neonFORGE LLC.  All rights reserved.
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

# Builds the Test images and pushes them to Docker Hub.
#
# NOTE: You must be logged into Docker Hub.
#
# USAGE: pwsh -file ./publish.ps1 [-all]

param 
(
	[switch]$all = $false,
    [switch]$nopush = $false
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
		[parameter(Mandatory=$true, Position=1)][string] $version,
		[switch]$latest = $false
	)

	$registry    = GetLibraryRegistry "test"
	$tag         = $version
	$tagAsLatest = TagAsLatest

	# Build and publish the images.

	. ./build.ps1 -registry $registry -version $version -tag $tag
    PushImage "${registry}:$tag"

	if ($latest)
	{
		Exec { docker tag "${registry}:$tag" "${registry}:latest" }
		PushImage "${registry}:latest"
	}
}

$noImagePush = $nopush

# The image is tiny so we're going to always build
# all versions which will all be the same anyway.

Build 0
Build 1
Build 2 -latest
