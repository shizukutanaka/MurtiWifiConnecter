using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using QRCoder;
using MWC.Core.Models;
using MWC.Core.Profile;

namespace MWC.App.Views;

public partial class QrCodeDialog : Window
{
    private readonly string _uri;
    private byte[] _png = Array.Empty<byte>();

    public QrCodeDialog(WifiProfileSpec spec)
    {
        InitializeComponent();
        SsidLabel.Text = spec.Ssid;
        _uri = WifiUri.Build(spec);
        UriLabel.Text = _uri;

        _png = GenerateQrPng(_uri);
        if (_png.Length > 0)
        {
            using var ms = new MemoryStream(_png);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            QrImage.Source = img;
        }
    }

    /// <summary>QRCoder で WIFI: URI → PNG バイト列生成。</summary>
    private static byte[] GenerateQrPng(string uri)
    {
        try
        {
            using var gen  = new QRCodeGenerator();
            using var data = gen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
            var pngCode = new PngByteQRCode(data);
            return pngCode.GetGraphic(10,
                darkColorRgba:  new byte[] { 0, 0, 0, 255 },
                lightColorRgba: new byte[] { 255, 255, 255, 255 });
        }
        catch { return Array.Empty<byte>(); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_uri);
        Title = MWC.App.Resources.L.Get("QR_Copied");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_png.Length == 0) return;
        var dlg = new SaveFileDialog
        {
            FileName = $"{SsidLabel.Text}.png",
            Filter   = "PNG Image (*.png)|*.png",
            DefaultExt = ".png"
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllBytes(dlg.FileName, _png);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
