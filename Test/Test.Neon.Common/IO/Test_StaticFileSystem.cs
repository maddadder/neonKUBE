﻿//-----------------------------------------------------------------------------
// FILE:	    Test_StaticFileSystem.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE LLC.  All rights reserved.
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;
using Neon.Xunit;

using Xunit;

namespace TestCommon
{
    // IMPLEMENTATION NOTE:
    // --------------------
    // We're going to combine testing of the [StaticDirectoryBase] and [StaticFileBase]
    // together with the [Assembly.GetResourceFileSystem()] extension method and related
    // internal classes.
    //
    // This will kill two birds with one stone and is an honest test anyway.  The resource
    // file system will be rooted at [Test.Neon.Common/Resources] and the virtual file
    // structure should look like this:
    // 
    //      /
    //          TextFile1.txt
    //          TextFile2.txt
    //
    //          Folder1/
    //              TextFile3.txt
    //              TextFile4.txt
    //
    //              Folder3/
    //                  TextFile5.txt
    //
    //          Folder2/
    //              TextFile6.txt
    //              TextFile7.txt
    //
    //              Folder4/
    //                  TextFile8.txt
    //
    // The text files will each have 10 lines of UTF-8 text like:
    //
    //      TextFile#.txt:
    //      Line 1
    //      Line 2
    //      Line 3
    //      Line 4
    //      Line 5
    //      Line 6
    //      Line 7
    //      Line 8
    //      Line 9
    //
    // When "#" will match the number in the file's name.

    // $todo(jefflill): I'm only testing UTF-8 encoding at this time.

    public class Test_StaticFileSystem
    {
        public Test_StaticFileSystem()
        {
        }

        //---------------------------------------------------------------------
        // Tests that don't filter resource names.

        [Fact]
        public void All_Load()
        {
            // Verify that an unfiltered filesystem has the directories and files that we expect.

            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem();
            var directory = fs;

            // Directory: /

            Assert.Single(directory.GetDirectories());
            Assert.Contains("TestCommon", directory.GetDirectories().Select(directory => directory.Name));
            Assert.Empty(directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            Assert.Contains("IO", directory.GetDirectories().Select(directory => directory.Name));
            Assert.Empty(directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/IO/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            Assert.Single(directory.GetDirectories());
            Assert.Contains("Resources", directory.GetDirectories().Select(directory => directory.Name));
            Assert.Empty(directory.GetFiles());

            // Directory: /TestCommon/IO/Resources/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            Assert.Equal(2, directory.GetDirectories().Count());
            Assert.Contains("Folder1", directory.GetDirectories().Select(directory => directory.Name));
            Assert.Contains("Folder2", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile1.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile2.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/IO/Resources/Folder1/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder1")
                .Single();

            Assert.Single(directory.GetDirectories());
            Assert.Contains("Folder3", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile3.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile4.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/IO/Resources/Folder1/Folder3/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder1")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder3")
                .Single();

            Assert.Empty(directory.GetDirectories());

            Assert.Single(directory.GetFiles());
            Assert.Contains("TextFile5.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/IO/Resources/Folder2/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder2")
                .Single();

            Assert.Single(directory.GetDirectories());
            Assert.Contains("Folder4", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile6.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile7.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /TestCommon/IO/Resources/Folder2/Folder4/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder2")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder4")
                .Single();

            Assert.Empty(directory.GetDirectories());

            Assert.Single(directory.GetFiles());
            Assert.Contains("TextFile8.txt", directory.GetFiles().Select(file => file.Name));
        }

        [Fact]
        public void All_List_Files()
        {
            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem();
            var directory = fs;

            // Directory: /

            Assert.Empty(directory.GetFiles());

            // Directory: /TestCommon/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            // Directory: /TestCommon/IO/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "TestCommon")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "IO")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Resources")
                .Single();

            var files = directory.GetFiles();

            Assert.Equal(2, files.Count());
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));
            Assert.Contains("TextFile2.txt", files.Select(file => file.Name));

            // Specific file.

            files = directory.GetFiles("TextFile1.txt");

            Assert.Single(files);
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));

            // Pattern match.

            files = directory.GetFiles("*1.*");

