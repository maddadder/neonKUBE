﻿//-----------------------------------------------------------------------------
// FILE:	    TlsCertificate.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using Neon.Common;
using Neon.IO;

// $todo(jeff.lill): 
//
// Support construction from an X509Certificate instance once
// .NET Standard 2.0 is released.

namespace Neon.Cryptography
{
    /// <summary>
    /// Holds the public and private parts of a TLS certificate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class follows the <b>HAProxy</b> convention of allowing the PEM encoded public certificate
    /// and private key to be encoded into a single text file by simply concatenating the public
    /// certificate with the private key, certificate first.
    /// </para>
    /// <note>
    /// The certificate part must include any intermediate certificates issues by the certificate
    /// authority after the certificate and before the private key.
    /// </note>
    /// </remarks>
    public class TlsCertificate
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Loads a combined public certificate and private key from a PEM encoded
        /// text file.
        /// </summary>
        /// <param name="pemCombinedPath">Path to the source file.</param>
        /// <returns>The parsed <see cref="TlsCertificate"/>.</returns>
        public static TlsCertificate Load(string pemCombinedPath)
        {
            return new TlsCertificate(File.ReadAllText(pemCombinedPath));
        }

        /// <summary>
        /// Loads a combined public certificate and private key from two PEM encoded
        /// files.
        /// </summary>
        /// <param name="certPath">Path to the public certificate PEM file.</param>
        /// <param name="keyPath">Path to the private key PEM file.</param>
        /// <returns>The parsed <see cref="TlsCertificate"/>.</returns>
        public static TlsCertificate Load(string certPath, string keyPath)
        {
            return new TlsCertificate(File.ReadAllText(certPath), File.ReadAllText(keyPath));
        }

        /// <summary>
        /// Parses a certificate and private key from PEM encoded text.
        /// </summary>
        /// <param name="pemCombined">The PEM encoded certificate and private key.</param>
        /// <returns>The parsed <see cref="TlsCertificate"/>.</returns>
        /// <exception cref="FormatException">Thrown if the certificate could not be parsed.</exception>
        public static TlsCertificate Parse(string pemCombined)
        {
            var certificate = new TlsCertificate(pemCombined);

            certificate.Parse();

            return certificate;
        }

        /// <summary>
        /// Attempts to parse a certificate and private key from PEM encoded text.
        /// </summary>
        /// <param name="pemCombined">The PEM encoded certificate and private key.</param>
        /// <param name="certificate">Returns as the parsed certificate.</param>
        /// <returns><c>true</c> if the certificate was parsed successfully.</returns>
        public static bool TryParse(string pemCombined, out TlsCertificate certificate)
        {
            try
            {
                certificate = TlsCertificate.Parse(pemCombined);

                return true;
            }
            catch
            {
                certificate = default(TlsCertificate);

                return false;
            }
        }

