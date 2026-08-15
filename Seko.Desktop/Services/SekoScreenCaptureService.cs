using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Seko.Desktop.Services;

public static class SekoScreenCaptureService
{
    private const int SmCxScreen =
        0;

    private const int SmCyScreen =
        1;

    private const int Srccopy =
        0x00CC0020;

    private const int CaptureBlt =
        0x40000000;

    public static string CapturePrimaryScreen()
    {
        var width =
            GetSystemMetrics(
                SmCxScreen);

        var height =
            GetSystemMetrics(
                SmCyScreen);

        if (width <= 0
            || height <= 0)
        {
            throw new InvalidOperationException(
                "Windows did not report a valid primary-screen size.");
        }

        var screenDc =
            GetDC(
                IntPtr.Zero);

        if (screenDc
            == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Windows could not open the screen capture device context.");
        }

        IntPtr memoryDc =
            IntPtr.Zero;

        IntPtr bitmap =
            IntPtr.Zero;

        IntPtr previousObject =
            IntPtr.Zero;

        try
        {
            memoryDc =
                CreateCompatibleDC(
                    screenDc);

            if (memoryDc
                == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            bitmap =
                CreateCompatibleBitmap(
                    screenDc,
                    width,
                    height);

            if (bitmap
                == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            previousObject =
                SelectObject(
                    memoryDc,
                    bitmap);

            if (previousObject
                == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            var copied =
                BitBlt(
                    memoryDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    0,
                    0,
                    Srccopy
                    | CaptureBlt);

            if (!copied)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            var bitmapSource =
                Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();

            var directory =
                GetCaptureDirectory();

            Directory.CreateDirectory(
                directory);

            CleanupOldCaptures(
                directory);

            var filePath =
                Path.Combine(
                    directory,
                    $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

            var encoder =
                new PngBitmapEncoder();

            encoder.Frames.Add(
                BitmapFrame.Create(
                    bitmapSource));

            using var stream =
                File.Create(
                    filePath);

            encoder.Save(
                stream);

            return
                filePath;
        }
        finally
        {
            if (previousObject
                != IntPtr.Zero
                && memoryDc
                    != IntPtr.Zero)
            {
                _ =
                    SelectObject(
                        memoryDc,
                        previousObject);
            }

            if (bitmap
                != IntPtr.Zero)
            {
                _ =
                    DeleteObject(
                        bitmap);
            }

            if (memoryDc
                != IntPtr.Zero)
            {
                _ =
                    DeleteDC(
                        memoryDc);
            }

            _ =
                ReleaseDC(
                    IntPtr.Zero,
                    screenDc);
        }
    }

    public static string SaveClipboardImage()
    {
        if (!Clipboard.ContainsImage())
        {
            throw new InvalidOperationException(
                "The Windows clipboard does not contain an image.");
        }

        var bitmapSource =
            Clipboard.GetImage();

        if (bitmapSource is null)
        {
            throw new InvalidOperationException(
                "Windows reported an image on the clipboard, but Seko could not read it.");
        }

        bitmapSource.Freeze();

        var directory =
            GetCaptureDirectory();

        Directory.CreateDirectory(
            directory);

        CleanupOldCaptures(
            directory);

        var filePath =
            Path.Combine(
                directory,
                $"screenshot-paste-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

        var encoder =
            new PngBitmapEncoder();

        encoder.Frames.Add(
            BitmapFrame.Create(
                bitmapSource));

        using var stream =
            File.Create(
                filePath);

        encoder.Save(
            stream);

        return
            filePath;
    }

    public static void TryDeleteOwnedCapture(
        string filePath)
    {
        try
        {
            var fullPath =
                Path.GetFullPath(
                    filePath);

            var directory =
                Path.GetFullPath(
                        GetCaptureDirectory())
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            var prefix =
                directory
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(
                    fullPath))
            {
                File.Delete(
                    fullPath);
            }
        }
        catch
        {
            // Temporary screenshot cleanup is best-effort only.
        }
    }

    private static string GetCaptureDirectory()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return
            Path.Combine(
                localAppData,
                "Seko",
                "Temp",
                "Screenshots");
    }

    private static void CleanupOldCaptures(
        string directory)
    {
        try
        {
            var threshold =
                DateTime.UtcNow
                - TimeSpan.FromHours(
                    24);

            foreach (var path
                     in Directory.EnumerateFiles(
                         directory,
                         "screenshot-*.png",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(
                            path)
                        < threshold)
                    {
                        File.Delete(
                            path);
                    }
                }
                catch
                {
                    // Skip an individual locked or unreadable temp file.
                }
            }
        }
        catch
        {
            // Cleanup must never block an explicit screenshot capture.
        }
    }

    [DllImport(
        "user32.dll")]
    private static extern IntPtr GetDC(
        IntPtr windowHandle);

    [DllImport(
        "user32.dll")]
    private static extern int ReleaseDC(
        IntPtr windowHandle,
        IntPtr deviceContext);

    [DllImport(
        "user32.dll")]
    private static extern int GetSystemMetrics(
        int index);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(
        IntPtr deviceContext);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteDC(
        IntPtr deviceContext);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr deviceContext,
        int width,
        int height);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern IntPtr SelectObject(
        IntPtr deviceContext,
        IntPtr graphicsObject);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    private static extern bool DeleteObject(
        IntPtr graphicsObject);

    [DllImport(
        "gdi32.dll",
        SetLastError = true)]
    [return:
        MarshalAs(
            UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destinationDc,
        int destinationX,
        int destinationY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        int rasterOperation);
}