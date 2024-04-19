using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Swiftlet.DataModels.Implementations;
using Swiftlet.Goo;
using Swiftlet.Params;
using Swiftlet.Util;

namespace Swiftlet.Components
{

    public static class MultipartUtils
    {
        public static bool TryParseMultipartBoundaryFromHeader(List<HttpHeader> headers, out string boundary)
        {
            //boundary = "--------------------------349960532445476026860457";
            //return true;

            var contentTypeRegex = new Regex("^multipart/form-data;\\s*boundary=(.*)$", RegexOptions.IgnoreCase);

            boundary = null;
            var contentType = headers.Where(h => h.Key.ToLower() == "content-type").FirstOrDefault();
            if(contentType == null)
            {
                return false;
            }

            if (!contentTypeRegex.IsMatch(contentType.Value))
            {
                boundary = null;
                return false;
            }
            else
            {
                boundary = contentTypeRegex.Match(contentType.Value).Groups[1].Value;
                return true;
            }
        }
    }


    /// <summary>
    /// Retrieves <see cref="HttpMultipartBoundary"/> instances from a request stream.
    /// </summary>
    public class HttpMultipart
    {
        private const byte LF = (byte)'\n';
        private readonly byte[] boundaryAsBytes;
        private readonly HttpMultipartBuffer readBuffer;
        private readonly Stream requestStream;
        private readonly byte[] closingBoundaryAsBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMultipart"/> class.
        /// </summary>
        /// <param name="requestStream">The request stream to parse.</param>
        /// <param name="boundary">The boundary marker to look for.</param>
        public HttpMultipart(Stream requestStream, string boundary)
        {
            this.requestStream = requestStream;
            this.boundaryAsBytes = GetBoundaryAsBytes(boundary, false);
            this.closingBoundaryAsBytes = GetBoundaryAsBytes(boundary, true);
            this.readBuffer = new HttpMultipartBuffer(this.boundaryAsBytes, this.closingBoundaryAsBytes);
        }

        /// <summary>
        /// Gets the <see cref="HttpMultipartBoundary"/> instances from the request stream.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{T}"/> instance, containing the found <see cref="HttpMultipartBoundary"/> instances.</returns>
        public IEnumerable<HttpMultipartBoundary> GetBoundaries()
        {
            return
                (from boundaryStream in this.GetBoundarySubStreams()
                 select new HttpMultipartBoundary(boundaryStream)).ToList();
        }

        private IEnumerable<HttpMultipartSubStream> GetBoundarySubStreams()
        {
            var boundarySubStreams = new List<HttpMultipartSubStream>();
            var boundaryStart = this.GetNextBoundaryPosition();

            var found = 0;
            while (MultipartIsNotCompleted(boundaryStart) && found < 1000)
            {
                var boundaryEnd = this.GetNextBoundaryPosition();
                boundarySubStreams.Add(new HttpMultipartSubStream(
                    this.requestStream,
                    boundaryStart,
                    this.GetActualEndOfBoundary(boundaryEnd)));

                boundaryStart = boundaryEnd;

                found++;
            }

            return boundarySubStreams;
        }
        private bool MultipartIsNotCompleted(long boundaryPosition)
        {
            return boundaryPosition > -1 && !this.readBuffer.IsClosingBoundary;
        }

        //we add two because or the \r\n before the boundary
        private long GetActualEndOfBoundary(long boundaryEnd)
        {
            if (this.CheckIfFoundEndOfStream())
            {
                return this.requestStream.Position - (this.readBuffer.Length + 2);
            }
            return boundaryEnd - (this.readBuffer.Length + 2);
        }

        private bool CheckIfFoundEndOfStream()
        {
            return this.requestStream.Position.Equals(this.requestStream.Length);
        }

        private static byte[] GetBoundaryAsBytes(string boundary, bool closing)
        {
            var boundaryBuilder = new StringBuilder();

            boundaryBuilder.Append("--");
            boundaryBuilder.Append(boundary);

            if (closing)
            {
                boundaryBuilder.Append("--");
            }
            else
            {
                boundaryBuilder.Append('\r');
                boundaryBuilder.Append('\n');
            }

            var bytes =
                Encoding.ASCII.GetBytes(boundaryBuilder.ToString());

            return bytes;
        }

