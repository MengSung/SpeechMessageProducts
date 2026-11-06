/*
 * 檔案整體說明
 *
 * 目的與定位：本檔定義Flex圖片的網址、縮放、對齊及外觀配置。
 *
 * 主要功能與流程：
 * 本檔宣告 ImageComponent。
 * 資料／選項欄位包含 Type、Url、Flex、Margin、Align、Gravity、Size、AspectRatio、AspectMode、BackgroundColor、Action，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/Flex/BubbleContainer.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerEntensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    /// <summary>
    /// This component draws an image.
    /// </summary>
    public class ImageComponent : IFlexComponent
    {
        public FlexComponentType Type => FlexComponentType.Image;

        /// <summary>
        /// Image URL (Required)<para>
        /// / Protocol: HTTPS
        /// / Image format: JPEG or PNG
        /// / Maximum image size: 1024×1024 pixels
        /// / Maximum data size: 1 MB</para>
        /// <para>(Required)</para>
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// The ratio of the width or height of this component within the parent box. <para>
        /// The default value for the horizontal parent box is 1, and the default value for the vertical parent box is 0.
        /// For more information, see Width and height of components.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public int? Flex { get; set; }

        /// <summary>
        /// Minimum space between this component and the previous component in the parent box.<para>
        /// You can specify one of the following values: none, xs, sm, md, lg, xl, or xxl.
        /// none does not set a space while the other values set a space whose size increases in the order of listing.
        /// The default value is the value of the spacing property of the parent box.
        ///If this component is the first component in the parent box, the margin property will be ignored.</para>
        ///<para>(Optional)</para>
        /// </summary>
        public Spacing? Margin { get; set; }

        /// <summary>
        /// Horizontal alignment style.<para>
        /// Specify one of the following values:
        /// / start: Left-aligned
        /// / end: Right-aligned
        /// / center: Center-aligned
        /// , The default value is center.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Align? Align { get; set; }

        /// <summary>
        /// Vertical alignment style.<para>
        /// Specify one of the following values:
        /// / top: Top-aligned
        /// / bottom: Bottom-aligned
        /// / center: Center-aligned
        /// , The default value is top.</para><para>
        /// If the layout property of the parent box is baseline, the gravity property will be ignored.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public Gravity? Gravity { get; set; }

        /// <summary>
        /// Maximum size of the image width.<para>
        /// You can specify one of the following values: xxs, xs, sm, md, lg, xl, xxl, 3xl, 4xl, 5xl, or full.
        /// The size increases in the order of listing.
        /// The default value is md.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public ComponentSize? Size { get; set; }

        /// <summary>
        /// Aspect ratio of the image.
        /// Specify in the {width}:{height} format. <para>
        /// Specify the value of the {width} property and the {height} property in the range from 1 to 100000. However,
        /// you cannot set the {height} property to a value that is more than three times the value of the {width} property. </para>
        /// The default value is 1:1.
        /// <para>(Optional)</para>
        /// </summary>
        public AspectRatio AspectRatio { get; set; }

        /// <summary>
        /// Style of the image.<para>
        /// Specify one of the following values:
        /// / cover: The image fills the entire drawing area.Parts of the image that do not fit in the drawing area are not displayed.
        /// / fit: The entire image is displayed in the drawing area.The background is displayed in the unused areas to the left and right of vertical images and in the areas above and below horizontal images.
        /// The default value is fit.</para>
        /// <para>(Optional)</para>
        /// </summary>
        public AspectMode? AspectMode { get; set; }

        /// <summary>
        /// Background color of the image. Use a hexadecimal color code.
        /// <para>(Optional)</para>
        /// </summary>
        public string BackgroundColor { get; set; }

        /// <summary>
        /// Action performed when this image is tapped. Specify an action object.
        /// <para>(Optional)</para>
        /// </summary>
        public ITemplateAction Action { get; set; }


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="url">
        /// Image URL (Required)<para>
        /// / Protocol: HTTPS
        /// / Image format: JPEG or PNG
        /// / Maximum image size: 1024×1024 pixels
        /// / Maximum data size: 1 MB</para>
        /// </param>
        public ImageComponent(string url)
        {
            Url = url;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public ImageComponent()
        {

        }

    }
}
