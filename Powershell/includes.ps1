﻿#Requires -Version 7.0 -RunAsAdministrator
#------------------------------------------------------------------------------
# FILE:         includes.ps1
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

#------------------------------------------------------------------------------
# IMPORTANT:
#
# This file defines GitHub related Powershell functions and is intended for use
# in GitHub actions and other deployment related scenarios.  This file is intended
# to be shared/included across multiple GitHub repos and should never include
# repo-specific code.
#
# After modifying this file, you should take care to push any changes to the
# other repos where this file is present.

# Common error handling settings.

$ErrorActionPreference = "Stop"
$PSDefaultParameterValues['*:ErrorAction'] = 'Stop'

# Include all of the other global scripts.

$scriptPath   = $MyInvocation.MyCommand.Path
$scriptFolder = [System.IO.Path]::GetDirectoryName($scriptPath)

Push-Cwd $scriptFolder

. ./error-handling
. ./utility.ps1
. ./files.ps1
. ./git.ps1
. ./deployment.ps1
. ./github.ps1

Pop-Cwd
