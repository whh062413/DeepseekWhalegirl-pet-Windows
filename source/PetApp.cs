using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("DeepSeek 鲸鱼娘桌宠")]
[assembly: AssemblyDescription("Independent Windows fan-made desktop pet")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WhalePet
{
    internal enum PetAction { Idle, Walk, Feed, Happy, Sleep, Think, Curious, Surprise, Pout, Shy, Stretch, TailHug, BegRice, Victory, TailSnack, Wake, Bow }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options options = Options.Parse(args);
            try
            {
                Native.EnableDpiAwareness();
                using (SpriteAtlas atlas = SpriteAtlas.Load())
                {
                    if (options.SelfTest)
                        return Diagnostics.Run(atlas, options.ReportPath) ? 0 : 2;
                    if (options.PreviewPath != null)
                    {
                        PetArt.WritePreview(atlas, options.PreviewPath);
                        return 0;
                    }
                    if (options.ExportFramesPath != null)
                    {
                        PetArt.ExportFrames(atlas, options.ExportFramesPath);
                        return 0;
                    }
                    bool created;
                    using (Mutex instance = new Mutex(true, "Local\\DeepSeekWhalePet.Standalone.v1", out created))
                    {
                        if (!created)
                        {
                            if (options.ReportPath != null)
                                Diagnostics.Write(options.ReportPath, "Another instance is already running.\r\n");
                            return 0;
                        }
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                        using (PetWindow pet = new PetWindow(atlas, options))
                        {
                            Application.Run(pet);
                            if ((options.SmokeSeconds > 0 || options.ShowcaseSeconds > 0) && !pet.DiagnosticSuccess) return 2;
                        }
                        GC.KeepAlive(instance);
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                string message = "桌宠启动失败：" + error.Message;
                string path = options.ReportPath;
                if (path == null)
                    path = Path.Combine(Settings.Folder, "last-error.txt");
                try { Diagnostics.Write(path, error.ToString()); } catch { }
                if (!options.SelfTest && options.PreviewPath == null && options.ExportFramesPath == null && options.SmokeSeconds <= 0 && options.ShowcaseSeconds <= 0)
                    MessageBox.Show(message + "\r\n诊断文件：" + path, "鲸鱼娘桌宠", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }
    }

    internal sealed class Options
    {
        internal bool SelfTest;
        internal string PreviewPath;
        internal string ExportFramesPath;
        internal string ReportPath;
        internal double SmokeSeconds;
        internal double ShowcaseSeconds;
        internal static Options Parse(string[] args)
        {
            Options result = new Options();
            foreach (string arg in args)
            {
                if (arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)) result.SelfTest = true;
                else if (arg.StartsWith("--preview=", StringComparison.OrdinalIgnoreCase)) result.PreviewPath = arg.Substring(10);
                else if (arg.StartsWith("--export-frames=", StringComparison.OrdinalIgnoreCase)) result.ExportFramesPath = arg.Substring(16);
                else if (arg.StartsWith("--report=", StringComparison.OrdinalIgnoreCase)) result.ReportPath = arg.Substring(9);
                else if (arg.StartsWith("--smoke-test=", StringComparison.OrdinalIgnoreCase))
                {
                    double seconds;
                    if (Double.TryParse(arg.Substring(13), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        result.SmokeSeconds = Math.Max(3, Math.Min(60, seconds));
                }
                else if (arg.StartsWith("--showcase-test=", StringComparison.OrdinalIgnoreCase))
                {
                    double seconds;
                    if (Double.TryParse(arg.Substring(16), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        result.ShowcaseSeconds = Math.Max(46, Math.Min(120, seconds));
                }
            }
            return result;
        }
    }

    internal sealed class SpriteAtlas : IDisposable
    {
        internal const int ActionCount = 17;
        internal const int FrameCount = ActionCount * 4;
        internal Bitmap Sheet;
        internal Rectangle[] Frames;
        internal Rectangle CommonCrop;
        internal int[] OpaquePixels;
        internal string Source;

        internal static SpriteAtlas Load()
        {
            SpriteAtlas atlas = new SpriteAtlas();
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PetSprites");
            if (source != null) atlas.Source = "embedded:PetSprites";
            else
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sprites.png");
                source = File.OpenRead(path);
                atlas.Source = path;
            }
            using (source)
            using (Image original = Image.FromStream(source))
            {
                atlas.Sheet = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(atlas.Sheet))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(original, 0, 0);
                }
            }
            atlas.Frames = new Rectangle[FrameCount];
            string framesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frames.tsv");
            if (File.Exists(framesPath))
            {
                bool[] supplied = new bool[FrameCount];
                foreach (string raw in File.ReadAllLines(framesPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("index", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] values = line.Split(new char[] { '\t', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length != 5) throw new InvalidDataException("frames.tsv must contain index, x, y, width, height.");
                    int index = Int32.Parse(values[0], CultureInfo.InvariantCulture);
                    if (index < 0 || index >= FrameCount || supplied[index]) throw new InvalidDataException("frames.tsv contains a repeated or invalid index.");
                    atlas.Frames[index] = new Rectangle(Int32.Parse(values[1]), Int32.Parse(values[2]), Int32.Parse(values[3]), Int32.Parse(values[4]));
                    supplied[index] = true;
                }
                foreach (bool present in supplied) if (!present) throw new InvalidDataException("frames.tsv must define all 68 frames.");
            }
            else
            {
                if (atlas.Sheet.Width % 4 != 0 || atlas.Sheet.Height % ActionCount != 0)
                    throw new InvalidDataException("Sprite dimensions must divide into 4 columns and 17 rows.");
                int cellWidth = atlas.Sheet.Width / 4;
                int cellHeight = atlas.Sheet.Height / ActionCount;
                for (int i = 0; i < FrameCount; i++) atlas.Frames[i] = new Rectangle((i % 4) * cellWidth, (i / 4) * cellHeight, cellWidth, cellHeight);
            }
            atlas.MeasureAlpha();
            return atlas;
        }

        private void MeasureAlpha()
        {
            BitmapData data = Sheet.LockBits(new Rectangle(0, 0, Sheet.Width, Sheet.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[Math.Abs(data.Stride) * Sheet.Height];
            try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
            finally { Sheet.UnlockBits(data); }
            OpaquePixels = new int[FrameCount];
            int minX = Int32.MaxValue, minY = Int32.MaxValue, maxX = -1, maxY = -1;
            int commonWidth = Frames[0].Width, commonHeight = Frames[0].Height;
            for (int i = 0; i < Frames.Length; i++)
            {
                Rectangle frame = Frames[i];
                if (frame.Width != commonWidth || frame.Height != commonHeight)
                    throw new InvalidDataException("All frame rectangles must use the same cell size.");
                if (frame.X < 0 || frame.Y < 0 || frame.Right > Sheet.Width || frame.Bottom > Sheet.Height || frame.Width <= 0 || frame.Height <= 0)
                    throw new InvalidDataException("A sprite frame lies outside the sheet.");
                for (int y = 0; y < frame.Height; y++)
                {
                    int row = (frame.Y + y) * Math.Abs(data.Stride);
                    for (int x = 0; x < frame.Width; x++)
                    {
                        if (pixels[row + (frame.X + x) * 4 + 3] <= 12) continue;
                        OpaquePixels[i]++;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
                if (OpaquePixels[i] < 30) throw new InvalidDataException("Sprite frame " + i + " is empty.");
            }
            minX = Math.Max(0, minX - 3); minY = Math.Max(0, minY - 3);
            maxX = Math.Min(commonWidth - 1, maxX + 3); maxY = Math.Min(commonHeight - 1, maxY + 3);
            CommonCrop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        internal Rectangle SourceRect(int frame)
        {
            Rectangle cell = Frames[Math.Max(0, Math.Min(FrameCount - 1, frame))];
            return new Rectangle(cell.X + CommonCrop.X, cell.Y + CommonCrop.Y, CommonCrop.Width, CommonCrop.Height);
        }
        public void Dispose() { if (Sheet != null) Sheet.Dispose(); }
    }

    internal sealed class PetLayout
    {
        internal Size Size;
        internal Rectangle Body;
        internal Rectangle Rice;
        internal Rectangle RiceHit { get { return new Rectangle(Rice.Left - 3, Rice.Top, Rice.Width + 6, Rice.Height + 16); } }
        internal Rectangle Bubble;
        internal static PetLayout Create(SpriteAtlas atlas, int sizeIndex)
        {
            int[] heights = new int[] { 170, 215, 280 };
            int height = heights[Math.Max(0, Math.Min(2, sizeIndex))];
            int width = Math.Max(60, (int)Math.Round(height * (double)atlas.CommonCrop.Width / atlas.CommonCrop.Height));
            int canvasWidth = Math.Max(294, width + 60);
            PetLayout result = new PetLayout();
            result.Size = new Size(canvasWidth, height + 116);
            result.Body = new Rectangle((canvasWidth - width) / 2, 70, width, height);
            result.Rice = new Rectangle(Math.Min(canvasWidth - 60, result.Body.Right + 4), result.Body.Bottom - 22, 52, 41);
            result.Bubble = new Rectangle((canvasWidth - 274) / 2, 4, 274, 53);
            return result;
        }
    }

    internal static class PetLogic
    {
        internal const double FeedSeconds = 5.8;
        internal static int Frame(PetAction action, double elapsed)
        {
            elapsed = Math.Max(0, elapsed);
            switch (action)
            {
                case PetAction.Walk: return 4 + ((int)(elapsed / 0.15) % 4);
                case PetAction.Feed:
                    if (elapsed < 0.45) return 8;
                    if (elapsed < 4.6) return 9 + ((int)((elapsed - 0.45) / 0.3) % 2);
                    return 11;
                case PetAction.Happy: return 12 + ((int)(elapsed / 0.22) % 4);
                case PetAction.Sleep: return 16 + ((int)(elapsed / 0.7) % 4);
                default: return (int)action * 4 + ((int)(elapsed / (action == PetAction.Idle ? 0.4 : 0.3)) % 4);
            }
        }
        internal static string ActionName(PetAction action)
        {
            string[] names = { "待机眨眼", "左右散步", "吃白饭", "开心挥手", "困困小睡", "认真思考", "好奇探头", "吓一小跳", "鼓脸委屈", "害羞捂脸", "伸个懒腰", "抱抱尾巴", "端盆讨饭", "吃饱万岁", "尾巴藏白饭", "揉眼醒来", "乖巧鞠躬" };
            return names[(int)action];
        }
        internal static string ActionDialogue(PetAction action)
        {
            switch (action)
            {
                case PetAction.Idle: return "我在这里陪着你呀～";
                case PetAction.Walk: return "散散步，等会儿吃白饭～";
                case PetAction.Feed: return "白饭！我最喜欢啦～\n啊呜～白饭真香！";
                case PetAction.Happy: return "听见你叫我啦～\n今天也要开开心心！";
                case PetAction.Sleep: return "小睡一会儿……\n开饭的时候记得叫我哦。";
                case PetAction.Think: return "让我想一想……\n想完可以吃白饭吗？";
                case PetAction.Curious: return "嗯？在看什么呀～\n也让我瞧瞧！";
                case PetAction.Surprise: return "呜哇！吓了一小跳～";
                case PetAction.Pout: return "哼唧……\n一碗白饭就能哄好我哦。";
                case PetAction.Shy: return "这样看着我……\n会有一点点不好意思啦。";
                case PetAction.Stretch: return "呼——伸个懒腰～\n你也休息一下吧！";
                case PetAction.TailHug: return "抱住软软的大尾巴～\n也分你一点点温暖。";
                case PetAction.BegRice: return "端着空铁盆……\n再来一点白饭，好不好嘛？";
                case PetAction.Victory: return "吃光光！白饭最棒啦～\n今天又多了一点幸福！";
                case PetAction.TailSnack: return "尾巴里？没有藏白饭呀……\n才、才没有偷吃呢～";
                case PetAction.Wake: return "醒啦～\n是不是到吃白饭的时间了？";
                default: return "谢谢你今天陪着我～\n请多多关照！";
            }
        }
        internal static double Duration(PetAction action)
        {
            if (action == PetAction.Feed) return FeedSeconds;
            if (action == PetAction.Sleep) return Double.PositiveInfinity;
            if (action == PetAction.Stretch || action == PetAction.BegRice || action == PetAction.Victory) return 3.2;
            return 2.6;
        }
        internal static double ShowcaseDuration(PetAction action)
        {
            if (action == PetAction.Feed) return FeedSeconds;
            return action == PetAction.Sleep ? 2.8 : 2.4;
        }
        internal static Point Clamp(Point location, Size size, Rectangle area)
        {
            int maxX = Math.Max(area.Left, area.Right - size.Width);
            int maxY = Math.Max(area.Top, area.Bottom - size.Height);
            return new Point(Math.Max(area.Left, Math.Min(maxX, location.X)), Math.Max(area.Top, Math.Min(maxY, location.Y)));
        }
        internal static bool IsDrag(Point start, Point end)
        {
            return Math.Abs(start.X - end.X) >= 6 || Math.Abs(start.Y - end.Y) >= 6;
        }
    }

    internal static class PetArt
    {
        internal static Bitmap Render(SpriteAtlas atlas, PetLayout layout, int frame, bool mirror, string bubble, bool sleeping, int feedCount)
        {
            Bitmap canvas = new Bitmap(layout.Size.Width, layout.Size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (!String.IsNullOrEmpty(bubble)) DrawBubble(graphics, layout.Bubble, bubble);
                DrawBody(graphics, atlas, layout.Body, frame, mirror);
                DrawRiceButton(graphics, layout.Rice, sleeping);
                if (feedCount > 0)
                {
                    using (Font font = new Font("Microsoft YaHei UI", 8, FontStyle.Regular, GraphicsUnit.Point))
                    using (Brush brush = new SolidBrush(Color.FromArgb(230, 68, 91, 123)))
                        graphics.DrawString("喂饭 " + feedCount + " 次", font, brush, layout.Rice.Left - 1, layout.Rice.Bottom + 15);
                }
            }
            return canvas;
        }

        private static void DrawBody(Graphics graphics, SpriteAtlas atlas, Rectangle destination, int frame, bool mirror)
        {
            GraphicsState state = graphics.Save();
            if (mirror)
            {
                graphics.TranslateTransform(destination.Left * 2 + destination.Width, 0);
                graphics.ScaleTransform(-1, 1);
            }
            graphics.DrawImage(atlas.Sheet, destination, atlas.SourceRect(frame), GraphicsUnit.Pixel);
            graphics.Restore(state);
        }

        internal static void ExportFrames(SpriteAtlas atlas, string path)
        {
            string folder = Path.GetFullPath(path); Directory.CreateDirectory(folder);
            PetLayout layout = PetLayout.Create(atlas, 1);
            for (int frame = 0; frame < SpriteAtlas.FrameCount; frame++)
            {
                using (Bitmap image = new Bitmap(layout.Body.Width, layout.Body.Height, PixelFormat.Format32bppPArgb))
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    DrawBody(graphics, atlas, new Rectangle(Point.Empty, layout.Body.Size), frame, false);
                    image.Save(Path.Combine(folder, frame.ToString("00") + ".png"), ImageFormat.Png);
                }
            }
        }

        private static void DrawBubble(Graphics graphics, Rectangle rectangle, string text)
        {
            using (GraphicsPath path = Rounded(rectangle, 13))
            using (Brush background = new SolidBrush(Color.FromArgb(247, 249, 253, 255)))
            using (Pen edge = new Pen(Color.FromArgb(230, 115, 170, 214), 1.4f))
            {
                graphics.FillPath(background, path);
                graphics.DrawPath(edge, path);
                Point[] tail = new Point[] { new Point(rectangle.Left + 71, rectangle.Bottom - 1), new Point(rectangle.Left + 83, rectangle.Bottom + 9), new Point(rectangle.Left + 92, rectangle.Bottom - 1) };
                graphics.FillPolygon(background, tail);
                graphics.DrawLine(edge, tail[0], tail[1]);
                graphics.DrawLine(edge, tail[1], tail[2]);
            }
            using (Font font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush ink = new SolidBrush(Color.FromArgb(255, 39, 78, 115)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawString(text, font, ink, Rectangle.Inflate(rectangle, -9, -4), format);
            }
        }

        private static void DrawRiceButton(Graphics graphics, Rectangle area, bool sleeping)
        {
            using (Brush shadow = new SolidBrush(Color.FromArgb(35, 31, 83, 128)))
                graphics.FillEllipse(shadow, area.X + 1, area.Y + 3, area.Width, area.Height);
            using (Brush background = new SolidBrush(Color.FromArgb(250, 249, 253, 255)))
            using (Pen edge = new Pen(Color.FromArgb(255, 137, 155, 171), 1.5f))
            {
                graphics.FillEllipse(background, area);
                graphics.DrawEllipse(edge, area);
            }
            int x = area.X, y = area.Y;
            using (LinearGradientBrush rim = new LinearGradientBrush(new Rectangle(x + 4, y + 19, 44, 9), Color.FromArgb(255, 244, 247, 249), Color.FromArgb(255, 151, 162, 173), 90f))
            using (Pen metalEdge = new Pen(Color.FromArgb(255, 112, 126, 140), 1.1f))
            {
                graphics.FillEllipse(rim, x + 4, y + 19, 44, 9);
                graphics.DrawEllipse(metalEdge, x + 4, y + 19, 44, 9);
            }
            using (Brush rice = new SolidBrush(Color.White))
            using (Pen outline = new Pen(Color.FromArgb(255, 172, 182, 191), 0.9f))
            {
                graphics.FillEllipse(rice, x + 9, y + 11, 34, 14);
                graphics.DrawArc(outline, x + 9, y + 11, 34, 14, 180, 180);
                graphics.DrawLine(outline, x + 17, y + 15, x + 16, y + 17);
                graphics.DrawLine(outline, x + 25, y + 13, x + 24, y + 16);
                graphics.DrawLine(outline, x + 33, y + 15, x + 32, y + 17);
            }
            using (GraphicsPath basin = new GraphicsPath())
            using (LinearGradientBrush silver = new LinearGradientBrush(new Rectangle(x + 5, y + 22, 42, 12), Color.FromArgb(255, 238, 243, 246), Color.FromArgb(255, 139, 153, 166), 90f))
            using (Pen line = new Pen(Color.FromArgb(255, 105, 121, 136), 1.1f))
            using (Pen shine = new Pen(Color.FromArgb(255, 253, 254, 255), 1.4f))
            {
                basin.AddBezier(x + 5, y + 23, x + 11, y + 35, x + 41, y + 35, x + 47, y + 23);
                basin.CloseFigure();
                graphics.FillPath(silver, basin);
                graphics.DrawPath(line, basin);
                graphics.DrawArc(line, x + 4, y + 19, 44, 9, 0, 180);
                graphics.DrawArc(shine, x + 6, y + 19, 40, 7, 9, 162);
                graphics.DrawLine(shine, x + 15, y + 29, x + 34, y + 29);
            }
            if (sleeping)
            {
                using (Font font = new Font("Segoe UI", 7, FontStyle.Bold))
                using (Brush brush = new SolidBrush(Color.FromArgb(255, 68, 101, 154)))
                    graphics.DrawString("z", font, brush, x + 39, y + 1);
            }
            using (Font font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            using (Brush ink = new SolidBrush(Color.FromArgb(255, 50, 91, 145)))
            using (StringFormat centered = new StringFormat())
            {
                centered.Alignment = StringAlignment.Center;
                graphics.DrawString("喂白饭", font, ink, new Rectangle(area.X, area.Bottom + 1, area.Width, 14), centered);
            }
        }

        private static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void WritePreview(SpriteAtlas atlas, string path)
        {
            string absolute = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            PetLayout layout = PetLayout.Create(atlas, 1);
            using (Bitmap image = new Bitmap(1200, 760, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.FromArgb(234, 239, 247));
                using (Font title = new Font("Microsoft YaHei UI", 23, FontStyle.Bold))
                using (Font subtitle = new Font("Microsoft YaHei UI", 12))
                using (Brush ink = new SolidBrush(Color.FromArgb(45, 76, 118)))
                {
                    graphics.DrawString("鲸鱼娘 · 白饭时间", title, ink, 55, 54);
                    graphics.DrawString("静态外观预览 · 双击角色或点击铁盆，触发白饭互动", subtitle, ink, 58, 103);
                }
                using (Bitmap pet = Render(atlas, layout, 10, false, "白饭！香香的白饭！\n啊呜——再来一口～", false, 1))
                    graphics.DrawImageUnscaled(pet, 680, 306);
                using (Brush bar = new SolidBrush(Color.FromArgb(160, 229, 238, 250)))
                    graphics.FillRectangle(bar, 0, 713, 1200, 47);
                image.Save(absolute, ImageFormat.Png);
            }
        }
    }

    internal sealed class Settings
    {
        internal int X, Y, SizeIndex = 1;
        internal bool PositionSaved, AutoWalk = true, AutoCute = true;
        internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekWhalePet"); } }
        internal static Settings Load()
        {
            Settings result = new Settings();
            try
            {
                string path = Path.Combine(Folder, "settings.txt");
                if (!File.Exists(path)) return result;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] pair = line.Split('=');
                    if (pair.Length != 2) continue;
                    int value;
                    if (pair[0] == "x" && Int32.TryParse(pair[1], out value)) { result.X = value; result.PositionSaved = true; }
                    if (pair[0] == "y" && Int32.TryParse(pair[1], out value)) result.Y = value;
                    if (pair[0] == "size" && Int32.TryParse(pair[1], out value)) result.SizeIndex = Math.Max(0, Math.Min(2, value));
                    if (pair[0] == "walk") result.AutoWalk = pair[1] == "1";
                    if (pair[0] == "cute") result.AutoCute = pair[1] == "1";
                }
            }
            catch { }
            return result;
        }
        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, "settings.txt"), "x=" + X + "\r\ny=" + Y + "\r\nsize=" + SizeIndex + "\r\nwalk=" + (AutoWalk ? "1" : "0") + "\r\ncute=" + (AutoCute ? "1" : "0") + "\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class PetWindow : Form
    {
        internal bool DiagnosticSuccess = true;
        private readonly SpriteAtlas atlas;
        private readonly Options options;
        private readonly Settings settings;
        private readonly Random random = new Random();
        private readonly System.Windows.Forms.Timer timer;
        private readonly System.Windows.Forms.Timer clickTimer;
        private readonly ContextMenuStrip menu;
        private readonly NotifyIcon tray;
        private readonly Icon trayIcon;
        private readonly ToolStripMenuItem walkItem;
        private readonly ToolStripMenuItem cuteItem;
        private readonly ToolStripMenuItem sleepItem;
        private readonly ToolStripMenuItem visibilityItem;
        private readonly ToolStripMenuItem[] sizes = new ToolStripMenuItem[3];
        private readonly DateTime started = DateTime.UtcNow;
        private DateTime actionStarted, lastTick, bubbleUntil;
        private PetLayout layout;
        private PetAction action = PetAction.Idle;
        private string bubble = "你好呀～\n双击我，或者点铁盆喂白饭！";
        private byte[] hitAlpha;
        private int lastFrame = -1, feedCount, direction = 1, renders, showcaseIndex = -1;
        private int mouseDownEvents, completedDrags, riceButtonEvents;
        private bool dirty = true, dragging, mousePressed, pressedBowl, suppressClickUp, closing, smokeFed;
        private Point mouseOrigin, windowOrigin;
        private double nextWalk, nextCute, walkUntil, motionX;
        private readonly HashSet<int> renderedFrames = new HashSet<int>();
        private readonly HashSet<PetAction> renderedActions = new HashSet<PetAction>();
        private PetAction[] sceneSteps;
        private int sceneIndex;
        private string sceneName;

        internal PetWindow(SpriteAtlas atlas, Options options)
        {
            this.atlas = atlas; this.options = options;
            settings = options.SmokeSeconds > 0 || options.ShowcaseSeconds > 0 ? new Settings() : Settings.Load();
            layout = PetLayout.Create(atlas, settings.SizeIndex);
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Size = layout.Size; Text = "鲸鱼娘 · 白饭时间";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.StandardDoubleClick, true);
            actionStarted = lastTick = started;
            bubbleUntil = started.AddSeconds(8);
            nextWalk = 10 + random.NextDouble() * 8;
            nextCute = 15 + random.NextDouble() * 15;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = settings.PositionSaved ? PetLogic.Clamp(new Point(settings.X, settings.Y), Size, Screen.FromPoint(new Point(settings.X, settings.Y)).WorkingArea)
                : PetLogic.Clamp(new Point(area.Right - Width - 26, area.Bottom - Height - 18), Size, area);
            motionX = Left;

            menu = new ContextMenuStrip();
            menu.Font = new Font("Microsoft YaHei UI", 9);
            menu.Items.Add("喂白饭（铁盆）", null, delegate { Feed(); });
            menu.Items.Add("打个招呼", null, delegate { Happy(); });
            sleepItem = new ToolStripMenuItem("睡一小会儿", null, delegate { ToggleSleep(); }); menu.Items.Add(sleepItem);
            walkItem = new ToolStripMenuItem("自动散步", null, delegate { settings.AutoWalk = !settings.AutoWalk; walkItem.Checked = settings.AutoWalk; if (!settings.AutoWalk && action == PetAction.Walk) SetAction(PetAction.Idle); nextWalk = Uptime + 7; SaveSettings(); });
            walkItem.Checked = settings.AutoWalk; menu.Items.Add(walkItem);
            cuteItem = new ToolStripMenuItem("偶尔卖个萌", null, delegate { settings.AutoCute = !settings.AutoCute; cuteItem.Checked = settings.AutoCute; nextCute = Uptime + 15; SaveSettings(); });
            cuteItem.Checked = settings.AutoCute; menu.Items.Add(cuteItem);
            ToolStripMenuItem gallery = new ToolStripMenuItem("动作相册（17 个）");
            for (int i = 0; i < SpriteAtlas.ActionCount; i++)
            {
                PetAction choice = (PetAction)i;
                gallery.DropDownItems.Add(PetLogic.ActionName(choice), null, delegate { PlayAction(choice); });
            }
            menu.Items.Add(gallery);
            menu.Items.Add("随机来个可爱动作", null, delegate { RandomCute(); });
            ToolStripMenuItem scenes = new ToolStripMenuItem("小剧场（6 段）");
            scenes.DropDownItems.Add("报到啦", null, delegate { StartScene("报到啦", new PetAction[] { PetAction.Curious, PetAction.Shy, PetAction.Happy, PetAction.Bow }); });
            scenes.DropDownItems.Add("白饭时间", null, delegate { StartScene("白饭时间", new PetAction[] { PetAction.BegRice, PetAction.Feed, PetAction.Victory, PetAction.Bow }); });
            scenes.DropDownItems.Add("还想再来一碗", null, delegate { StartScene("还想再来一碗", new PetAction[] { PetAction.Victory, PetAction.BegRice, PetAction.Pout, PetAction.Shy }); });
            scenes.DropDownItems.Add("偷藏白饭被发现", null, delegate { StartScene("偷藏白饭被发现", new PetAction[] { PetAction.TailSnack, PetAction.Surprise, PetAction.Shy, PetAction.Bow }); });
            scenes.DropDownItems.Add("起床开饭", null, delegate { StartScene("起床开饭", new PetAction[] { PetAction.Sleep, PetAction.Wake, PetAction.Feed, PetAction.Victory }); });
            scenes.DropDownItems.Add("今天也辛苦了", null, delegate { StartScene("今天也辛苦了", new PetAction[] { PetAction.Think, PetAction.Stretch, PetAction.TailHug, PetAction.Sleep, PetAction.Wake }); });
            menu.Items.Add(scenes);
            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("角色大小");
            string[] labels = new string[] { "小", "中", "大" };
            for (int i = 0; i < 3; i++)
            {
                int selected = i;
                sizes[i] = new ToolStripMenuItem(labels[i], null, delegate { ChangeSize(selected); });
                sizes[i].Checked = i == settings.SizeIndex; sizeMenu.DropDownItems.Add(sizes[i]);
            }
            menu.Items.Add(sizeMenu);
            menu.Items.Add("回到屏幕右下角", null, delegate { ResetPosition(); });
            visibilityItem = new ToolStripMenuItem("隐藏桌宠", null, delegate { ToggleVisibility(); }); menu.Items.Add(visibilityItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });

            trayIcon = MakeTrayIcon();
            Icon = trayIcon;
            tray = new NotifyIcon(); tray.Icon = trayIcon; tray.Text = "鲸鱼娘 · 白饭时间 · 双击显示/隐藏"; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += delegate { ToggleVisibility(); };
            timer = new System.Windows.Forms.Timer(); timer.Interval = 50; timer.Tick += Tick;
            clickTimer = new System.Windows.Forms.Timer(); clickTimer.Interval = SystemInformation.DoubleClickTime;
            clickTimer.Tick += delegate { clickTimer.Stop(); Happy(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            MouseDown += OnPetMouseDown; MouseMove += OnPetMouseMove; MouseUp += OnPetMouseUp;
            MouseCaptureChanged += delegate
            {
                if (!mousePressed) return;
                bool wasDragging = dragging;
                mousePressed = dragging = pressedBowl = suppressClickUp = false;
                clickTimer.Stop(); nextWalk = Uptime + 10;
                if (wasDragging) { if (action != PetAction.Feed) SetAction(PetAction.Happy); SaveSettings(); }
            };
            MouseDoubleClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && !pressedBowl) { clickTimer.Stop(); suppressClickUp = true; Feed(); } };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); Render(); timer.Start();
        }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        private double Uptime { get { return (DateTime.UtcNow - started).TotalSeconds; } }

        private void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            double delta = Math.Min(0.15, Math.Max(0, (now - lastTick).TotalSeconds)); lastTick = now;
            double up = Uptime;
            bool showcase = options.ShowcaseSeconds > 0;
            if (showcase) UpdateShowcase(up);
            else UpdateScene();
            double elapsed = (now - actionStarted).TotalSeconds;
            if (bubble != null && now > bubbleUntil) { bubble = null; dirty = true; }
            if (!showcase && sceneSteps == null && !dragging && action == PetAction.Feed && elapsed >= PetLogic.FeedSeconds)
            {
                SetAction(PetAction.Victory); Bubble(PetLogic.ActionDialogue(PetAction.Victory), 3.2);
            }
            else if (!showcase && sceneSteps == null && !dragging && action != PetAction.Idle && action != PetAction.Walk && action != PetAction.Feed && action != PetAction.Sleep && elapsed >= PetLogic.Duration(action)) SetAction(PetAction.Idle);
            if (!dragging && !mousePressed && Visible && !menu.Visible)
            {
                if (action == PetAction.Walk)
                {
                    Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                    motionX += direction * 24 * delta;
                    Point desired = new Point((int)Math.Round(motionX), Top);
                    Point clamped = PetLogic.Clamp(desired, Size, area);
                    if (desired.X != clamped.X) { direction = -direction; motionX = clamped.X; if (!showcase) SetAction(PetAction.Idle); }
                    if (clamped != Location) MoveWithoutActivation(clamped);
                    if (!showcase && up >= walkUntil) SetAction(PetAction.Idle);
                }
                else if (!showcase && sceneSteps == null && action == PetAction.Idle && settings.AutoCute && up >= nextCute)
                {
                    RandomCute(); nextCute = up + 15 + random.NextDouble() * 15;
                }
                else if (!showcase && sceneSteps == null && action == PetAction.Idle && settings.AutoWalk && up >= nextWalk)
                {
                    direction = random.Next(2) == 0 ? -1 : 1;
                    walkUntil = up + 1.5 + random.NextDouble() * 2;
                    motionX = Left; SetAction(PetAction.Walk);
                }
            }
            if (!showcase && options.SmokeSeconds > 0)
            {
                if (!smokeFed && up >= 0.5) { smokeFed = true; Feed(); }
                if (up >= options.SmokeSeconds) { Close(); return; }
            }
            if (showcase && up >= options.ShowcaseSeconds) { Close(); return; }
            int frame = PetLogic.Frame(action, (now - actionStarted).TotalSeconds);
            if (dirty || frame != lastFrame) Render();
        }

        private void SetAction(PetAction next)
        {
            action = next; actionStarted = DateTime.UtcNow; dirty = true;
            sleepItem.Text = next == PetAction.Sleep ? "醒醒，开饭啦" : "睡一小会儿";
            if (next == PetAction.Idle) nextWalk = Uptime + 11 + random.NextDouble() * 16;
        }
        private void UpdateShowcase(double up)
        {
            double remaining = Math.Max(0, up - 0.15);
            int index = SpriteAtlas.ActionCount - 1;
            for (int i = 0; i < SpriteAtlas.ActionCount; i++)
            {
                double duration = PetLogic.ShowcaseDuration((PetAction)i);
                if (remaining < duration) { index = i; break; }
                remaining -= duration;
            }
            if (index == showcaseIndex) return;
            showcaseIndex = index;
            PetAction choice = (PetAction)index;
            if (choice == PetAction.Feed) { feedCount++; smokeFed = true; }
            if (choice == PetAction.Walk) { direction = -1; motionX = Left; }
            SetAction(choice); Bubble("动作预览：" + PetLogic.ActionName(choice) + "\n" + (index + 1) + " / " + SpriteAtlas.ActionCount, PetLogic.ShowcaseDuration(choice));
        }
        private void PlayAction(PetAction choice)
        {
            CancelScene();
            clickTimer.Stop();
            if (!Visible && IsHandleCreated) ToggleVisibility();
            if (choice == PetAction.Feed) { Feed(); return; }
            if (action == PetAction.Feed) { Bubble("先让我把白饭吃完嘛～", 2); return; }
            if (choice == PetAction.Walk)
            {
                direction = random.Next(2) == 0 ? -1 : 1;
                walkUntil = Uptime + 3; motionX = Left;
            }
            SetAction(choice); Bubble(PetLogic.ActionDialogue(choice), choice == PetAction.Sleep ? 4 : PetLogic.Duration(choice));
            nextCute = Uptime + 15 + random.NextDouble() * 15;
        }
        private void RandomCute()
        {
            PetAction[] choices = { PetAction.Happy, PetAction.Think, PetAction.Curious, PetAction.Surprise, PetAction.Pout, PetAction.Shy, PetAction.Stretch, PetAction.TailHug, PetAction.BegRice, PetAction.Victory, PetAction.TailSnack, PetAction.Bow };
            PlayAction(choices[random.Next(choices.Length)]);
        }
        private void CancelScene()
        {
            sceneSteps = null; sceneName = null;
        }
        private void StartScene(string name, PetAction[] steps)
        {
            CancelScene(); clickTimer.Stop();
            if (!Visible && IsHandleCreated) ToggleVisibility();
            if (action == PetAction.Feed) { Bubble("先让我把这盆白饭吃完嘛～", 2); return; }
            sceneName = name; sceneSteps = steps; sceneIndex = 0; EnterSceneStep();
        }
        private void EnterSceneStep()
        {
            PetAction choice = sceneSteps[sceneIndex];
            if (choice == PetAction.Feed) feedCount++;
            SetAction(choice);
            string line = PetLogic.ActionDialogue(choice).Split('\n')[0];
            Bubble(sceneName + "（" + (sceneIndex + 1) + "/" + sceneSteps.Length + "）\n" + line, SceneDuration(choice));
        }
        private static double SceneDuration(PetAction choice)
        {
            return choice == PetAction.Sleep ? 3.0 : PetLogic.Duration(choice);
        }
        private void UpdateScene()
        {
            if (sceneSteps == null || dragging || mousePressed) return;
            if ((DateTime.UtcNow - actionStarted).TotalSeconds < SceneDuration(action)) return;
            sceneIndex++;
            if (sceneIndex < sceneSteps.Length) EnterSceneStep();
            else { CancelScene(); SetAction(PetAction.Idle); nextCute = Uptime + 15 + random.NextDouble() * 15; }
        }
        private void Bubble(string text, double seconds) { bubble = text; bubbleUntil = DateTime.UtcNow.AddSeconds(seconds); dirty = true; }
        private void Feed()
        {
            CancelScene();
            clickTimer.Stop();
            if (!Visible && IsHandleCreated) ToggleVisibility();
            if (action == PetAction.Feed) { Bubble("这盆还没吃完呢～\n啊呜啊呜……", 2); return; }
            feedCount++; SetAction(PetAction.Feed); Bubble(PetLogic.ActionDialogue(PetAction.Feed), PetLogic.FeedSeconds);
        }
        private void Happy()
        {
            CancelScene();
            if (!Visible && IsHandleCreated) ToggleVisibility();
            if (action == PetAction.Feed) { Bubble("先让我把白饭吃完嘛～", 2); return; }
            PetAction[] choices = { PetAction.Happy, PetAction.Curious, PetAction.Shy, PetAction.TailHug, PetAction.Bow };
            PlayAction(choices[random.Next(choices.Length)]);
        }
        private void ToggleSleep()
        {
            if (action == PetAction.Sleep) PlayAction(PetAction.Wake);
            else PlayAction(PetAction.Sleep);
        }
        private void ToggleVisibility()
        {
            if (Visible) { Hide(); visibilityItem.Text = "显示桌宠"; clickTimer.Stop(); }
            else { ClampToScreen(); Show(); Native.SetWindowPos(Handle, Native.HWND_TOPMOST, Left, Top, Width, Height, Native.SWP_NOACTIVATE | Native.SWP_NOSIZE); visibilityItem.Text = "隐藏桌宠"; dirty = true; Render(); }
        }
        private void ChangeSize(int index)
        {
            int baseY = layout.Body.Bottom + Top;
            int centerX = Left + Width / 2;
            settings.SizeIndex = index; layout = PetLayout.Create(atlas, index); Size = layout.Size;
            Location = new Point(centerX - Width / 2, baseY - layout.Body.Bottom); ClampToScreen();
            for (int i = 0; i < 3; i++) sizes[i].Checked = i == index;
            dirty = true; Render(); SaveSettings();
        }
        private void ResetPosition()
        {
            CancelScene();
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MoveWithoutActivation(PetLogic.Clamp(new Point(area.Right - Width - 26, area.Bottom - Height - 18), Size, area));
            SetAction(PetAction.Idle); SaveSettings();
        }
        private void ClampToScreen()
        {
            MoveWithoutActivation(PetLogic.Clamp(Location, Size, Screen.FromRectangle(Bounds).WorkingArea));
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (closing || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!closing) { ClampToScreen(); dirty = true; Render(); } }); } catch (InvalidOperationException) { }
        }
        private void MoveWithoutActivation(Point point)
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, point.X, point.Y, 0, 0, Native.SWP_NOACTIVATE | Native.SWP_NOSIZE);
        }
        private void SaveSettings()
        {
            if (options.SmokeSeconds > 0 || options.ShowcaseSeconds > 0) return;
            settings.X = Left; settings.Y = Top; settings.PositionSaved = true; settings.Save();
        }

        private void OnPetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { clickTimer.Stop(); menu.Show(Cursor.Position); return; }
            if (e.Button != MouseButtons.Left) return;
            CancelScene();
            mouseDownEvents++;
            clickTimer.Stop();
            mousePressed = true; dragging = false; pressedBowl = layout.RiceHit.Contains(e.Location);
            mouseOrigin = Cursor.Position; windowOrigin = Location; Capture = true;
        }
        private void OnPetMouseMove(object sender, MouseEventArgs e)
        {
            if (!mousePressed || pressedBowl) return;
            Point cursor = Cursor.Position;
            if (!dragging && PetLogic.IsDrag(mouseOrigin, cursor))
            {
                dragging = true; clickTimer.Stop();
                if (action != PetAction.Feed) SetAction(PetAction.Surprise);
                Bubble("被拎起来啦～\n铁盆也要一起带上哦！", 2.5);
            }
            if (dragging)
            {
                Point point = new Point(windowOrigin.X + cursor.X - mouseOrigin.X, windowOrigin.Y + cursor.Y - mouseOrigin.Y);
                MoveWithoutActivation(PetLogic.Clamp(point, Size, Screen.FromPoint(cursor).WorkingArea));
            }
        }
        private void OnPetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !mousePressed) return;
            bool wasDragging = dragging; mousePressed = false; dragging = false; Capture = false;
            if (wasDragging) { completedDrags++; suppressClickUp = false; nextWalk = Uptime + 10; if (action != PetAction.Feed) { SetAction(PetAction.Happy); Bubble("稳稳落地啦～\n铁盆也好好带着呢！", 2.6); } SaveSettings(); return; }
            if (suppressClickUp) { suppressClickUp = false; return; }
            if (pressedBowl) { if (layout.RiceHit.Contains(e.Location)) { riceButtonEvents++; Feed(); } return; }
            clickTimer.Stop(); clickTimer.Start();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WM_MOUSEACTIVATE) { message.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            if (message.Msg == Native.WM_NCHITTEST && !mousePressed)
            {
                Point point = PointToClient(Cursor.Position);
                bool hit = layout != null && layout.RiceHit.Contains(point);
                if (!hit && layout != null && layout.Body.Contains(point) && hitAlpha != null && point.X >= 0 && point.Y >= 0 && point.X < Width && point.Y < Height)
                    hit = hitAlpha[point.Y * Width + point.X] > 24;
                message.Result = (IntPtr)(hit ? Native.HTCLIENT : Native.HTTRANSPARENT); return;
            }
            base.WndProc(ref message);
        }

        private void Render()
        {
            if (closing || !IsHandleCreated) return;
            int frame = PetLogic.Frame(action, (DateTime.UtcNow - actionStarted).TotalSeconds);
            using (Bitmap bitmap = PetArt.Render(atlas, layout, frame, action == PetAction.Walk && direction < 0, bubble, action == PetAction.Sleep, feedCount))
            {
                hitAlpha = Native.Update(Handle, bitmap, Location);
            }
            lastFrame = frame; dirty = false; renders++; renderedFrames.Add(frame); renderedActions.Add(action);
        }

        private Icon MakeTrayIcon()
        {
            try
            {
                Icon associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (associated != null) return associated;
            }
            catch (ArgumentException) { }
            using (Bitmap icon = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(icon))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawImage(atlas.Sheet, new Rectangle(1, 1, 30, 30), atlas.SourceRect(0), GraphicsUnit.Pixel);
                IntPtr handle = icon.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closing)
            {
                closing = true; timer.Stop(); clickTimer.Stop(); SaveSettings();
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                tray.Visible = false;
                if ((options.SmokeSeconds > 0 || options.ShowcaseSeconds > 0) && options.ReportPath != null)
                {
                    StringBuilder report = new StringBuilder();
                    report.AppendLine("LIVE WINDOWS SMOKE TEST");
                    report.AppendLine("ElapsedSeconds=" + Uptime.ToString("F2", CultureInfo.InvariantCulture));
                    report.AppendLine("NativeLayeredWindow=" + IsHandleCreated);
                    report.AppendLine("LayeredRenderCount=" + renders);
                    report.AppendLine("AlphaHitTestBuffer=" + (hitAlpha != null && hitAlpha.Length == Width * Height));
                    report.AppendLine("TrayCreated=" + (tray.Icon != null));
                    report.AppendLine("FeedTriggered=" + smokeFed);
                    report.AppendLine("FeedCount=" + feedCount);
                    List<int> frameList = new List<int>(renderedFrames); frameList.Sort();
                    report.AppendLine("RenderedFrames=" + String.Join(",", frameList.ConvertAll<string>(delegate(int i) { return i.ToString(); }).ToArray()));
                    List<int> actionList = new List<int>(); foreach (PetAction rendered in renderedActions) actionList.Add((int)rendered); actionList.Sort();
                    report.AppendLine("RenderedActions=" + String.Join(",", actionList.ConvertAll<string>(delegate(int i) { return i.ToString() + ":" + PetLogic.ActionName((PetAction)i); }).ToArray()));
                    report.AppendLine("RenderedActionCount=" + renderedActions.Count);
                    report.AppendLine("RenderedFrameCount=" + renderedFrames.Count);
                    report.AppendLine("TestMode=" + (options.ShowcaseSeconds > 0 ? "showcase" : "feeding-smoke"));
                    report.AppendLine("ActualMouseDownEvents=" + mouseDownEvents);
                    report.AppendLine("ActualCompletedDrags=" + completedDrags);
                    report.AppendLine("ActualRiceButtonEvents=" + riceButtonEvents);
                    report.AppendLine("InputNote=Automated showcase selects actions internally; mouse/tray interaction requires separate live input verification.");
                    report.AppendLine("WindowBounds=" + Bounds);
                    report.AppendLine("WorkingArea=" + Screen.FromRectangle(Bounds).WorkingArea);
                    bool success = renders > 0 && smokeFed;
                    if (options.ShowcaseSeconds > 0) success = success && renderedActions.Count == SpriteAtlas.ActionCount && renderedFrames.Count == SpriteAtlas.FrameCount;
                    DiagnosticSuccess = success;
                    report.AppendLine("Status=" + (success ? "PASS" : "FAIL"));
                    Diagnostics.Write(options.ReportPath, report.ToString());
                }
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                if (timer != null) timer.Dispose(); if (clickTimer != null) clickTimer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (menu != null) menu.Dispose(); if (trayIcon != null) trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Diagnostics
    {
        internal static void Write(string path, string text)
        {
            string absolute = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            File.WriteAllText(absolute, text, new UTF8Encoding(true));
        }
        internal static bool Run(SpriteAtlas atlas, string path)
        {
            List<string> lines = new List<string>(); bool passed = true;
            lines.Add("HEADLESS RESOURCE AND LOGIC SELF TEST");
            lines.Add("Source=" + atlas.Source);
            lines.Add("Sheet=" + atlas.Sheet.Width + "x" + atlas.Sheet.Height);
            lines.Add("CommonCrop=" + atlas.CommonCrop);
            Check(lines, ref passed, "68 nonempty alpha frames", atlas.Frames.Length == SpriteAtlas.FrameCount && Array.TrueForAll(atlas.OpaquePixels, delegate(int n) { return n > 30; }));
            bool sourceTransparency = true;
            for (int frame = 0; frame < SpriteAtlas.FrameCount; frame++)
                if (atlas.OpaquePixels[frame] >= atlas.Frames[frame].Width * atlas.Frames[frame].Height * 0.97) sourceTransparency = false;
            Check(lines, ref passed, "every sprite frame has a transparent background", sourceTransparency);
            HashSet<int> feed = new HashSet<int>();
            for (double time = 0; time < PetLogic.FeedSeconds; time += 0.05) feed.Add(PetLogic.Frame(PetAction.Feed, time));
            Check(lines, ref passed, "feeding covers frames 8,9,10,11", feed.Count == 4 && feed.Contains(8) && feed.Contains(9) && feed.Contains(10) && feed.Contains(11));
            Check(lines, ref passed, "empty bowl ending held", PetLogic.Frame(PetAction.Feed, 5.5) == 11);
            Check(lines, ref passed, "sleep row 16..19", PetLogic.Frame(PetAction.Sleep, 2.2) == 19);
            bool actionRows = Enum.GetValues(typeof(PetAction)).Length == SpriteAtlas.ActionCount;
            for (int i = 0; i < SpriteAtlas.ActionCount; i++)
            {
                HashSet<int> sequence = new HashSet<int>();
                for (double time = 0; time < PetLogic.ShowcaseDuration((PetAction)i); time += 0.05)
                {
                    int frame = PetLogic.Frame((PetAction)i, time);
                    if (frame < i * 4 || frame >= i * 4 + 4) actionRows = false;
                    sequence.Add(frame);
                }
                if (sequence.Count != 4) actionRows = false;
            }
            Check(lines, ref passed, "17 action rows each reach all four frames", actionRows);
            Check(lines, ref passed, "drag threshold rejects tiny movements", !PetLogic.IsDrag(new Point(10, 10), new Point(13, 13)) && PetLogic.IsDrag(new Point(10, 10), new Point(16, 10)));
            Rectangle[] screens = { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -120, 1920, 1080), new Rectangle(3840, 0, 1280, 720) };
            bool bounds = true;
            foreach (Rectangle screen in screens)
                for (int size = 0; size < 3; size++)
                {
                    Size pet = PetLayout.Create(atlas, size).Size;
                    Point point = PetLogic.Clamp(new Point(screen.Left - 300, screen.Bottom + 200), pet, screen);
                    Rectangle rectangle = new Rectangle(point, pet);
                    if (!screen.Contains(rectangle)) bounds = false;
                    point = PetLogic.Clamp(new Point(screen.Right + 1000, screen.Top - 400), pet, screen);
                    if (!screen.Contains(new Rectangle(point, pet))) bounds = false;
                }
            Check(lines, ref passed, "drag/motion clamp including negative monitor", bounds);
            bool alpha = false, transparent = false, premultiplied = true;
            using (Bitmap render = PetArt.Render(atlas, PetLayout.Create(atlas, 1), 9, false, "白饭测试", false, 1))
            {
                BitmapData data = render.LockBits(new Rectangle(0, 0, render.Width, render.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                byte[] pixels = new byte[Math.Abs(data.Stride) * render.Height];
                try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                finally { render.UnlockBits(data); }
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte a = pixels[i + 3]; if (a == 0) transparent = true; if (a > 0) alpha = true;
                    if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a) premultiplied = false;
                }
            }
            Check(lines, ref passed, "transparent canvas with visible pixels", alpha && transparent);
            Check(lines, ref passed, "premultiplied alpha for native layer", premultiplied);
            lines.Add("Status=" + (passed ? "PASS" : "FAIL"));
            if (path == null) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt");
            Write(path, String.Join("\r\n", lines.ToArray()) + "\r\n"); return passed;
        }
        private static void Check(List<string> lines, ref bool passed, string name, bool result)
        {
            lines.Add((result ? "PASS " : "FAIL ") + name); passed = passed && result;
        }
    }

    internal static class Native
    {
        internal const int WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        internal const int WM_NCHITTEST = 0x84, WM_MOUSEACTIVATE = 0x21, HTCLIENT = 1, HTTRANSPARENT = -1, MA_NOACTIVATE = 3;
        internal const int SWP_NOSIZE = 1, SWP_NOACTIVATE = 0x10;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { internal int x, y; internal POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { internal int cx, cy; internal SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER
        {
            internal uint biSize; internal int biWidth, biHeight; internal ushort biPlanes, biBitCount;
            internal uint biCompression, biSizeImage; internal int biXPelsPerMeter, biYPelsPerMeter; internal uint biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { internal BITMAPINFOHEADER bmiHeader; internal uint bmiColors; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDC, ref POINT destination, ref SIZE size, IntPtr memoryDC, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        internal static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
        }

        internal static byte[] Update(IntPtr window, Bitmap bitmap, Point location)
        {
            BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
            try
            {
                for (int row = 0; row < bitmap.Height; row++)
                    Marshal.Copy(IntPtr.Add(locked.Scan0, row * locked.Stride), pixels, row * bitmap.Width * 4, bitmap.Width * 4);
            }
            finally { bitmap.UnlockBits(locked); }
            byte[] alpha = new byte[bitmap.Width * bitmap.Height];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
            IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, dib = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                BITMAPINFO info = new BITMAPINFO(); info.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                info.bmiHeader.biWidth = bitmap.Width; info.bmiHeader.biHeight = -bitmap.Height;
                info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32;
                IntPtr bits; dib = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero || bits == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Marshal.Copy(pixels, 0, bits, pixels.Length); previous = SelectObject(memory, dib);
                POINT destination = new POINT(location.X, location.Y), source = new POINT(0, 0);
                SIZE size = new SIZE(bitmap.Width, bitmap.Height);
                BLENDFUNCTION blend = new BLENDFUNCTION(); blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                if (!UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memory, previous);
                if (dib != IntPtr.Zero) DeleteObject(dib); if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
            return alpha;
        }
    }
}