        private long GetNextBoundaryPosition()
        {
            this.readBuffer.Reset();
            while (true)
            {
                var byteReadFromStream = this.requestStream.ReadByte();

                if (byteReadFromStream == -1)
                {
                    return -1;
                }

                this.readBuffer.Insert((byte)byteReadFromStream);

                if (this.readBuffer.IsFull && (this.readBuffer.IsBoundary || this.readBuffer.IsClosingBoundary))
                {
                    return this.requestStream.Position;
                }

                if (byteReadFromStream.Equals(LF) || this.readBuffer.IsFull)
                {
                    this.readBuffer.Reset();
                }
            }
        }
    }

    /// <summary>
    /// A buffer that is used to locate a HTTP multipart/form-data boundary in a stream.
    /// </summary>
    public class HttpMultipartBuffer
    {
        private readonly byte[] boundaryAsBytes;
        private readonly byte[] closingBoundaryAsBytes;
        private readonly byte[] buffer;
        private int position;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMultipartBuffer"/> class, with
        /// the provided <paramref name="boundaryAsBytes"/> and <paramref name="closingBoundaryAsBytes"/>.
        /// </summary>
        /// <param name="boundaryAsBytes">The boundary as a byte-array.</param>
        /// <param name="closingBoundaryAsBytes">The closing boundary as byte-array</param>
        public HttpMultipartBuffer(byte[] boundaryAsBytes, byte[] closingBoundaryAsBytes)
        {
            this.boundaryAsBytes = boundaryAsBytes;
            this.closingBoundaryAsBytes = closingBoundaryAsBytes;
            this.buffer = new byte[this.boundaryAsBytes.Length];
        }

        /// <summary>
        /// Gets a value indicating whether the buffer contains the same values as the boundary.
        /// </summary>
        /// <value><see langword="true"/> if buffer contains the same values as the boundary; otherwise, <see langword="false"/>.</value>
        public bool IsBoundary
        {
            get { return this.buffer.SequenceEqual(this.boundaryAsBytes); }
        }
        /// <summary>
        /// Gets a value indicating whether this instance is closing boundary.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if this instance is closing boundary; otherwise, <see langword="false"/>.
        /// </value>
        public bool IsClosingBoundary
        {
            get { return this.buffer.SequenceEqual(this.closingBoundaryAsBytes); }
        }
        /// <summary>
        /// Gets a value indicating whether this buffer is full.
        /// </summary>
        /// <value><see langword="true"/> if buffer is full; otherwise, <see langword="false"/>.</value>
        public bool IsFull
        {
            get { return this.position.Equals(this.buffer.Length); }
        }

        /// <summary>
        /// Gets the number of bytes that can be stored in the buffer.
        /// </summary>
        /// <value>The number of bytes that can be stored in the buffer.</value>
        public int Length
        {
            get { return this.buffer.Length; }
        }

        /// <summary>
        /// Resets the buffer so that inserts happens from the start again.
        /// </summary>
        /// <remarks>This does not clear any previously written data, just resets the buffer position to the start. Data that is inserted after Reset has been called will overwrite old data.</remarks>
        public void Reset()
        {
            this.position = 0;
        }

        /// <summary>
        /// Inserts the specified value into the buffer and advances the internal position.
        /// </summary>
        /// <param name="value">The value to insert into the buffer.</param>
        /// <remarks>This will throw an <see cref="ArgumentOutOfRangeException"/> is you attempt to call insert more times then the <see cref="Length"/> of the buffer and <see cref="Reset"/> was not invoked.</remarks>
        public void Insert(byte value)
        {
            this.buffer[this.position++] = value;
        }
    }


    /// <summary>
    /// A decorator stream that sits on top of an existing stream and appears as a unique stream.
    /// </summary>
    public class HttpMultipartSubStream : Stream
    {
        private readonly Stream stream;
        private long start;
        private readonly long end;
        private long position;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMultipartSubStream"/> class, with
        /// the provided <paramref name="stream"/>, <paramref name="start"/> and <paramref name="end"/>.
        /// </summary>
        /// <param name="stream">The stream to create the sub-stream ontop of.</param>
        /// <param name="start">The start offset on the parent stream where the sub-stream should begin.</param>
        /// <param name="end">The end offset on the parent stream where the sub-stream should end.</param>
        public HttpMultipartSubStream(Stream stream, long start, long end)
        {
            this.stream = stream;
            this.start = start;
            this.position = start;
            this.end = end;
        }

