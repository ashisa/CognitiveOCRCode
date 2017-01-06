#What is it?

This code is an abstracted version of the code to use Microsoft Cognitive APIs. The purpose of the project is to read the vehicle registration number when a vehicle is detected in the camera frame.

#How to use this sample?

Download/clone this project and open in Visual Studio. Build the project to have VS install the required Nuget packages and then run the project. Point your device's camera to a car with its registration number plate in view and tap on the camera button to take a photo.

You will also have to add your Vision Cognitive API key added to the project to do the work.

Once you tap on the camera button, the app does the following -

1. Convert the saved image to a Stream and use the GetTagsAsync() function to extract the tags for the given image
2. AnalyzeImageAsync() is called on the same Stream object to find out the information about the categorization of objects detected in the image
3. Finally, we call the DescribeAsync() function to get a descrption and the tags as well

This information is now checked to see if we have a car in the scene and if it is detected, we proceed to read the vehicle registration number information using the RecognizeTextAsync() function in the SDK. All the parsed text is then run through a LUIS model utilizing a Regex pattern to identify the registration number.
