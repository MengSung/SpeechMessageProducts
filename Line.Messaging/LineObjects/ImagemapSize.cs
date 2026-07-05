namespace Line.Messaging
{
    /// <summary>
    /// Image size. 
    /// </summary>
    public class ImagemapSize
    {
        /// <summary>
        /// LINE RichMenu 長版預設尺寸，對應 2500x1686。
        /// 此尺寸必須與上傳圖片和 ActionArea 座標系一致。
        /// </summary>
        public static ImagemapSize RichMenuLong { get; } = new ImagemapSize(2500, 1686);
        
        /// <summary>
        /// LINE RichMenu 短版尺寸，對應 2500x843。
        /// 適合較精簡的選單；仍需使用同一套 RichMenu 座標與圖片尺寸規則。
        /// </summary>
        public static ImagemapSize RichMenuShort { get; } = new ImagemapSize(2500, 843);

        /// <summary>
        /// Width
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Height
        /// </summary>
        public int Height { get; }

        public ImagemapSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}
    
