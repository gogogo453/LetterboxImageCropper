using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LetterboxImageCropper;

/// <summary>Shows an image scaled to fit, with the areas that will be cut shaded (or only the cropped frame).</summary>
public sealed class PreviewPanel : Control
{
    private PreviewImage? _image;
    private Bars _crop;
    private bool _showCropped;
    private string _message = "Select a folder to begin.";

    public PreviewPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.Gainsboro;
    }

    /// <summary>Takes ownership of <paramref name="image"/>.</summary>
    public void SetImage(PreviewImage? image, string? message = null)
    {
        _image?.Dispose();
        _image = image;
        _message = message ?? "";
        Invalidate();
    }

    public void SetCrop(Bars crop)
    {
        _crop = crop;
        Invalidate();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCropped
    {
        get => _showCropped;
        set { _showCropped = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        if (_image is null)
        {
            TextRenderer.DrawText(g, _message, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        Bitmap bmp = _image.Bitmap;
        // Crop is expressed in original-image pixels; the bitmap may be a downscaled copy.
        float sx = bmp.Width / (float)_image.SourceWidth;
        float sy = bmp.Height / (float)_image.SourceHeight;

        bool cropValid = _crop.IsValidFor(_image.SourceWidth, _image.SourceHeight);
        RectangleF frame = cropValid
            ? new RectangleF(_crop.Left * sx, _crop.Top * sy,
                _crop.FrameWidth(_image.SourceWidth) * sx, _crop.FrameHeight(_image.SourceHeight) * sy)
            : new RectangleF(0, 0, bmp.Width, bmp.Height);

        RectangleF source = _showCropped ? frame : new RectangleF(0, 0, bmp.Width, bmp.Height);
        RectangleF dest = Fit(source.Size, ClientRectangle, padding: 8);

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(bmp, dest, source, GraphicsUnit.Pixel);

        if (_showCropped) return;

        float scale = dest.Width / source.Width;
        RectangleF frameOnScreen = new(dest.X + frame.X * scale, dest.Y + frame.Y * scale, frame.Width * scale, frame.Height * scale);

        using var shade = new SolidBrush(Color.FromArgb(140, 220, 30, 30));
        g.FillRectangle(shade, dest.Left, dest.Top, dest.Width, frameOnScreen.Top - dest.Top);
        g.FillRectangle(shade, dest.Left, frameOnScreen.Bottom, dest.Width, dest.Bottom - frameOnScreen.Bottom);
        g.FillRectangle(shade, dest.Left, frameOnScreen.Top, frameOnScreen.Left - dest.Left, frameOnScreen.Height);
        g.FillRectangle(shade, frameOnScreen.Right, frameOnScreen.Top, dest.Right - frameOnScreen.Right, frameOnScreen.Height);

        using var pen = new Pen(Color.Yellow, 1) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, frameOnScreen.X, frameOnScreen.Y, frameOnScreen.Width - 1, frameOnScreen.Height - 1);
    }

    private static RectangleF Fit(SizeF content, Rectangle bounds, int padding)
    {
        float availW = Math.Max(1, bounds.Width - 2 * padding);
        float availH = Math.Max(1, bounds.Height - 2 * padding);
        float scale = Math.Min(availW / content.Width, availH / content.Height);
        float w = content.Width * scale, h = content.Height * scale;
        return new RectangleF(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
    }
}
