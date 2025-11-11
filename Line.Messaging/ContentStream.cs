using System;
using System.IO;
using System.Net.Http.Headers;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE Messaging API 回傳的內容串流（如圖片、影片、音訊等媒體檔案）
    /// Stream object for content such as image, video, audio, and other media files from LINE Messaging API.
    /// </summary>
    /// <remarks>
    /// 此類別封裝了底層的 Stream 物件，並提供 HTTP Content Headers 的存取。
    /// 主要用於從 LINE 伺服器下載媒體內容時的資料傳輸。
    /// </remarks>
    public class ContentStream : Stream
    {
        /// <summary>
        /// 底層的串流物件
        /// The underlying stream object
        /// </summary>
        protected Stream _baseStream;

        /// <summary>
        /// 取得底層串流物件，如果已釋放則拋出例外
        /// Gets the base stream, throws ObjectDisposedException if already disposed
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        protected Stream BaseStream
        {
            get
            {
                if (_baseStream == null) { throw new ObjectDisposedException(nameof(BaseStream)); }
                return _baseStream;
            }
        }

        /// <summary>
        /// 取得 HTTP Content 的標頭資訊（如 Content-Type、Content-Length 等）
        /// Gets the HTTP content headers (e.g., Content-Type, Content-Length)
        /// </summary>
        public HttpContentHeaders ContentHeaders { get; }

        /// <summary>
        /// 初始化 ContentStream 的新執行個體
        /// Initializes a new instance of the ContentStream class
        /// </summary>
        /// <param name="baseStream">底層的串流物件 / The underlying stream object</param>
        /// <param name="contentHeaders">HTTP Content 標頭 / HTTP content headers</param>
        public ContentStream(Stream baseStream, HttpContentHeaders contentHeaders)
        {
            _baseStream = baseStream;
            ContentHeaders = contentHeaders;
        }

        /// <summary>
        /// 取得值，指出目前的串流是否支援讀取
        /// Gets a value indicating whether the current stream supports reading
        /// </summary>
        public override bool CanRead => _baseStream?.CanRead ?? false;

        /// <summary>
        /// 取得值，指出目前的串流是否支援搜尋
        /// Gets a value indicating whether the current stream supports seeking
        /// </summary>
        public override bool CanSeek => _baseStream?.CanSeek ?? false;

        /// <summary>
        /// 取得值，指出目前的串流是否支援寫入
        /// Gets a value indicating whether the current stream supports writing
        /// </summary>
        public override bool CanWrite => _baseStream?.CanWrite ?? false;

        /// <summary>
        /// 取得串流的位元組長度
        /// Gets the length in bytes of the stream
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Length => BaseStream.Length;

        /// <summary>
        /// 取得或設定串流中的目前位置
        /// Gets or sets the current position within the stream
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Position 
        { 
            get => BaseStream.Position; 
            set => BaseStream.Position = value; 
        }

        /// <summary>
        /// 清除此串流的所有緩衝區，並將所有緩衝資料寫入至底層裝置
        /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device
        /// </summary>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void Flush() => BaseStream.Flush();

        /// <summary>
        /// 從目前串流讀取位元組序列，並將串流中的位置往前移動讀取的位元組數
        /// Reads a sequence of bytes from the current stream and advances the position within the stream
        /// </summary>
        /// <param name="buffer">位元組陣列 / Byte array buffer</param>
        /// <param name="offset">buffer 中以零為起始的位元組位移 / Zero-based byte offset in buffer</param>
        /// <param name="count">最多要讀取的位元組數 / Maximum number of bytes to read</param>
        /// <returns>讀入緩衝區的總位元組數 / Total number of bytes read into the buffer</returns>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override int Read(byte[] buffer, int offset, int count) => BaseStream.Read(buffer, offset, count);

        /// <summary>
        /// 設定目前串流中的位置
        /// Sets the position within the current stream
        /// </summary>
        /// <param name="offset">相對於 origin 參數的位元組位移 / Byte offset relative to origin</param>
        /// <param name="origin">型別 SeekOrigin 的值，指出用來取得新位置的參考點 / Reference point for obtaining new position</param>
        /// <returns>目前串流中的新位置 / New position within the current stream</returns>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

        /// <summary>
        /// 設定目前串流的長度
        /// Sets the length of the current stream
        /// </summary>
        /// <param name="value">目前串流的所需長度（以位元組為單位）/ Desired length of current stream in bytes</param>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void SetLength(long value) => BaseStream.SetLength(value);

        /// <summary>
        /// 將位元組序列寫入至目前的串流，並將這個串流中的目前位置往前移動寫入的位元組數
        /// Writes a sequence of bytes to the current stream and advances the current position
        /// </summary>
        /// <param name="buffer">位元組陣列 / Byte array</param>
        /// <param name="offset">buffer 中以零為起始的位元組位移 / Zero-based byte offset in buffer</param>
        /// <param name="count">要寫入目前串流的位元組數 / Number of bytes to write</param>
        /// <exception cref="ObjectDisposedException">當串流已被釋放時拋出</exception>
        public override void Write(byte[] buffer, int offset, int count) => BaseStream.Write(buffer, offset, count);

        /// <summary>
        /// 釋放 ContentStream 所使用的 Unmanaged 資源，並選擇性地釋放 Managed 資源
        /// Releases unmanaged resources used by ContentStream and optionally releases managed resources
        /// </summary>
        /// <param name="disposing">true 表示釋放 Managed 和 Unmanaged 資源；false 則只釋放 Unmanaged 資源</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _baseStream?.Dispose();
                _baseStream = null;
            }
        }
    }
}
