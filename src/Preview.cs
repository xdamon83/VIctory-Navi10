using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

internal sealed class Preview : Form
{
    private readonly Renderer renderer;
    private readonly Bitmap image;
    private readonly byte[] pixels;
    private readonly Timer timer = new Timer { Interval = 16 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly HashSet<Keys> keys = new HashSet<Keys>();
    private readonly Font titleFont = new Font("Segoe UI", 12, FontStyle.Bold);
    private readonly Font statsFont = new Font("Segoe UI", 10);
    private View camera = View.Default();
    private RenderPath path = RenderPath.Expanded;
    private bool ready;
    private FrameStats latest;
    private double previous, motion;
    private string error;

    internal Preview(Renderer renderer)
    {
        this.renderer = renderer;
        pixels = new byte[renderer.Width * renderer.Height * 4];
        image = new Bitmap(renderer.Width, renderer.Height, PixelFormat.Format32bppArgb);
        Text = "VIctory-Navi10";
        ClientSize = new Size(1040, 684);
        MinimumSize = new Size(680, 450);
        BackColor = Color.FromArgb(18, 27, 40);
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (sender, e) =>
        {
            if (!keys.Add(e.KeyCode)) return;
            if (e.KeyCode == Keys.Tab) NextPath();
            if (e.KeyCode == Keys.Home) camera = View.Default();
            if (e.KeyCode == Keys.Escape) Close();
        };
        KeyUp += (sender, e) => keys.Remove(e.KeyCode);
        Deactivate += (sender, e) => keys.Clear();
        timer.Tick += (sender, e) => Step();
        Shown += (sender, e) => timer.Start();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Tab) { NextPath(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    private void NextPath()
    {
        path = (RenderPath)(((int)path + 1) % 3);
    }

    private void Step()
    {
        double now = clock.Elapsed.TotalSeconds;
        double delta = Math.Min(0.1, now - previous);
        previous = now;
        motion += delta;
        double pan = delta * camera.HalfHeight;
        double right = Direction(Keys.D, Keys.A) * pan;
        double forward = Direction(Keys.W, Keys.S) * pan;
        camera.X += Math.Cos(camera.Yaw) * right - Math.Sin(camera.Yaw) * forward;
        camera.Z += Math.Sin(camera.Yaw) * right + Math.Cos(camera.Yaw) * forward;
        camera.Y += Direction(Keys.E, Keys.Q) * pan;
        camera.Yaw += Direction(Keys.Right, Keys.Left) * delta * 1.5;
        camera.Pitch = Math.Max(0.08, Math.Min(1.48, camera.Pitch + Direction(Keys.Up, Keys.Down) * delta));
        camera.HalfHeight = Math.Max(4, Math.Min(500, camera.HalfHeight * Math.Exp(-Direction(Keys.Add, Keys.Subtract) * delta)));
        try
        {
            latest = renderer.Render(camera, path, (float)motion, true);
            renderer.ReadPixels(pixels);
            Program.CopyToBitmap(pixels, image);
            ready = true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            timer.Stop();
        }
        Invalidate();
    }

    private int Direction(Keys positive, Keys negative)
    {
        return (keys.Contains(positive) ? 1 : 0) - (keys.Contains(negative) ? 1 : 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.DrawString("VIctory-Navi10", titleFont, Brushes.White, 16, 10);
        graphics.DrawString(renderer.Hardware.Adapter, statsFont, Brushes.LightSteelBlue, 160, 12);
        if (ready)
        {
            float scale = Math.Min((ClientSize.Width - 32f) / image.Width, (ClientSize.Height - 110f) / image.Height);
            int w = (int)(image.Width * scale), h = (int)(image.Height * scale);
            graphics.DrawImage(image, (ClientSize.Width - w) / 2, 42, w, h);
            string timing = String.Format("{0}  |  {1:F2} ms / image   CPU {2:F2} ms   GPU {3:F2} ms   Triangles {4:N0}",
                latest.Path, latest.FrameMs, latest.CpuSubmitMs, latest.GpuCullMs + latest.GpuRasterMs, latest.SubmittedTriangles);
            graphics.DrawString(timing, statsFont, Brushes.White, 16, ClientSize.Height - 56);
        }
        if (error != null) graphics.DrawString(error, statsFont, Brushes.Salmon, 16, ClientSize.Height - 56);
        graphics.DrawString("WASD déplacement · Q/E altitude · Flèches orientation · +/− zoom · Tab pipeline · Home réinitialiser",
            statsFont, Brushes.LightSteelBlue, 16, ClientSize.Height - 31);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            image.Dispose();
            titleFont.Dispose();
            statsFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
