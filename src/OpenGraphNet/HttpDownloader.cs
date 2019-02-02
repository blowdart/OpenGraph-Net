﻿namespace OpenGraphNet
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.IO.Compression;
    using System.Net;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    /// <summary>
    /// Http Downloader
    /// </summary>
    /// <remarks>
    /// Taken from http://stackoverflow.com/a/2700707
    /// </remarks>
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Reviewed. Suppression is OK here.")]
    [ExcludeFromCodeCoverage]
    public class HttpDownloader
    {
        /// <summary>
        /// The referrer
        /// </summary>
        private readonly string referrer;

        /// <summary>
        /// The user agent
        /// </summary>
        private readonly string userAgent;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpDownloader"/> class.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="referrer">The referrer.</param>
        /// <param name="userAgent">The user agent.</param>
        public HttpDownloader(Uri url, string referrer, string userAgent)
        {
            this.Encoding = Encoding.GetEncoding("ISO-8859-1");
            this.Url = url;
            this.userAgent = userAgent;
            this.referrer = referrer;
        }

        /// <summary>
        /// Gets or sets the encoding.
        /// </summary>
        /// <value>
        /// The encoding.
        /// </value>
        public Encoding Encoding { get; set; }

        /// <summary>
        /// Gets or sets the headers.
        /// </summary>
        /// <value>
        /// The headers.
        /// </value>
        public WebHeaderCollection Headers { get; set; }

        /// <summary>
        /// Gets or sets the URL.
        /// </summary>
        /// <value>
        /// The URL.
        /// </value>
        public Uri Url { get; set; }

        /// <summary>
        /// Gets the page.
        /// </summary>
        /// <returns>The content of the page</returns>
        public string GetPage()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.Url);
            if (!string.IsNullOrEmpty(this.referrer))
            {
                request.Referer = this.referrer;
            }

            if (!string.IsNullOrEmpty(this.userAgent))
            {
                request.UserAgent = this.userAgent;
            }

            // ReSharper disable once StringLiteralTypo
            request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                this.Headers = response.Headers;
                this.Url = response.ResponseUri;
                return this.ProcessContent(response);
            }
        }

        /// <summary>
        /// Gets the page asynchronously
        /// </summary>
        /// <returns>
        /// The content of the page
        /// </returns>
        public async Task<string> GetPageAsync()
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(this.Url);
            if (!string.IsNullOrEmpty(this.referrer))
            {
                request.Referer = this.referrer;
            }

            if (!string.IsNullOrEmpty(this.userAgent))
            {
                request.UserAgent = this.userAgent;
            }

            // ReSharper disable once StringLiteralTypo
            request.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");

            using (HttpWebResponse response = (HttpWebResponse)(await request.GetResponseAsync()))
            {
                this.Headers = response.Headers;
                this.Url = response.ResponseUri;
                return this.ProcessContent(response);
            }
        }

        /// <summary>
        /// Processes the content.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <returns>The HTML content</returns>
        /// <exception cref="InvalidOperationException">Response stream came back as null</exception>
        private string ProcessContent(HttpWebResponse response)
        {
            this.SetEncodingFromHeader(response);

            Stream s = response.GetResponseStream();

            if (s == null)
            {
                throw new InvalidOperationException("Response stream came back as null");
            }

            // ReSharper disable once StringLiteralTypo
            if (response.ContentEncoding.ToLower().Contains("gzip"))
            {
                s = new GZipStream(s, CompressionMode.Decompress);
            }
            else if (response.ContentEncoding.ToLower().Contains("deflate"))
            {
                s = new DeflateStream(s, CompressionMode.Decompress);
            }

            MemoryStream memStream = new MemoryStream();
            int bytesRead;
            byte[] buffer = new byte[0x1000];
            for (bytesRead = s.Read(buffer, 0, buffer.Length); bytesRead > 0; bytesRead = s.Read(buffer, 0, buffer.Length))
            {
                memStream.Write(buffer, 0, bytesRead);
            }

            s.Close();
            string html;
            memStream.Position = 0;
            using (StreamReader r = new StreamReader(memStream, this.Encoding))
            {
                html = r.ReadToEnd().Trim();
                html = this.CheckMetaCharSetAndReEncode(memStream, html);
            }

            return html;
        }

        /// <summary>
        /// Sets the encoding from header.
        /// </summary>
        /// <param name="response">The response.</param>
        private void SetEncodingFromHeader(HttpWebResponse response)
        {
            string charset = null;
            if (string.IsNullOrEmpty(response.CharacterSet))
            {
                Match m = Regex.Match(response.ContentType, @";\s*charset\s*=\s*(?<charset>.*)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    charset = m.Groups["charset"].Value.Trim('\'', '"');
                }
            }
            else
            {
                charset = response.CharacterSet;
            }

            if (!string.IsNullOrEmpty(charset))
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    this.Encoding = Encoding.GetEncoding(charset);
                }
                // ReSharper disable once UncatchableException
                catch (ArgumentException)
                {
                }
            }
        }

        /// <summary>
        /// Checks the meta character set and re encode.
        /// </summary>
        /// <param name="memStream">The memory stream.</param>
        /// <param name="html">The HTML.</param>
        /// <returns>The HTML content</returns>
        private string CheckMetaCharSetAndReEncode(Stream memStream, string html)
        {
            Match m = new Regex(@"<meta\s+.*?charset\s*=\s*?""?(?<charset>[A-Za-z0-9_-]+?)""", RegexOptions.Singleline | RegexOptions.IgnoreCase).Match(html);
            if (m.Success)
            {
                string charset = m.Groups["charset"].Value.ToLower();
                if ((charset == "unicode") || (charset == "utf-16"))
                {
                    charset = "utf-8";
                }

                try
                {
                    Encoding metaEncoding = Encoding.GetEncoding(charset);
                    if (!this.Encoding.Equals(metaEncoding))
                    {
                        memStream.Position = 0L;
                        StreamReader recodeReader = new StreamReader(memStream, metaEncoding);
                        html = recodeReader.ReadToEnd().Trim();
                        recodeReader.Close();
                    }
                }
                // ReSharper disable once UncatchableException
                catch (ArgumentException)
                {
                }
            }

            return html;
        }
    }
}