            Assert.Single(files);
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));
        }

        [Fact]
        public void All_List_Files_Recursively()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem();

            // List from root.

            var files = fs.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TestCommon/IO/Resources/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // Pattern match

            files = fs.GetFiles(searchPattern: "TextFile3.txt", options: SearchOption.AllDirectories);

            Assert.Single(files);
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile3.txt", files.Select(file => file.Path));

            files = fs.GetFiles(searchPattern: "*.txt", options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TestCommon/IO/Resources/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // List from a subdirectory.

            var directory = fs.GetDirectory("/TestCommon/IO/Resources/Folder1");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));

            // Extra test to ensure that an extra trailing "/" in a directory path is ignored.

            directory = fs.GetDirectory("/TestCommon/IO/Resources/Folder1/");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
        }

        [Fact]
        public void All_List_Directories()
        {
            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem();
            var directory = fs;

            // Directory: /

            Assert.Single(directory.GetDirectories("TestCommon"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /TestCommon/

            directory = fs.GetDirectories("TestCommon").Single();

            Assert.Single(directory.GetDirectories("IO"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /TestCommon/IO/

            directory = fs.GetDirectories("TestCommon").Single();
            directory = directory.GetDirectories("IO").Single();

            Assert.Single(directory.GetDirectories("Resources"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /TestCommon/IO/Resources/

            directory = fs.GetDirectories("TestCommon").Single();
            directory = directory.GetDirectories("IO").Single();
            directory = directory.GetDirectories("Resources").Single();

            Assert.Single(directory.GetDirectories("Folder1"));
            Assert.Single(directory.GetDirectories("Folder2"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /TestCommon/IO/Resources/Folder1/

            directory = fs.GetDirectories("TestCommon").Single();
            directory = directory.GetDirectories("IO").Single();
            directory = directory.GetDirectories("Resources").Single();
            directory = directory.GetDirectories("Folder1").Single();

            Assert.Single(directory.GetDirectories("Folder3"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /TestCommon/IO/Resources/Folder2/

            directory = fs.GetDirectories("TestCommon").Single();
            directory = directory.GetDirectories("IO").Single();
            directory = directory.GetDirectories("Resources").Single();
            directory = directory.GetDirectories("Folder2").Single();

            Assert.Single(directory.GetDirectories("Folder4"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));
        }

        [Fact]
        public void All_List_Directories_Recursively()
        {
            var fs          = Assembly.GetExecutingAssembly().GetResourceFileSystem();
            var directories = fs.GetDirectories(searchPattern: null, options: SearchOption.AllDirectories);

            Assert.Equal(7, directories.Count());
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1/Folder3"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2/Folder4"));
        }

        [Fact]
        public void All_List_Directories_Recursively_WithFilter()
        {
            // Filter by: *

            var fs          = Assembly.GetExecutingAssembly().GetResourceFileSystem();
            var directories = fs.GetDirectories("*", SearchOption.AllDirectories);

            Assert.Equal(7, directories.Count());
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1/Folder3"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2/Folder4"));

            // Filter by: *.*
            
            directories = fs.GetDirectories("*.*", SearchOption.AllDirectories);

            Assert.Equal(7, directories.Count());
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1/Folder3"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2/Folder4"));

            // Filter by: F*

            directories = fs.GetDirectories("F*", SearchOption.AllDirectories);

            Assert.Equal(4, directories.Count());
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1/Folder3"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2/Folder4"));

            // Filter by: Folder?

            directories = fs.GetDirectories("Folder?", SearchOption.AllDirectories);

            Assert.Equal(4, directories.Count());
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder1/Folder3"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2"));
            Assert.Single(directories.Where(directory => directory.Path == "/TestCommon/IO/Resources/Folder2/Folder4"));
        }

        [Fact]
        public void All_GetFile()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem();

            // From the root directory.

            Assert.Equal("/TestCommon/IO/Resources/TextFile1.txt", fs.GetFile("/TestCommon/IO/Resources/TextFile1.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/TextFile2.txt", fs.GetFile("/TestCommon/IO/Resources/TextFile2.txt").Path);

            Assert.Equal("/TestCommon/IO/Resources/Folder1/TextFile3.txt", fs.GetFile("/TestCommon/IO/Resources/Folder1/TextFile3.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/TextFile4.txt", fs.GetFile("/TestCommon/IO/Resources/Folder1/TextFile4.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", fs.GetFile("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt").Path);

            Assert.Equal("/TestCommon/IO/Resources/Folder2/TextFile6.txt", fs.GetFile("/TestCommon/IO/Resources/Folder2/TextFile6.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/TextFile7.txt", fs.GetFile("/TestCommon/IO/Resources/Folder2/TextFile7.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt", fs.GetFile("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt").Path);

            Assert.Throws<FileNotFoundException>(() => fs.GetFile("/TestCommon/IO/Resources/Folder2/NOT-FOUND.txt"));

            // From a subdirectory.

            var directory = fs.GetDirectory("/TestCommon/IO/Resources/Folder2/Folder4");

            Assert.Equal("/TestCommon/IO/Resources/TextFile1.txt", directory.GetFile("/TestCommon/IO/Resources/TextFile1.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/TextFile2.txt", directory.GetFile("/TestCommon/IO/Resources/TextFile2.txt").Path);

            Assert.Equal("/TestCommon/IO/Resources/Folder1/TextFile3.txt", directory.GetFile("/TestCommon/IO/Resources/Folder1/TextFile3.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/TextFile4.txt", directory.GetFile("/TestCommon/IO/Resources/Folder1/TextFile4.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt", fs.GetFile("/TestCommon/IO/Resources/Folder1/Folder3/TextFile5.txt").Path);

            Assert.Equal("/TestCommon/IO/Resources/Folder2/TextFile6.txt", directory.GetFile("/TestCommon/IO/Resources/Folder2/TextFile6.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/TextFile7.txt", directory.GetFile("/TestCommon/IO/Resources/Folder2/TextFile7.txt").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt", directory.GetFile("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt").Path);

            // Relative path.

            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4/TextFile8.txt", directory.GetFile("TextFile8.txt").Path);

            // Not found.

            Assert.Throws<FileNotFoundException>(() => directory.GetFile("/TestCommon/IO/Resources/Folder2/NOT-FOUND.txt"));
        }

        [Fact]
        public void All_GetDirectory()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem();

            // From the root directory.

            Assert.Equal("/TestCommon", fs.GetDirectory("/TestCommon").Path);
            Assert.Equal("/TestCommon/IO", fs.GetDirectory("/TestCommon/IO").Path);
            Assert.Equal("/TestCommon/IO/Resources", fs.GetDirectory("/TestCommon/IO/Resources").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1", fs.GetDirectory("/TestCommon/IO/Resources/Folder1").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/Folder3", fs.GetDirectory("/TestCommon/IO/Resources/Folder1/Folder3").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2", fs.GetDirectory("/TestCommon/IO/Resources/Folder2").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4", fs.GetDirectory("/TestCommon/IO/Resources/Folder2/Folder4").Path);

            Assert.Throws<FileNotFoundException>(() => fs.GetDirectory("/TestCommon/IO/Resources/NOT-FOUND.txt"));

            // From a subdirectory.

            var directory = fs.GetDirectory("/TestCommon/IO/Resources/Folder2/Folder4");

            Assert.Equal("/TestCommon", directory.GetDirectory("/TestCommon").Path);
            Assert.Equal("/TestCommon/IO", directory.GetDirectory("/TestCommon/IO").Path);
            Assert.Equal("/TestCommon/IO/Resources", directory.GetDirectory("/TestCommon/IO/Resources").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1", directory.GetDirectory("/TestCommon/IO/Resources/Folder1").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder1/Folder3", directory.GetDirectory("/TestCommon/IO/Resources/Folder1/Folder3").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2", directory.GetDirectory("/TestCommon/IO/Resources/Folder2").Path);
            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4", directory.GetDirectory("/TestCommon/IO/Resources/Folder2/Folder4").Path);

            // Relative path.

            directory = fs.GetDirectory("/TestCommon/IO/Resources/Folder2");

            Assert.Equal("/TestCommon/IO/Resources/Folder2/Folder4", directory.GetDirectory("Folder4").Path);

            // Not found.

            Assert.Throws<FileNotFoundException>(() => fs.GetDirectory("/TestCommon/IO/Resources/NOT-FOUND.txt"));
        }

        //---------------------------------------------------------------------
        // Tests that use a prefix to extract only some of the resopurces.

        [Fact]
        public void Partial_Load()
        {
            // Verify that an filtered filesystem has the directories and files that we expect.

            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");
            var directory = fs;

            // Directory: /

            Assert.Equal(2, fs.GetDirectories().Count());
            Assert.Contains("Folder1", directory.GetDirectories().Select(directory => directory.Name));
            Assert.Contains("Folder2", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile1.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile2.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /Folder1/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "Folder1")
                .Single();

            Assert.Single(directory.GetDirectories());
            Assert.Contains("Folder3", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile3.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile4.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /Folder3/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "Folder1")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder3")
                .Single();

            Assert.Empty(directory.GetDirectories());

            Assert.Single(directory.GetFiles());
            Assert.Contains("TextFile5.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /Folder2/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "Folder2")
                .Single();

            Assert.Single(directory.GetDirectories());
            Assert.Contains("Folder4", directory.GetDirectories().Select(directory => directory.Name));

            Assert.Equal(2, directory.GetFiles().Count());
            Assert.Contains("TextFile6.txt", directory.GetFiles().Select(file => file.Name));
            Assert.Contains("TextFile7.txt", directory.GetFiles().Select(file => file.Name));

            // Directory: /Folder4/

            directory = fs.GetDirectories()
                .Where(directory => directory.Name == "Folder2")
                .Single();

            directory = directory.GetDirectories()
                .Where(directory => directory.Name == "Folder4")
                .Single();

            Assert.Empty(directory.GetDirectories());

            Assert.Single(directory.GetFiles());
            Assert.Contains("TextFile8.txt", directory.GetFiles().Select(file => file.Name));
        }

        [Fact]
        public void Partial_List_Files()
        {
            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");
            var directory = fs;

            var files = directory.GetFiles();

            Assert.Equal(2, files.Count());
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));
            Assert.Contains("TextFile2.txt", files.Select(file => file.Name));

            // Specific file.

            files = directory.GetFiles("TextFile1.txt");

            Assert.Single(files);
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));

            // Pattern match.

            files = directory.GetFiles("*1.*");

            Assert.Single(files);
            Assert.Contains("TextFile1.txt", files.Select(file => file.Name));
        }

        [Fact]
        public void Partial_List_Files_Recursively()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");

            // List from root.

            var files = fs.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // Pattern match

            files = fs.GetFiles(searchPattern: "TextFile3.txt", options: SearchOption.AllDirectories);

            Assert.Single(files);
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));

            files = fs.GetFiles(searchPattern: "*.txt", options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // List from a subdirectory.

            var directory = fs.GetDirectory("/Folder1");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));

            // Extra test to ensure that an extra trailing "/" in a directory path is ignored.

            directory = fs.GetDirectory("/Folder1/");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
        }

        [Fact]
        public void Partial_List_Directories()
        {
            var fs        = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");
            var directory = fs;

            // Directory: /Folder1/

            directory = fs.GetDirectories("Folder1").Single();

            Assert.Single(directory.GetDirectories("Folder3"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));

            // Directory: /Folder2/

            directory = fs.GetDirectories("Folder2").Single();

            Assert.Single(directory.GetDirectories("Folder4"));
            Assert.Empty(directory.GetDirectories("NOT-FOUND"));
        }

        [Fact]
        public void Partial_List_Directories_Recursively()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");

            // List from root.

            var files = fs.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // Pattern match

            files = fs.GetFiles(searchPattern: "TextFile3.txt", options: SearchOption.AllDirectories);

            Assert.Single(files);
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));

            files = fs.GetFiles(searchPattern: "*.txt", options: SearchOption.AllDirectories);

            Assert.Equal(8, files.Count());
            Assert.Contains("/TextFile1.txt", files.Select(file => file.Path));
            Assert.Contains("/TextFile2.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile6.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/TextFile7.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder2/Folder4/TextFile8.txt", files.Select(file => file.Path));

            // List from a subdirectory.

            var directory = fs.GetDirectory("/Folder1");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));

            // Extra test to ensure that an extra trailing "/" in a directory path is ignored.

            directory = fs.GetDirectory("/Folder1/");
            
            files = directory.GetFiles(options: SearchOption.AllDirectories);

            Assert.Equal(3, files.Count());
            Assert.Contains("/Folder1/TextFile3.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/TextFile4.txt", files.Select(file => file.Path));
            Assert.Contains("/Folder1/Folder3/TextFile5.txt", files.Select(file => file.Path));
        }

        [Fact]
        public void Partial_GetFile()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");

            // From the root directory.

            Assert.Equal("/TextFile1.txt", fs.GetFile("/TextFile1.txt").Path);
            Assert.Equal("/TextFile2.txt", fs.GetFile("/TextFile2.txt").Path);

            Assert.Equal("/Folder1/TextFile3.txt", fs.GetFile("/Folder1/TextFile3.txt").Path);
            Assert.Equal("/Folder1/TextFile4.txt", fs.GetFile("/Folder1/TextFile4.txt").Path);
            Assert.Equal("/Folder1/Folder3/TextFile5.txt", fs.GetFile("/Folder1/Folder3/TextFile5.txt").Path);

            Assert.Equal("/Folder2/TextFile6.txt", fs.GetFile("/Folder2/TextFile6.txt").Path);
            Assert.Equal("/Folder2/TextFile7.txt", fs.GetFile("/Folder2/TextFile7.txt").Path);
            Assert.Equal("/Folder2/Folder4/TextFile8.txt", fs.GetFile("/Folder2/Folder4/TextFile8.txt").Path);

            Assert.Throws<FileNotFoundException>(() => fs.GetFile("/Folder2/NOT-FOUND.txt"));

            // From a subdirectory.

            var directory = fs.GetDirectory("/Folder2/Folder4");

            Assert.Equal("/TextFile1.txt", directory.GetFile("/TextFile1.txt").Path);
            Assert.Equal("/TextFile2.txt", directory.GetFile("/TextFile2.txt").Path);

            Assert.Equal("/Folder1/TextFile3.txt", directory.GetFile("/Folder1/TextFile3.txt").Path);
            Assert.Equal("/Folder1/TextFile4.txt", directory.GetFile("/Folder1/TextFile4.txt").Path);
            Assert.Equal("/Folder1/Folder3/TextFile5.txt", fs.GetFile("/Folder1/Folder3/TextFile5.txt").Path);

            Assert.Equal("/Folder2/TextFile6.txt", directory.GetFile("/Folder2/TextFile6.txt").Path);
            Assert.Equal("/Folder2/TextFile7.txt", directory.GetFile("/Folder2/TextFile7.txt").Path);
            Assert.Equal("/Folder2/Folder4/TextFile8.txt", directory.GetFile("/Folder2/Folder4/TextFile8.txt").Path);

            // Relative path.

            Assert.Equal("/Folder2/Folder4/TextFile8.txt", directory.GetFile("TextFile8.txt").Path);

            // Not found.

            Assert.Throws<FileNotFoundException>(() => directory.GetFile("/Folder2/NOT-FOUND.txt"));
        }

        [Fact]
        public void Partial_GetDirectory()
        {
            var fs = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");

            // From the root directory.

            Assert.Equal("", fs.GetDirectory("/").Path);
            Assert.Equal("/Folder1", fs.GetDirectory("/Folder1").Path);
            Assert.Equal("/Folder1/Folder3", fs.GetDirectory("/Folder1/Folder3").Path);
            Assert.Equal("/Folder2", fs.GetDirectory("/Folder2").Path);
            Assert.Equal("/Folder2/Folder4", fs.GetDirectory("/Folder2/Folder4").Path);

            Assert.Throws<FileNotFoundException>(() => fs.GetDirectory("//NOT-FOUND.txt"));

            // From a subdirectory.

            var directory = fs.GetDirectory("/Folder2/Folder4");

            Assert.Equal("/Folder1", directory.GetDirectory("/Folder1").Path);
            Assert.Equal("/Folder1/Folder3", directory.GetDirectory("/Folder1/Folder3").Path);
            Assert.Equal("/Folder2", directory.GetDirectory("/Folder2").Path);
            Assert.Equal("/Folder2/Folder4", directory.GetDirectory("/Folder2/Folder4").Path);

            // Relative path.

            directory = fs.GetDirectory("/Folder2");

            Assert.Equal("/Folder2/Folder4", directory.GetDirectory("Folder4").Path);

            // Not found.

            Assert.Throws<FileNotFoundException>(() => fs.GetDirectory("//NOT-FOUND.txt"));
        }

        //---------------------------------------------------------------------
        // Resource file read tests.

        [Fact]
        public void ReadAllText()
        {
            var fs   = Assembly.GetExecutingAssembly().GetResourceFileSystem("TestCommon.IO.Resources");
            var file = fs.GetFile("/TextFile1.txt");

            Assert.Equal(
@"TextFile1.txt:
Line 1
Line 2
Line 3
Line 4
Line 5
Line 6
Line 7
Line 8
Line 9
",
                file.ReadAllText());
        }

        [Fact]
        public async Task ReadAllTextAsync()
        {
        }

        [Fact]
        public void ReadAllBytes()
        {
        }

        [Fact]
        public async Task ReadAllBytesAsync()
        {
        }

        [Fact]
        public void OpenReader()
        {
        }

        [Fact]
        public async Task OpenReaderAsync()
        {
        }

        [Fact]
        public void OpenStream()
        {
        }

        [Fact]
        public async Task OpenStreamAsync()
        {
        }
    }
}
