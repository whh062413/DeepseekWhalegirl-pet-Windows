using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Independent audit harness. Sends messages only to its own test pet HWND.
// Does not move the real pointer, activate another app, or take screenshots.
internal static class NativeInputAudit
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_MOUSEMOVE = 0x200;
    private static readonly StringBuilder Report = new StringBuilder();
    private static bool passed = true;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private static object Field(object instance, string name) { return instance.GetType().GetField(name, All).GetValue(instance); }
    private static void Field(object instance, string name, object value) { instance.GetType().GetField(name, All).SetValue(instance, value); }
    private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, All).Invoke(instance, args); }
    private static void Check(string label, bool result) { Report.AppendLine((result ? "PASS " : "FAIL ") + label); passed &= result; }
    private static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); }
    }
    private static void Send(Form form, int message, Point point)
    {
        int coordinates = (point.X & 0xffff) | ((point.Y & 0xffff) << 16);
        SendMessage(form.Handle, message, message == WM_LBUTTONUP ? IntPtr.Zero : new IntPtr(1), new IntPtr(coordinates));
    }
    private static void Click(Form form, Point point) { Send(form, WM_LBUTTONDOWN, point); Send(form, WM_LBUTTONUP, point); }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) return 3;
        Form form = null; IDisposable atlas = null;
        Report.AppendLine("INDEPENDENT NATIVE HWND MESSAGE AUDIT");
        Report.AppendLine("Scope=Real WinForms/native message dispatch to an isolated no-activate test window; not manual pointer or visual acceptance.");
        Report.AppendLine("PointerControl=No SetCursorPos/SendInput or global pointer movement used. Drag origin is offset by reflection to exercise the actual move handler.");
        try
        {
            Assembly runtime = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            runtime.GetType("WhalePet.Native").GetMethod("EnableDpiAwareness", All).Invoke(null, null);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            atlas = (IDisposable)runtime.GetType("WhalePet.SpriteAtlas").GetMethod("Load", All).Invoke(null, null);
            Type optionsType = runtime.GetType("WhalePet.Options");
            object options = Activator.CreateInstance(optionsType, true);
            Field(options, "SmokeSeconds", 3600d); // Isolates settings; Feed auto trigger is disabled below.
            form = (Form)Activator.CreateInstance(runtime.GetType("WhalePet.PetWindow"), All, null, new object[] { atlas, options }, null);
            Field(form, "smokeFed", true);
            object settings = Field(form, "settings");
            Field(settings, "AutoWalk", false); Field(settings, "AutoCute", false);
            ((NotifyIcon)Field(form, "tray")).Visible = false;
            IntPtr foreground = GetForegroundWindow();
            form.Show();
            ((System.Windows.Forms.Timer)Field(form, "timer")).Stop();
            ((NotifyIcon)Field(form, "tray")).Visible = false;
            object layout = Field(form, "layout");
            Rectangle body = (Rectangle)Field(layout, "Body");
            Rectangle rice = (Rectangle)layout.GetType().GetProperty("RiceHit", All).GetValue(layout, null);
            Point bodyPoint = new Point(body.Left + body.Width / 2, body.Top + body.Height / 2);
            Point ricePoint = new Point(rice.Left + rice.Width / 2, rice.Top + rice.Height / 2);
            Type actionType = runtime.GetType("WhalePet.PetAction");
            Action<int> setAction = delegate(int index) { Call(form, "SetAction", Enum.ToObject(actionType, index)); };
            Func<int> action = delegate { return Convert.ToInt32(Field(form, "action")); };

            setAction(0); Click(form, bodyPoint); Pump(SystemInformation.DoubleClickTime + 80);
            Check("body single-click reaches an affectionate action through native messages", new int[] { 3, 6, 9, 11, 16 }.Contains(action()));
            Check("single-click does not feed", Convert.ToInt32(Field(form, "feedCount")) == 0);

            setAction(0); Click(form, bodyPoint); Send(form, WM_LBUTTONDBLCLK, bodyPoint); Send(form, WM_LBUTTONUP, bodyPoint);
            Pump(SystemInformation.DoubleClickTime + 80);
            Check("body native double-click feeds once and suppresses delayed single-click", action() == 2 && Convert.ToInt32(Field(form, "feedCount")) == 1);
            Check("double-click leaves no pressed state", !(bool)Field(form, "mousePressed"));

            setAction(0); Click(form, ricePoint);
            Check("rice button native click feeds once", action() == 2 && Convert.ToInt32(Field(form, "feedCount")) == 2 && Convert.ToInt32(Field(form, "riceButtonEvents")) == 1);

            setAction(0); Send(form, WM_LBUTTONDOWN, bodyPoint);
            Point current = Cursor.Position; Field(form, "mouseOrigin", new Point(current.X - 20, current.Y));
            Send(form, WM_MOUSEMOVE, bodyPoint);
            Check("native drag move reaches Surprise", (bool)Field(form, "dragging") && action() == 7);
            Send(form, WM_LBUTTONUP, bodyPoint);
            Check("native drag release reaches Happy and clears capture state", action() == 3 && !(bool)Field(form, "mousePressed") && !(bool)Field(form, "dragging") && Convert.ToInt32(Field(form, "completedDrags")) == 1);

            Call(form, "Feed"); int feedBeforeDrag = Convert.ToInt32(Field(form, "feedCount"));
            Send(form, WM_LBUTTONDOWN, bodyPoint); current = Cursor.Position; Field(form, "mouseOrigin", new Point(current.X - 20, current.Y));
            Send(form, WM_MOUSEMOVE, bodyPoint); Send(form, WM_LBUTTONUP, bodyPoint);
            Check("dragging feeding pet preserves feeding and count", action() == 2 && Convert.ToInt32(Field(form, "feedCount")) == feedBeforeDrag);

            setAction(0); Send(form, WM_LBUTTONDOWN, bodyPoint); ReleaseCapture();
            Check("native capture loss clears pressed/drag state", !(bool)Field(form, "mousePressed") && !(bool)Field(form, "dragging"));

            Field(settings, "AutoWalk", true); Field(settings, "AutoCute", true);
            int[] protectedActions = new int[] { 2, 4, 9 };
            foreach (int protectedAction in protectedActions)
            {
                setAction(protectedAction); Field(form, "nextCute", -1d); Field(form, "nextWalk", -1d);
                Call(form, "Tick", null, EventArgs.Empty);
                Check("automatic actions preserve current action " + protectedAction, action() == protectedAction);
            }
            Field(settings, "AutoWalk", false); Field(settings, "AutoCute", false);
            setAction(2); Field(form, "actionStarted", DateTime.UtcNow.AddSeconds(-6)); Call(form, "Tick", null, EventArgs.Empty);
            Check("finished feeding transitions to Victory", action() == 13);

            ContextMenuStrip menu = (ContextMenuStrip)Field(form, "menu"); ToolStripMenuItem gallery = null;
            foreach (ToolStripItem item in menu.Items) if (item.Text.StartsWith("动作相册")) gallery = item as ToolStripMenuItem;
            bool galleryPass = gallery != null && gallery.DropDownItems.Count == 17;
            for (int index = 0; galleryPass && index < 17; index++)
            {
                setAction(0); gallery.DropDownItems[index].PerformClick();
                if (action() != index) galleryPass = false;
            }
            Check("all 17 gallery items dispatch to their own action", galleryPass);
            Check("native mouse event counters prove handlers ran", Convert.ToInt32(Field(form, "mouseDownEvents")) >= 7 && Convert.ToInt32(Field(form, "completedDrags")) == 2);
            ToolStripMenuItem scenes = null;
            foreach (ToolStripItem item in menu.Items) if (item.Text.StartsWith("小剧场")) scenes = item as ToolStripMenuItem;
            int[][] expectedScenes = {
                new int[] { 6, 9, 3, 16 }, new int[] { 12, 2, 13, 16 }, new int[] { 13, 12, 8, 9 },
                new int[] { 14, 7, 9, 16 }, new int[] { 4, 15, 2, 13 }, new int[] { 5, 10, 11, 4, 15 }
            };
            bool scenesPass = scenes != null && scenes.DropDownItems.Count == expectedScenes.Length;
            Field(settings, "AutoWalk", true); Field(settings, "AutoCute", true);
            for (int scene = 0; scenesPass && scene < expectedScenes.Length; scene++)
            {
                Call(form, "CancelScene"); setAction(0); int countBeforeScene = Convert.ToInt32(Field(form, "feedCount"));
                scenes.DropDownItems[scene].PerformClick();
                for (int step = 0; step < expectedScenes[scene].Length; step++)
                {
                    if (action() != expectedScenes[scene][step] || Field(form, "sceneSteps") == null) scenesPass = false;
                    Field(form, "nextCute", -1d); Field(form, "nextWalk", -1d);
                    Call(form, "Tick", null, EventArgs.Empty);
                    if (action() != expectedScenes[scene][step]) scenesPass = false;
                    Field(form, "actionStarted", DateTime.UtcNow.AddSeconds(-10)); Call(form, "Tick", null, EventArgs.Empty);
                }
                if (Field(form, "sceneSteps") != null || action() != 0) scenesPass = false;
                int expectedFeeds = Array.IndexOf(expectedScenes[scene], 2) >= 0 ? 1 : 0;
                if (Convert.ToInt32(Field(form, "feedCount")) - countBeforeScene != expectedFeeds) scenesPass = false;
            }
            Check("all six scene queues advance, preserve priority, finish, and count feeding once", scenesPass);
            Call(form, "CancelScene"); setAction(0); scenes.DropDownItems[0].PerformClick();
            Send(form, WM_LBUTTONDOWN, bodyPoint); ReleaseCapture();
            Check("manual native mouse press cancels queued scene steps", Field(form, "sceneSteps") == null);
            for (int sizeIndex = 0; sizeIndex < 3; sizeIndex++)
            {
                Call(form, "ChangeSize", sizeIndex); layout = Field(form, "layout");
                Rectangle basin = (Rectangle)Field(layout, "Rice");
                Rectangle basinHit = (Rectangle)layout.GetType().GetProperty("RiceHit", All).GetValue(layout, null);
                Rectangle basinLabel = new Rectangle(basin.Left, basin.Bottom + 1, basin.Width, 14);
                int countBeforeLabel = Convert.ToInt32(Field(form, "feedCount"));
                setAction(0); Click(form, new Point(basinLabel.Left + basinLabel.Width / 2, basinLabel.Top + basinLabel.Height / 2));
                Check("basin label click and hit bounds at size " + sizeIndex,
                    new Rectangle(Point.Empty, form.Size).Contains(basinHit) && basinHit.Contains(basinLabel) && action() == 2 && Convert.ToInt32(Field(form, "feedCount")) == countBeforeLabel + 1);
            }
            Report.AppendLine("ForegroundUnchangedDuringAudit=" + (GetForegroundWindow() == foreground));
        }
        catch (Exception error) { passed = false; Report.AppendLine(error.ToString()); }
        finally
        {
            if (form != null) { form.Close(); form.Dispose(); }
            if (atlas != null) atlas.Dispose();
            Report.AppendLine("Status=" + (passed ? "PASS" : "FAIL"));
            string reportPath = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, Report.ToString(), new UTF8Encoding(true));
        }
        return passed ? 0 : 2;
    }
    private static bool Contains(this int[] values, int value) { return Array.IndexOf(values, value) >= 0; }
}
