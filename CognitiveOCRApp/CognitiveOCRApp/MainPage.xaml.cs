using Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Vision.Contract;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Sensors;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CognitiveOCRApp
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private MediaCapture _mediaCapture;
        bool _isInitialized, _externalCamera, _isPreviewing;
        private readonly DisplayInformation _displayInformation = DisplayInformation.GetForCurrentView();
        private readonly SimpleOrientationSensor _orientationSensor = SimpleOrientationSensor.GetDefault();
        private SimpleOrientation _deviceOrientation = SimpleOrientation.NotRotated;
        private DisplayOrientations _displayOrientation = DisplayOrientations.Portrait;

        private static readonly Guid RotationKey = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");
        private readonly DisplayRequest _displayRequest = new DisplayRequest();

        private readonly SystemMediaTransportControls _systemMediaControls = SystemMediaTransportControls.GetForCurrentView();

        private AdvancedPhotoCapture _advancedCapture;
        private int _advancedCaptureMode = -1;
        private StorageFolder _captureFolder = null;
        private static string visionKey = "a91fb64687944d5c92e8b07afa901dd5";

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await initializeCamera();
        }

        private async Task initializeCamera()
        {
            var picturesLibrary = await StorageLibrary.GetLibraryAsync(KnownLibraryId.Pictures);
            _captureFolder = picturesLibrary.SaveFolder ?? ApplicationData.Current.LocalFolder;


            _displayOrientation = _displayInformation.CurrentOrientation;
            if (_orientationSensor != null)
            {
                _deviceOrientation = _orientationSensor.GetCurrentOrientation();
            }

            if (_mediaCapture == null)
            {
                var cameraDevice = await FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel.Back);

                _mediaCapture = new MediaCapture();

                var settings = new MediaCaptureInitializationSettings { VideoDeviceId = cameraDevice.Id };

                try
                {
                    //await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    //{
                    await _mediaCapture.InitializeAsync(settings);
                    //});

                    _isInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine("The app was denied access to the camera");
                }

                if (_isInitialized)
                {
                    if (cameraDevice.EnclosureLocation == null || cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        // No information on the location of the camera, assume it's an external camera, not integrated on the device
                        _externalCamera = true;
                    }
                    else
                    {
                        _externalCamera = false;
                    }
                }
            }

            _displayRequest.RequestActive();
            PreviewControl.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();
            _isPreviewing = true;

            await EnableAdvancedCaptureAsync();

            if (_isPreviewing)
            {
                if (_externalCamera) return;

                int rotationDegrees = ConvertDisplayOrientationToDegrees(_displayOrientation);

                // Add rotation metadata to the preview stream to make sure the aspect ratio / dimensions match when rendering and getting preview frames
                var props = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
                props.Properties[RotationKey] = rotationDegrees;
                await _mediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, null);
            }
        }
        private async void PhotoButton_Click(object sender, RoutedEventArgs e)
        {
            await TakeAdvancedCapturePhotoAsync();
        }

        private async Task TakeAdvancedCapturePhotoAsync()
        {
            try
            {
                Debug.WriteLine("Taking Advanced Capture photo...");

                // Read the current orientation of the camera and the capture time
                var photoOrientation = ConvertOrientationToPhotoOrientation(GetCameraOrientation());
                var fileName = String.Format("AdvancedCapturePhoto_{0}.jpg", DateTime.Now.ToString("HHmmss"));

                // Create a context object, to identify the capture later on
                var context = new AdvancedCaptureContext { CaptureFileName = fileName, CaptureOrientation = photoOrientation };

                // Start capture, and pass the context object to get it back in the OptionalReferencePhotoCaptured event
                var capture = await _advancedCapture.CaptureAsync(context);

                using (var frame = capture.Frame)
                {
                    var file = await _captureFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    Debug.WriteLine("Advanced Capture photo taken! Saving to " + file.Path);

                    await ReencodeAndSavePhotoAsync(frame, file, photoOrientation);
                }
            }
            catch (Exception ex)
            {
                // File I/O errors are reported as exceptions
                Debug.WriteLine("Exception when taking an Advanced Capture photo: " + ex.ToString());
            }
        }

        private async Task ReencodeAndSavePhotoAsync(IRandomAccessStream stream, StorageFile file, PhotoOrientation photoOrientation)
        {
            using (var inputStream = stream)
            {
                var decoder = await BitmapDecoder.CreateAsync(inputStream);

                using (var outputStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(outputStream, decoder);

                    var properties = new BitmapPropertySet { { "System.Photo.Orientation", new BitmapTypedValue(photoOrientation, PropertyType.UInt16) } };

                    await encoder.BitmapProperties.SetPropertiesAsync(properties);
                    await encoder.FlushAsync();
                }
            }

            VisionInfo visionResult = await AnalyzeImage(file.Path, "en");
            OcrResults textResult = null;
            if (visionResult.Category.Contains("car") || visionResult.Tags.Contains("car"))
            {
                textResult = await ReadTextFromImage(file.Path, "en");
                StringBuilder stringBuilder = new StringBuilder();
                if (textResult != null && textResult.Regions != null)
                {
                    stringBuilder.Append("Text: ");
                    foreach (var item in textResult.Regions)
                    {
                        foreach (var line in item.Lines)
                        {
                            foreach (var word in line.Words)
                            {
                                stringBuilder.Append(word.Text);
                                stringBuilder.Append(" ");
                            }
                        }
                    }
                }

                statusText.Text = stringBuilder.ToString();
                Debug.WriteLine(stringBuilder);
            }
        }

        private async Task<OcrResults> ReadTextFromImage(string imageFilePath, string textLanguage)
        {
            OcrResults ocrResults = null;
            try { 
            VisionServiceClient vClient = new VisionServiceClient(visionKey);
            Stream imageStream = null;
            await Task.Run(() => { imageStream = File.OpenRead(imageFilePath); });
            ocrResults = await vClient.RecognizeTextAsync(imageStream, textLanguage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return ocrResults;
        }

        private async Task<VisionInfo> AnalyzeImage(string imageFilePath, string textLanguage)
        {
            AnalysisResult visionResult = null, tagResult = null, descResult = null;
            VisionServiceClient vClient = new VisionServiceClient(visionKey);
            Stream imageStream = null;
            IEnumerable<VisualFeature> vFeatures = null;

            try
            {
                await Task.Run(() => { imageStream = File.OpenRead(imageFilePath); });

                tagResult = await vClient.GetTagsAsync(imageStream);
                imageStream.Position = 0;
                visionResult = await vClient.AnalyzeImageAsync(imageStream, vFeatures);
                imageStream.Position = 0;
                descResult = await vClient.DescribeAsync(imageStream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            StringBuilder tagString = new StringBuilder();
            foreach (Tag tag in tagResult.Tags)
            {
                tagString.Append(tag.Name + " ");
            }

            StringBuilder catString = new StringBuilder();
            foreach (Category cat in visionResult.Categories)
            {
                catString.Append(cat.Name + " ");
            }

            foreach (string tag in descResult.Description.Tags)
            {
                tagString.Append(tag + " ");
            }

            VisionInfo vInfo = new VisionInfo();
            vInfo.Category = catString.ToString();
            vInfo.Tags = tagString.ToString();

            statusText.Text = vInfo.Tags;

            return vInfo;
        }

        private int ConvertDisplayOrientationToDegrees(DisplayOrientations orientation)
        {
            switch (orientation)
            {
                case DisplayOrientations.Portrait:
                    return 90;
                case DisplayOrientations.LandscapeFlipped:
                    return 180;
                case DisplayOrientations.PortraitFlipped:
                    return 270;
                case DisplayOrientations.Landscape:
                default:
                    return 0;
            }
        }

        private async Task<DeviceInformation> FindCameraDeviceByPanelAsync(Windows.Devices.Enumeration.Panel desiredPanel)
        {
            // Get available devices for capturing pictures
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            // Get the desired camera by panel
            DeviceInformation desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == desiredPanel);

            // If there is no device mounted on the desired panel, return the first device found
            return desiredDevice ?? allVideoDevices.FirstOrDefault();
        }

        private PhotoOrientation ConvertOrientationToPhotoOrientation(SimpleOrientation orientation)
        {
            switch (orientation)
            {
                case SimpleOrientation.Rotated90DegreesCounterclockwise:
                    return PhotoOrientation.Rotate90;
                case SimpleOrientation.Rotated180DegreesCounterclockwise:
                    return PhotoOrientation.Rotate180;
                case SimpleOrientation.Rotated270DegreesCounterclockwise:
                    return PhotoOrientation.Rotate270;
                case SimpleOrientation.NotRotated:
                default:
                    return PhotoOrientation.Normal;
            }
        }

        private SimpleOrientation GetCameraOrientation()
        {
            if (_externalCamera)
            {
                // Cameras that are not attached to the device do not rotate along with it, so apply no rotation
                return SimpleOrientation.NotRotated;
            }

            var result = _deviceOrientation;

            // Account for the fact that, on portrait-first devices, the camera sensor is mounted at a 90 degree offset to the native orientation
            if (_displayInformation.NativeOrientation == DisplayOrientations.Portrait)
            {
                switch (result)
                {
                    case SimpleOrientation.Rotated90DegreesCounterclockwise:
                        result = SimpleOrientation.NotRotated;
                        break;
                    case SimpleOrientation.Rotated180DegreesCounterclockwise:
                        result = SimpleOrientation.Rotated90DegreesCounterclockwise;
                        break;
                    case SimpleOrientation.Rotated270DegreesCounterclockwise:
                        result = SimpleOrientation.Rotated180DegreesCounterclockwise;
                        break;
                    case SimpleOrientation.NotRotated:
                        result = SimpleOrientation.Rotated270DegreesCounterclockwise;
                        break;
                }
            }

            return result;
        }

        private async Task EnableAdvancedCaptureAsync()
        {
            // No work to be done if there already is an AdvancedCapture instance
            if (_advancedCapture != null) return;

            // Configure one of the modes in the control
            CycleAdvancedCaptureMode();

            // Prepare for an Advanced Capture
            _advancedCapture = await _mediaCapture.PrepareAdvancedPhotoCaptureAsync(ImageEncodingProperties.CreateJpeg());

            Debug.WriteLine("Enabled Advanced Capture");

            // Register for events published by the AdvancedCapture
            //_advancedCapture.AllPhotosCaptured += AdvancedCapture_AllPhotosCaptured;
            //_advancedCapture.OptionalReferencePhotoCaptured += AdvancedCapture_OptionalReferencePhotoCaptured;
        }
        private void CycleAdvancedCaptureMode()
        {
            // Calculate the index for the next supported mode
            _advancedCaptureMode = (_advancedCaptureMode + 1) % _mediaCapture.VideoDeviceController.AdvancedPhotoControl.SupportedModes.Count;

            // Configure the settings object to the mode at the calculated index
            var settings = new AdvancedPhotoCaptureSettings
            {
                Mode = _mediaCapture.VideoDeviceController.AdvancedPhotoControl.SupportedModes[_advancedCaptureMode]
            };

            // Configure the mode on the control
            _mediaCapture.VideoDeviceController.AdvancedPhotoControl.Configure(settings);

            // Update the button text to reflect the current mode
            //ModeTextBlock.Text = _mediaCapture.VideoDeviceController.AdvancedPhotoControl.Mode.ToString();
        }
    }
    public class AdvancedCaptureContext
    {
        public string CaptureFileName;
        public PhotoOrientation CaptureOrientation;
    }

    public class VisionInfo
    {
        public string Tags { get; set; }
        public string Category { get; set; }
    }
}
