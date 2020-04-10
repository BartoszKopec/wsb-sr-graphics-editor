using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;

namespace GraphicsEditor.ViewModels
{
    class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private ImageSource _source;
        private Bitmap _bitmap;
        private CancellationTokenSource _tokenSource;
        private long _operationElapsedTimeParallel;
        private long _operationElapsedTime;
        private int _rValue, _gValue, _bValue;
        private bool _isUserInterfaceEnabled = true;

        public void CancelOperation()
        {
            _tokenSource?.Cancel();
            IsUserInterfaceEnabled = true;
        }

        public void OpenDialogForImageSelecting()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg)|*.png;*.jpeg;*.jpg|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                _bitmap = new Bitmap(File.OpenRead(openFileDialog.FileName));
                SetImage();
            }
        }

        public void ChangeColors()
        {
            if (_bitmap is null)
                return;
            _tokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                Debug.WriteLine($"R {_rValue}, G {_gValue}, B {_bitmap}");
                TransformBitmapThreads(color =>
                {
                    static byte getNewColor(byte oldColor, int value)
                    {
                        int x = (int)(oldColor);

                        if (value < 50)
                            x -= 50-value;
                        if (value > 50)
                            x += value-50;
                        
                        if (x > 255)
                            x = 255;
                        if (x < 0)
                            x = 0;
                        return (byte)x;
                    }

                    int r = getNewColor(color.R, _rValue);
                    int g = getNewColor(color.G, _gValue);
                    int b = getNewColor(color.B, _bValue);

                    return Color.FromArgb(r, g, b);
                }, true);
            });
        }

        public void TransformToNegative()
        {
            if (_bitmap is null)
                return;
            _tokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                TransformBitmapThreads(color =>
                {
                    Color negative = Color.FromArgb(255 - color.R, 255 - color.G, 255 - color.B);
                    return negative;
                }, true);
            });
        }

        public void TransformToGrayscale()
        {
            if (_bitmap is null)
                return;
            _tokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                TransformBitmapThreads(color =>
                {
                    double grayscaleValue = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
                    return Color.FromArgb((int)grayscaleValue, (int)grayscaleValue, (int)grayscaleValue);
                }, true);
            });
        }

        private void SetImage()
        {
            var image = new BitmapImage();
            using (MemoryStream memory = new MemoryStream())
            {
                _bitmap.Save(memory, ImageFormat.Bmp);
                memory.Position = 0;
                image.BeginInit();
                image.StreamSource = memory;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
            }
            image.Freeze();
            Source = image;
            RValue = 50;
            GValue = 50;
            BValue = 50;
        }

        private void TransformBitmap(Func<Color, Color> transforming)
        {
            Stopwatch timer = Stopwatch.StartNew();
            Bitmap bitmap = (Bitmap)_bitmap.Clone();
            int heightInPixels = bitmap.Height;
            int widthInPixels = bitmap.Width;
            for (int y = 0; y < heightInPixels; y++)
            {
                if (_tokenSource.IsCancellationRequested)
                    break;
                for (int x = 0; x < widthInPixels; x++)
                {
                    if (_tokenSource.IsCancellationRequested)
                        break;
                    Color oldColor = bitmap.GetPixel(x, y);
                    Color newColor = transforming(oldColor);
                    bitmap.SetPixel(x, y, newColor);
                }
            }
            timer.Stop();
            bitmap.Dispose();
            OperationElapsedTime = timer.ElapsedMilliseconds;
        }

        private void TransformBitmapThreads(Func<Color, Color> transforming, bool runSync)
        {
            IsUserInterfaceEnabled = false;
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                BitmapData bitmapData = _bitmap.LockBits(
                    new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
                    ImageLockMode.ReadWrite,
                    _bitmap.PixelFormat);
                int bitsPerPixel = Bitmap.GetPixelFormatSize(_bitmap.PixelFormat) / 8;
                int byteCount = bitmapData.Stride * _bitmap.Height;
                byte[] pixels = new byte[byteCount];
                IntPtr ptrFirstPixel = bitmapData.Scan0;
                Marshal.Copy(ptrFirstPixel, pixels, 0, pixels.Length);

                bool isWidthSmaller = bitmapData.Width < bitmapData.Height;
                int usingSizeInPixels = isWidthSmaller ? bitmapData.Width : bitmapData.Height;
                int usingSizeInBytes = isWidthSmaller ? (bitmapData.Height * bitsPerPixel) : (bitmapData.Width * bitsPerPixel);

                ParallelOptions options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 200,
                    CancellationToken = _tokenSource.Token,
                    TaskScheduler = TaskScheduler.Default
                };
                try
                {
                    Parallel.For(0, usingSizeInPixels, options, y =>
                    {
                        int currentLine = y * bitmapData.Stride;
                        for (int x = 0; x < usingSizeInBytes; x += bitsPerPixel)
                        {
                            int oldBlue = pixels[currentLine + x];
                            int oldGreen = pixels[currentLine + x + 1];
                            int oldRed = pixels[currentLine + x + 2];
                            Color newColor = transforming(Color.FromArgb(oldRed, oldGreen, oldBlue));
                            pixels[currentLine + x] = newColor.B;
                            pixels[currentLine + x + 1] = newColor.G;
                            pixels[currentLine + x + 2] = newColor.R;
                        }
                    });
                    Marshal.Copy(pixels, 0, ptrFirstPixel, pixels.Length);
                    _bitmap.UnlockBits(bitmapData);
                    timer.Stop();
                    SetImage();
                    OperationElapsedTimeParallel = timer.ElapsedMilliseconds;
                    if(runSync)
                        TransformBitmap(transforming);
                }
                catch (OperationCanceledException)
                {
                    Marshal.Copy(pixels, 0, ptrFirstPixel, pixels.Length);
                    _bitmap.UnlockBits(bitmapData);
                    timer.Stop();
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            IsUserInterfaceEnabled = true;
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName]string propertyName = "")
        {
            field = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged([CallerMemberName]string propertyName = "")=>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string GetFormattedColorValue(int value)
        {
            if (value < 50)
                return $"-{50 - value}";
            if (value > 50)
                return $"+{value - 50}";
            return "0";
        }

        public ImageSource Source
        {
            get => _source;
            set 
            { 
                SetProperty(ref _source, value);
                Debug.WriteLine("Set image source");
            }
        }

        public long OperationElapsedTimeParallel
        {
            get => _operationElapsedTimeParallel;
            set => SetProperty(ref _operationElapsedTimeParallel, value);
        }

        public long OperationElapsedTime
        {
            get => _operationElapsedTime;
            set => SetProperty(ref _operationElapsedTime, value);
        }

        public int RValue
        {
            get => _rValue;
            set { SetProperty(ref _rValue, value); OnPropertyChanged(nameof(RValueText)); }
        }

        public int GValue
        {
            get => _gValue;
            set { SetProperty(ref _gValue, value); OnPropertyChanged(nameof(GValueText)); }
        }

        public int BValue
        {
            get => _bValue;
            set { SetProperty(ref _bValue, value); OnPropertyChanged(nameof(BValueText)); }
        }

        public string RValueText => GetFormattedColorValue(_rValue);
        public string GValueText => GetFormattedColorValue(_gValue);
        public string BValueText => GetFormattedColorValue(_bValue);

        public bool IsUserInterfaceEnabled
        {
            get => _isUserInterfaceEnabled;
            set => SetProperty(ref _isUserInterfaceEnabled, value);
        }
    }
}
