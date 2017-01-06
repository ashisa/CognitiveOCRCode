#What is this sample?

It is a UWP App sample code to demonstrate the use of Microsoft Cognitive Services to analyze and detect vehicle in the camera preview frames and read the vehicle registration number if detected.

#How to use this sample?

Download/clone this project and open in Visual Studio. Build the project to have VS install the required Nuget packages and then run the project. Point your device's camera to a car with its registration number plate in view and tap on the camera button to take a photo.

You will also have to add your Vision Cognitive API key added to the project to do the work.

Once you tap on the camera button, the app uses the Vision API SDK to upload the image and get it analyzed for presence of a vehicle in the frame. If a vehicle is detected, it makes another call to process any text detected in the images.