        /// <summary>
        /// When overridden in a derived class, gets a value indicating whether the current stream supports reading.
        /// </summary>
        /// <returns><see langword="true"/> if the stream supports reading; otherwise, <see langword="false"/>.</returns>
        public override bool CanRead
        {
            get { return true; }
        }

        /// <summary>
        /// When overridden in a derived class, gets a value indicating whether the current stream supports seeking.
        /// </summary>
        /// <returns><see langword="true"/> if the stream supports seeking; otherwise, <see langword="false"/>.</returns>
        public override bool CanSeek
        {
            get { return true; }
        }

        /// <summary>
        /// When overridden in a derived class, gets a value indicating whether the current stream supports writing.
        /// </summary>
        /// <returns><see langword="true"/> if the stream supports writing; otherwise, <see langword="false"/>.</returns>
        public override bool CanWrite
        {
            get { return false; }
        }

        /// <summary>
        /// When overridden in a derived class, gets the length in bytes of the stream.
        /// </summary>
        /// <returns>A long value representing the length of the stream in bytes.</returns>
        /// <exception cref="NotSupportedException">A class derived from Stream does not support seeking. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed.</exception>
        public override long Length
        {
            get { return (this.end - this.start); }
        }

        /// <summary>
        /// When overridden in a derived class, gets or sets the position within the current stream.
        /// </summary>
        /// <returns>
        /// The current position within the stream.
        /// </returns>
        /// <exception cref="T:System.IO.IOException">An I/O error occurs. </exception><exception cref="T:System.NotSupportedException">The stream does not support seeking. </exception><exception cref="T:System.ObjectDisposedException">Methods were called after the stream was closed. </exception><filterpriority>1</filterpriority>
        public override long Position
        {
            get { return this.position - this.start; }
            set { this.position = this.Seek(value, SeekOrigin.Begin); }
        }

        private long CalculateSubStreamRelativePosition(SeekOrigin origin, long offset)
        {
            var subStreamRelativePosition = 0L;

            switch (origin)
            {
                case SeekOrigin.Begin:
                    subStreamRelativePosition = this.start + offset;
                    break;

                case SeekOrigin.Current:
                    subStreamRelativePosition = this.position + offset;
                    break;

                case SeekOrigin.End:
                    subStreamRelativePosition = this.end + offset;
                    break;
            }
            return subStreamRelativePosition;
        }

        /// <summary>
        /// Sets the position of the stream as the start point.
        /// </summary>
        public void PositionStartAtCurrentLocation()
        {
            this.start = this.stream.Position;
        }

        /// <summary>
        /// When overridden in a derived class, clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        /// </summary>
        /// <remarks>In the <see cref="HttpMultipartSubStream"/> type this method is implemented as no-op.</remarks>
        public override void Flush()
        {
        }

        /// <summary>
        /// When overridden in a derived class, reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
        /// </summary>
        /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero (0) if the end of the stream has been reached. </returns>
        /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between <paramref name="offset"/> and (<paramref name="offset"/> + <paramref name="count"/> - 1) replaced by the bytes read from the current source. </param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin storing the data read from the current stream.</param>
        /// <param name="count">The maximum number of bytes to be read from the current stream. </param>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count > (this.end - this.position))
            {
                count = (int)(this.end - this.position);
            }

            if (count <= 0)
            {
                return 0;
            }

            this.stream.Position = this.position;

            var bytesReadFromStream =
                this.stream.Read(buffer, offset, count);

            this.RepositionAfterRead(bytesReadFromStream);

            return bytesReadFromStream;
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte, or returns -1 if at the end of the stream.
        /// </summary>
        /// <returns>The unsigned byte cast to an Int32, or -1 if at the end of the stream.</returns>
        public override int ReadByte()
        {
            if (this.position >= this.end)
            {
                return -1;
            }

            this.stream.Position = this.position;

            var byteReadFromStream = this.stream.ReadByte();

            this.RepositionAfterRead(1);

            return byteReadFromStream;
        }

        private void RepositionAfterRead(int bytesReadFromStream)
        {
            if (bytesReadFromStream == -1)
            {
                this.position = this.end;
            }
            else
            {
                this.position += bytesReadFromStream;
            }
        }

        /// <summary>
        /// When overridden in a derived class, sets the position within the current stream.
        /// </summary>
        /// <returns>The new position within the current stream.</returns>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
        /// <param name="origin">A value of type <see cref="SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
        public override long Seek(long offset, SeekOrigin origin)
        {
            var subStreamRelativePosition =
                this.CalculateSubStreamRelativePosition(origin, offset);

            this.ThrowExceptionIsPositionIsOutOfBounds(subStreamRelativePosition);

            this.position = this.stream.Seek(subStreamRelativePosition, SeekOrigin.Begin);

            return this.position;
        }

