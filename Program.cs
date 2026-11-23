using System;
using System.Windows.Forms;

namespace LetterboxImageCropper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            Codecs.Initialize();
        }
        catch (Exception ex)
        {
            // Typically libvips-42.dll is missing next to the exe or belongs to another architecture.
            MessageBox.Show(
                $"The image library (libvips) could not be loaded.\n\nMake sure libvips-42.dll is in the same folder as the program.\n\n{ex.InnerException?.Message ?? ex.Message}",
                AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
    }
}
