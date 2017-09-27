// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace embedder
{
    using System;
    using System.IO;
    using System.Text;
    using Newtonsoft.Json;

    public class License
    {
        public string Path { get; set; }
        public string Content { get; set; }

        public License() { }

        public License(string path, string fileContents)
        {
            this.Path = path;
            this.Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileContents));
        }
        public License(string path, byte[] fileContents)
        {
            this.Path = path;
            this.Content = Convert.ToBase64String(fileContents);
        }
    }

    public class LicenseData
    {
        public License[] Licenses { get; set; }

        public static void InjectIntoFilesystem(string environmentVariable)
        {
            var x = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrEmpty(x))
            {
                Console.Error.WriteLine($"No license files found in environment variable '{environmentVariable}'.");
                return;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(x));
            var licenses = JsonConvert.DeserializeObject<LicenseData>(json);
            foreach (var license in licenses.Licenses)
            {
                var payload = Convert.FromBase64String(license.Content);
                new FileInfo(license.Path).Directory.Create();
                File.WriteAllBytes(license.Path, payload);

                Console.WriteLine($"Installed license into file '{license.Path}'.");
            }
        }

        public override string ToString()
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this)));
        }

        public static string Generate { get; } = new LicenseData
            {
                Licenses = new[]
                {
                    // preprocessor license
                    new License(
                        path: "/usr/share/nexguardescreener-preprocessor/PayTVPreProcessorVideo.lic",
                        fileContents: "server=104.40.188.211\nport=5093\n"),
                        // fileContents: "server=10.240.0.6\nport=5093\n"),


                    // embedder license
                    new License(
                        path: "/usr/bin/NGStreamingSE.lic",
                        fileContents: File.ReadAllBytes(
                            @"C:\github\chgeuer\media_hack\Christian\SmartEmbedderCLI\NGStreamingSE.lic"))
                }
            }.ToString();
    }
}