        /// <summary>
        /// When overridden in a derived class, sets the length of the current stream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        /// <remarks>This will always throw a <see cref="InvalidOperationException"/> for the <see cref="HttpMultipartSubStream"/> type.</remarks>
        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        /// <summary>
        /// When overridden in a derived class, writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
        /// </summary>
        /// <param name="buffer">An array of bytes. This method copies <paramref name="count"/> bytes from <paramref name="buffer"/> to the current stream. </param>
        /// <param name="offset">The zero-based byte offset in <paramref name="buffer"/> at which to begin copying bytes to the current stream. </param>
        /// <param name="count">The number of bytes to be written to the current stream. </param>
        /// <remarks>This will always throw a <see cref="InvalidOperationException"/> for the <see cref="HttpMultipartSubStream"/> type.</remarks>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        private void ThrowExceptionIsPositionIsOutOfBounds(long subStreamRelativePosition)
        {
            if (subStreamRelativePosition < 0 || subStreamRelativePosition > this.end)
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Represents the content boundary of a HTTP multipart/form-data boundary in a stream.
    /// </summary>
    public class HttpMultipartBoundary
    {
        private const byte LF = (byte)'\n';
        private const byte CR = (byte)'\r';

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMultipartBoundary"/> class.
        /// </summary>
        /// <param name="boundaryStream">The stream that contains the boundary information.</param>
        public HttpMultipartBoundary(HttpMultipartSubStream boundaryStream)
        {
            this.Value = boundaryStream;
            this.ExtractHeaders();
        }

        /// <summary>
        /// Gets the contents type of the boundary value.
        /// </summary>
        /// <value>A <see cref="string"/> containing the name of the value if it is available; otherwise <see cref="string.Empty"/>.</value>
        public string ContentType { get; private set; }

        /// <summary>
        /// Gets or the filename for the boundary value.
        /// </summary>
        /// <value>A <see cref="string"/> containing the filename value if it is available; otherwise <see cref="string.Empty"/>.</value>
        /// <remarks>This is the RFC2047 decoded value of the filename attribute of the Content-Disposition header.</remarks>
        public string Filename { get; private set; }

        /// <summary>
        /// Gets name of the boundary value.
        /// </summary>
        /// <remarks>This is the RFC2047 decoded value of the name attribute of the Content-Disposition header.</remarks>
        public string Name { get; private set; }

        /// <summary>
        /// A stream containing the value of the boundary.
        /// </summary>
        /// <remarks>This is the RFC2047 decoded value of the Content-Type header.</remarks>
        public HttpMultipartSubStream Value { get; private set; }

        private void ExtractHeaders()
        {
            while (true)
            {
                var header = ReadLineFromStream(this.Value);

                if (string.IsNullOrEmpty(header))
                {
                    break;
                }

                if (header.StartsWith("Content-Disposition", StringComparison.CurrentCultureIgnoreCase))
                {
                    this.Name = Regex.Match(header, @"name=""?(?<name>[^\""]*)", RegexOptions.IgnoreCase).Groups["name"].Value;
                    this.Filename = Regex.Match(header, @"filename\*?=""?(?<filename>[^\"";]*)", RegexOptions.IgnoreCase).Groups["filename"].Value;
                    if (this.Filename.StartsWith("utf-8''", StringComparison.CurrentCultureIgnoreCase))
                    {
                        this.Filename = Uri.UnescapeDataString(this.Filename.Substring(7));
                    }
                }

                if (header.StartsWith("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    this.ContentType = header.Split(new[] { ' ' }).Last().Trim();
                }
            }

            this.Value.PositionStartAtCurrentLocation();
        }

        private static string ReadLineFromStream(Stream stream)
        {
            var readBuffer = new List<byte>();

            while (true)
            {
                var byteReadFromStream = stream.ReadByte();

                if (byteReadFromStream == -1)
                {
                    return null;
                }

                if (byteReadFromStream.Equals(LF))
                {
                    break;
                }

                readBuffer.Add((byte)byteReadFromStream);
            }

            return Encoding.UTF8.GetString(readBuffer.ToArray()).Trim((char)CR);
        }
    }
}