        /// <summary>
        /// Normalizes PEM encoded text to have Linux style (LF) line endings.
        /// </summary>
        /// <param name="input">The input text.</param>
        /// <returns>The normalized text.</returns>
        public static string NormalizePem(string input)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(input));

            // Strip out any blank lines (some services like Namecheap.com will
            // generate certificates with blank lines between the certificates
            // in the CA bunbdle.

            var sb = new StringBuilder();

            using (var reader = new StringReader(input))
            {
                foreach (var line in reader.Lines())
                {
                    var l = line.Trim();

                    if (l.Length > 0)
                    {
                        sb.AppendLine(l);
                    }
                }
            }

            input = sb.ToString();

            // Convert to Linux style line endings and ensure that the
            // last line is terminated with a LF.

            var output = NeonHelper.ToLinuxLineEndings(input);

            if (output.EndsWith("\n"))
            {
                return output;
            }
            else
            {
                return output + "\n";
            }
        }

        /// <summary>
        /// Generates a self-signed certificate for a hostname.
        /// </summary>
        /// <param name="hostname">
        /// <para>
        /// The hostname.
        /// </para>
        /// <note>
        /// You can use include a <b>"*"</b> to specify a wildcard
        /// certificate like: <b>*.test.com</b>.
        /// </note>
        /// </param>
        /// <param name="bitCount">The certificate key size in bits: one of <b>1024</b>, <b>2048</b>, or <b>4096</b> (defaults to <b>2048</b>).</param>
        /// <param name="validDays">
        /// The number of days the certificate will be valid.  This defaults to 365,000 days
        /// or about 1,000 years.
        /// </param>
        /// <param name="wildcard">
        /// Optionally generate a wildcard certificate for the subdomains of 
        /// <paramref name="hostname"/> or the combination of the subdomains
        /// and the hostname.  This defaults to <see cref="Wildcard.None"/>
        /// which does not generate a wildcard certificate.
        /// </param>
        /// <returns>The <see cref="TlsCertificate"/>.</returns>
        public static TlsCertificate CreateSelfSigned(string hostname, int bitCount = 2048, int validDays = 365000, Wildcard wildcard = Wildcard.None)
        {
            Covenant.Requires<ArgumentException>(!string.IsNullOrEmpty(hostname));
            Covenant.Requires<ArgumentException>(bitCount == 1024 || bitCount == 2048 || bitCount == 4096);
            Covenant.Requires<ArgumentException>(validDays > 1);

            var tempFolder   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var certPath     = Path.Combine(tempFolder, "cache.crt");
            var keyPath      = Path.Combine(tempFolder, "cache.key");
            var combinedPath = Path.Combine(tempFolder, "combined.pem");
            var hostnames    = new List<string>();

            Directory.CreateDirectory(tempFolder);

            switch (wildcard)
            {
                case Wildcard.None:

                    hostnames.Add(hostname);
                    break;

                case Wildcard.SubdomainsOnly:

                    hostname = $"*.{hostname}";
                    hostnames.Add(hostname);
                    break;

                case Wildcard.RootAndSubdomains:

                    hostnames.Add(hostname);
                    hostnames.Add($"*.{hostname}");
                    break;
            }

            try
            {
                // We need to specify [Subject Alternative Names] 
                // because specifying the hostname as the [Common Name]
                // is deprecated by the IETF and CA/Browser Forums.
                //
                // The latest OpenSSL release candidate for (v1.1.1) includes 
                // a new command line option for this but the current release
                // does not, so we're going to generate a temporary config
                // file specifiying this.

                var configPath = Path.Combine(tempFolder, "cert.conf");
                var sbConfig   = new StringBuilder();
                var sbAltNames = new StringBuilder();

                foreach (var name in hostnames)
                {
                    sbAltNames.AppendWithSeparator($"DNS:{name}", ", ");
                }

                sbConfig.Append(
$@"
[req]
default_bits       = 2048
prompt             = no
default_md         = sha256
distinguished_name = dn

[dn]
C=US
ST=.
L=.
O=.
OU=.
CN={hostname}

[san]
subjectAltName = {sbAltNames}
");

                sbConfig.AppendLine();

                File.WriteAllText(configPath, NeonHelper.ToLinuxLineEndings(sbConfig.ToString()));

                var result = NeonHelper.ExecuteCaptureStreams("openssl",
                    $"req -newkey rsa:{bitCount} -nodes -sha256 -x509 -days {validDays} " +
                    $"-subj \"/C=--/ST=./L=./O=./CN={hostname}\" " +
                    $"-reqexts san " +
                    $"-extensions san " +
                    $"-keyout \"{keyPath}\" " +
                    $"-out \"{certPath}\" " +
                    $"-config \"{configPath}\"");

                if (result.ExitCode != 0)
                {
                    throw new Exception($"Certificate Error: {result.ErrorText}");
                }

                var certPem     = File.ReadAllText(certPath) + File.ReadAllText(keyPath);
                var certificate = TlsCertificate.Parse(certPem);

                return certificate;
            }
            catch (Win32Exception e)
            {
                throw new Exception($"Cannot find the [openssl] utility on the PATH.", e);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        /// <summary>
        /// Verifies a certificate file.
        /// </summary>
        /// <param name="path">Path to the certificate.</param>
        /// <exception cref="ArgumentException">Thrown if the certificate is not valid.</exception>
        public static void Validate(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path));

            var certificate = TlsCertificate.Load(path);

            // We're going to split the certificate into two files, the issued
            // certificate and the certificate authority's certificate chain
            // (AKA the CA bundle).

            var tempCertPath = Path.GetTempFileName();
            var tempCaPath   = Path.GetTempFileName();
            var tool         = "openssl";

            try
            {
                var pos = certificate.CertPem.IndexOf("-----END CERTIFICATE-----");

                if (pos == -1)
                {
                    throw new ArgumentNullException("The certificate is not formatted properly.");
                }

                pos = certificate.CertPem.IndexOf("-----BEGIN CERTIFICATE-----", pos);

                var issuedCert = certificate.CertPem.Substring(0, pos);
                var caBundle   = certificate.CertPem.Substring(pos);

                File.WriteAllText(tempCertPath, issuedCert);
                File.WriteAllText(tempCaPath, caBundle);

                var sbArgs = new StringBuilder();

                // We're going to use [certutil] for Windows and [OpenSSL]
                // for everything else.

                if (NeonHelper.IsWindows)
                {
                    tool = "certutil";

                    sbArgs.Append("-verify ");
                    sbArgs.Append($"\"{tempCertPath}\" ");
                    sbArgs.Append($"\"{tempCaPath}\"");

                    var result = NeonHelper.ExecuteCaptureStreams("certutil", sbArgs.ToString());

                    if (result.ExitCode != 0)
                    {
                        throw new ArgumentException("Invalid certificate.");
                    }
                }
                else
                {
                    sbArgs.Append("verify ");
                    sbArgs.Append("-purpose sslserver ");
                    sbArgs.Append($"-CAfile \"{tempCaPath}\" ");
                    sbArgs.Append($"\"{tempCertPath}\"");

                    var result = NeonHelper.ExecuteCaptureStreams("openssl", sbArgs.ToString());

                    if (result.ExitCode != 0)
                    {
                        throw new ArgumentException("Invalid certificate.");
                    }
                }
            }
            catch (Win32Exception)
            {
                throw new ArgumentException($"INTERNAL ERROR: Cannot find the [{tool}] SSL certificate utility on the PATH.");
            }
            finally
            {
                File.Delete(tempCertPath);
                File.Delete(tempCaPath);
            }
        }

        //---------------------------------------------------------------------
        // Instance members

        /// <summary>
        /// Constructs an uninitialized certificate.
        /// </summary>
        public TlsCertificate()
        {
        }

        /// <summary>
        /// Constructs an instance by parsing the combined site certificate, any intermediate
        /// certificates followed by the private key as PEM encoded text.
        /// </summary>
        /// <param name="pemCombined">The certificate(s) followed by the private key text.</param>
        public TlsCertificate(string pemCombined)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(pemCombined));

            var certPos = pemCombined.IndexOf("-----BEGIN CERTIFICATE-----");
            var keyPos  = pemCombined.IndexOf("-----BEGIN PRIVATE KEY-----");

            if (certPos == -1)
            {
                throw new ArgumentException($"[{nameof(pemCombined)}] does not include a certificate.");
            }

            if (keyPos == -1)
            {
                throw new ArgumentException($"[{nameof(pemCombined)}] does not include a private key.");
            }

            if (keyPos < certPos)
            {
                throw new ArgumentException($"[{nameof(pemCombined)}] is improperly formatted: The private key must be after the certificate.");
            }

            if (certPos > 0)
            {
                throw new ArgumentException($"[{nameof(pemCombined)}] is improperly formatted: There is extra text before the certificate.");
            }

            CertPem = NormalizePem(pemCombined.Substring(0, keyPos));
            KeyPem  = NormalizePem(pemCombined.Substring(keyPos));
        }

        /// <summary>
        /// Constructs an instance by parsing the certificate and private key PEM encoded
        /// text passed.
        /// </summary>
        /// <param name="certPem">The certificate PEM text.</param>
        /// <param name="keyPem">The private key PEM text.</param>
        public TlsCertificate(string certPem, string keyPem)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(certPem));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(keyPem));

            if (!certPem.StartsWith("-----BEGIN CERTIFICATE-----"))
            {
                throw new ArgumentException($"[{nameof(certPem)}] is improperly formatted.");
            }

            if (!keyPem.StartsWith("-----BEGIN PRIVATE KEY-----"))
            {
                throw new ArgumentException($"[{nameof(keyPem)}] is improperly formatted.");
            }

            CertPem = NormalizePem(certPem);
            KeyPem  = NormalizePem(keyPem);
        }

        /// <summary>
        /// The public certificate as PEM encoded text.
        /// </summary>
        [JsonProperty(PropertyName = "CertPem", Required = Required.Always | Required.DisallowNull)]
        public string CertPem { get; set; }

        /// <summary>
        /// The private key as PEM encoded text.
        /// </summary>
        [JsonProperty(PropertyName = "KeyPem", Required = Required.Always | Required.DisallowNull)]
        public string KeyPem { get; set; }

        /// <summary>
        /// Returns the combined certificate and private key as PEM encoded text.
        /// </summary>
        [JsonIgnore]
        public string CombinedPem
        {
            get { return CertPem + KeyPem; }
        }

        /// <summary>
        /// Returns the combined certificate and private key as PEM encoded text normalized
        /// with Linux-style line endings for HAProxy compatability.
        /// </summary>
        /// <returns>The combined PEM coded certificate.</returns>
        public string CombinedNormalizedPem
        {
            get { return NormalizePem(CertPem + KeyPem); }
        }

        /// <summary>
        /// The date when the certificate becomes valid (or <c>null</c>).
        /// </summary>
        [JsonProperty(PropertyName = "ValidFrom", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public DateTime? ValidFrom { get; set; }

        /// <summary>
        /// The date when the certificate expires (or <c>null</c>).
        /// </summary>
        [JsonProperty(PropertyName = "ValidUntil", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public DateTime? ValidUntil { get; set; }

        /// <summary>
        /// The DNS hostnames covered by the certificate.  This may be a single or wildcard hostname
        /// extracted from the the certificate's <b>Common Name</b> or multiple hostname extracted
        /// from the <b>Subject Alternative Name</b> from a SAN certificate.  This list will be 
        /// <c>null</c> or empty if the hostname(s) are unknown.
        /// </summary>
        [JsonProperty(PropertyName = "Hosts", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(null)]
        public List<string> Hosts { get; set; } = new List<string>();

        /// <summary>
        /// Returns a deep copy of the instance.
        /// </summary>
        /// <returns>The clone.</returns>
        public TlsCertificate Clone()
        {
            var clone =
                new TlsCertificate()
                {
                    CertPem    = this.CertPem,
                    KeyPem     = this.KeyPem,
                    ValidFrom  = this.ValidFrom,
                    ValidUntil = this.ValidUntil
                };

            foreach (var host in this.Hosts)
            {
                clone.Hosts.Add(host);
            }

            return clone;
        }

        /// <summary>
        /// Returns the DNS hostnames covered by the certificate as a comma separated string.
        /// </summary>
        [JsonIgnore]
        public string HostNames
        {
            get
            {
                var sb = new StringBuilder();

                foreach (var host in Hosts)
                {
                    sb.AppendWithSeparator(host, ",");
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Determines whether the certificate is valid for the current or an optionally a specified date.
        /// </summary>
        /// <param name="dateTimeUtc">The optional date (UTC) to check.</param>
        /// <returns><c>true</c> if the certificate is valid for the date.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if either of <see cref="ValidFrom"/> or <see cref="ValidUntil"/> are <c>null</c>.
        /// </exception>
        public bool IsValidDate(DateTime? dateTimeUtc = null)
        {
            if (ValidFrom == null || ValidUntil == null)
            {
                throw new InvalidOperationException($"Cannot verify certificate validity when either of the [{nameof(ValidFrom)}] or [{nameof(ValidUntil)}] fields are NULL.");
            }

            dateTimeUtc = dateTimeUtc ?? DateTime.UtcNow;

            return ValidFrom <= dateTimeUtc && dateTimeUtc <= ValidUntil;
        }

        /// <summary>
        /// Determines whether the certificate is valid for a hostname.
        /// </summary>
        /// <param name="hostname">The hostname to validate.</param>
        /// <returns><c>true</c> if the certificate is valid for the hostname.</returns>
        /// <exception cref="InvalidOperationException"> Thrown if <see cref="Hosts"/> is <c>null</c> or empty.</exception>
        public bool IsValidHost(string hostname)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(hostname));

            if (ValidFrom == null || ValidUntil == null)
            {
                throw new InvalidOperationException($"Cannot verify certificate validity when either of the [{nameof(ValidFrom)}] or [{nameof(ValidUntil)}] fields are NULL.");
            }

            if (Hosts == null || Hosts.Count == 0)
            {
                throw new InvalidOperationException($"Cannot verify certificate validity when [{nameof(Hosts)}] is NULL or empty.");
            }

            foreach (var certHost in Hosts)
            {
                if (certHost.StartsWith("*."))
                {
                    // Wildcard:
                    //
                    // We're going to strip off the leading segment off of both certificate and
                    // test hostname up to the first dot and then compare what's left.

                    var certHostDotPos = certHost.IndexOf('.');
                    var hostDotPos     = hostname.IndexOf('.');

                    if (certHostDotPos == -1 || hostDotPos == -1)
                    {
                        throw new FormatException("Misformatted hostname or certificate host.");
                    }

                    if (string.Equals(certHost.Substring(certHostDotPos), hostname.Substring(hostDotPos), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;    // We have a match
                    }
                }
                else
                {
                    if (string.Equals(certHost, hostname, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;    // Wen have a match
                    }
                }
            }

            // The hostname specfied didn't match any of the certificate hosts.

            return false;
        }

        /// <summary>
        /// Extracts a field from a certificate dump.
        /// </summary>
        /// <param name="prefix">The field prefix.</param>
        /// <param name="info">The certificate dump.</param>
        /// <param name="throwOnNotFound">Can optionally disable not found error checking.</param>
        /// <returns>The extracted field or <c>null</c> if it was not found.</returns>
        /// <exception cref="FormatException">Thrown if the field wasn't found and error checking isn't disabled.</exception>
        private string ExtractField(string prefix, string info, bool throwOnNotFound = true)
        {
            var pos = info.IndexOf(prefix);

            if (pos == -1)
            {
                if (throwOnNotFound)
                {
                    throw new FormatException($"Certificate dump does not include the [{prefix}] field.");
                }
                else
                {
                    return null;
                }
            }

            pos += prefix.Length;

            // Extract up the the end of the line.

            var posEol = info.IndexOf('\n', pos);

            if (posEol == -1)
            {
                return info.Substring(pos).Trim();
            }
            else
            {
                return info.Substring(pos, posEol - pos).Trim();
            }
        }

        /// <summary>
        /// Extracts certificate properties such as <see cref="ValidFrom"/>, <see cref="ValidUntil"/>, and <see cref="Hosts"/> 
        /// from the dump output from the Windows <b>CertUtil.exe</b> tool (e.g. via <c>certutil -dump cert.pem</c>).
        /// </summary>
        /// <param name="info">The dumped certificate information.</param>
        /// <remarks>
        /// <note>
        /// Interesting Fact: <b>OpenSSL</b> and <b>CertUtil</b> report slightly different valid dates
        /// for the same certificate.  It appears that <b>CertUtil</b> reports the actual dates whereas
        /// <b>OpenSSL</b> rounds <see cref="ValidFrom"/> down to <b>00:00:00</b> and rounds <see cref="ValidUntil"/> 
        /// up to <b>23:59:59</b>.  I'm guessing OpenSSL is doing this to give clients that do not
        /// properly handle UTC conversions times some leaway at the beginning and end of a certficiate's life.
        /// </note>
        /// </remarks>
        public void ParseCertUtil(string info)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(info));

            const string datePattern = "M/d/yyyy h:mm tt";

            var notBefore = ExtractField("NotBefore:", info);
            var notAfter  = ExtractField("NotAfter:", info);

            var date = DateTime.ParseExact(notBefore, datePattern, CultureInfo.InvariantCulture);

            ValidFrom = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Utc);

            date = DateTime.ParseExact(notAfter, datePattern, CultureInfo.InvariantCulture);

            ValidUntil = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Utc);

            using (var reader = new StringReader(info))
            {
                var posSAN = info.IndexOf("Subject Alternative Name");

                if (posSAN != -1)
                {
                    // This is a SAN certificate so we'll extract the [DNS Name=*] lines.

                    // Skip lines up to the [Subject Alternative Name]

                    while (true)
                    {
                        var line = reader.ReadLine();

                        if (line.TrimStart().StartsWith("Subject Alternative Name"))
                        {
                            break;
                        }
                    }

                    // Read the following [DNS Name=] lines.

                    while (true)
                    {
                        var line = reader.ReadLine();

                        if (line == null || !line.TrimStart().StartsWith("DNS Name="))
                        {
                            break;
                        }

                        Hosts.Add(line.Substring(line.IndexOf('=') + 1));
                    }
                }
                else
                {
                    // This is a single host certificate, so we'll extract the [Subject/CN=*] value.

                    // Skip lines up to the [Subject:]

                    while (true)
                    {
                        var line = reader.ReadLine();

                        if (line.TrimStart().StartsWith("Subject:"))
                        {
                            break;
                        }
                    }

                    // Read the following lines until we see one that starts with [CN=]

                    while (true)
                    {
                        var line = reader.ReadLine();

                        if (line == null)
                        {
                            throw new FormatException("Invalid certificate dump: Subject/CN not found.");
                        }

                        if (line.TrimStart().StartsWith("CN="))
                        {
                            Hosts.Add(line.Substring(line.IndexOf('=') + 1));
                            break;
                        }
                    }
                }
            }

            if (Hosts.Count == 0)
            {
                throw new FormatException("Invalid certificate dump: No hostnames found.");
            }
        }

        /// <summary>
        /// Extracts certificate properties such as <see cref="ValidFrom"/>, <see cref="ValidUntil"/>, and <see cref="Hosts"/> 
        /// from the dump output from the <b>OpenSSL</b> tool (e.g. via <c>openssl x509 -in cert.pem -text</c>).
        /// </summary>
        /// <param name="info">The dumped certificate information.</param>
        /// <remarks>
        /// <note>
        /// Interesting Fact: <b>OpenSSL</b> and <b>CertUtil</b> report slightly different valid dates
        /// for the same certificate.  It appears that <b>CertUtil</b> reports the actual dates whereas
        /// <b>OpenSSL</b> rounds <see cref="ValidFrom"/> down to <b>00:00:00</b> and rounds <see cref="ValidUntil"/> 
        /// up to <b>23:59:59</b>.  I'm guessing OpenSSL is doing this to give clients that do not
        /// properly handle UTC conversions times some leaway at the beginning and end of a certficiate's life.
        /// </note>
        /// </remarks>
        public void ParseOpenSsl(string info)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(info));

            const string datePattern = "MMM d HH:mm:ss yyyy";

            var notBefore = ExtractField("Not Before:", info).Replace(" GMT", string.Empty);
            var notAfter  = ExtractField("Not After :", info).Replace(" GMT", string.Empty);

            // $hack(jeff.lill):
            //
            // OpenSSH on Linux includes an extra space for single digit day fields
            // like: "Feb  4 01:45:25 2117".  This causes ParseExact() to fail.  We're
            // going to work around this by converting the two spaces into a single space.

            notBefore = notBefore.Replace("  ", " ");
            notAfter  = notAfter.Replace("  ", " ");

            var date = DateTime.ParseExact(notBefore, datePattern, CultureInfo.InvariantCulture);

            ValidFrom = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Utc);

            date = DateTime.ParseExact(notAfter, datePattern, CultureInfo.InvariantCulture);

            ValidUntil = new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, DateTimeKind.Utc);

            using (var reader = new StringReader(info))
            {
                var posSAN = info.IndexOf("X509v3 Subject Alternative Name:");

                if (posSAN != -1)
                {
                    // This is a SAN certificate so we'll extract the comma separated [DNS:*] entries.

                    // Skip lines up to the [X509v3 Subject Alternative Name:]

                    while (true)
                    {
                        var line = reader.ReadLine();

                        if (line.TrimStart().StartsWith("X509v3 Subject Alternative Name:"))
                        {
                            break;
                        }
                    }

                    // The next line specifies the comma separated DNS hostnames.

                    var hostEntries = reader.ReadLine().Split(',');

                    foreach (var entry in hostEntries)
                    {
                        Hosts.Add(entry.Replace("DNS:", string.Empty).Trim());
                    }
                }
                else
                {
                    // This is a single host certificate, so we'll extract the [Subject/CN=*] value.

                    var subject = ExtractField("Subject:", info);
                    var fields  = subject.Split(',');

                    foreach (var field in fields)
                    {
                        var trimmed = field.Trim();

                        if (trimmed.StartsWith("CN="))
                        {
                            Hosts.Add(trimmed.Substring(3).Trim());
                            break;
                        }
                        else if (trimmed.StartsWith("CN = "))
                        {
                            Hosts.Add(trimmed.Substring(5).Trim());
                            break;
                        }
                    }
                }
            }

            if (Hosts.Count == 0)
            {
                throw new FormatException("Invalid certificate dump: No hostnames found.");
            }
        }

        /// <summary>
        /// Attempts to parse the certificate details.
        /// </summary>
        /// <exception cref="FormatException">Thrown if the certificate cannot be parsed.</exception>
        public void Parse()
        {
            // $todo(jeff.lill):
            //
            // Hacking this using the [CertUtil] and [OpenSSL] tools until the X509Certificate
            // class is implemented in .NET Standard 2.0.

            var tempCertPath = Path.GetTempFileName();
            var tool         = "openssl";

            try
            {
                File.WriteAllText(tempCertPath, this.CombinedNormalizedPem);

                var sbArgs = new StringBuilder();

                // We're going to use [CertUtil] for Windows and [OpenSSL]
                // for everything else.

                if (NeonHelper.IsWindows)
                {
                    tool = "certutil";

                    sbArgs.Append("-dump ");
                    sbArgs.Append($"\"{tempCertPath}\" ");

                    var result = NeonHelper.ExecuteCaptureStreams("certutil", sbArgs.ToString());

                    if (result.ExitCode != 0)
                    {
                        throw new FormatException($"Cannot parse certificate: {result.ErrorText}");
                    }

                    ParseCertUtil(result.OutputText);
                }
                else
                {
                    sbArgs.Append($"x509 -in \"{tempCertPath}\" ");
                    sbArgs.Append("-text");

                    var result = NeonHelper.ExecuteCaptureStreams("openssl", sbArgs.ToString());

                    if (result.ExitCode != 0)
                    {
                        throw new FormatException($"Cannot parse certificate: {result.ErrorText}");
                    }

                    ParseOpenSsl(result.OutputText);
                }
            }
            catch (Win32Exception)
            {
                throw new Exception($"Cannot find or execute the [{tool}] SSL certificate utility on the PATH.");
            }
            finally
            {
                File.Delete(tempCertPath);
            }
        }
    }
}