# Volume-Selection
Volume Selection is a technique that utilizes Shadow Volumes for visual selection on any data sets. The selection happens purely on the GPU without manipulating the underlying data.The selection works independently of the dataset, making it perfect for applications like Point-Cloud Rendering. 
The Technique is implemented in F# using the Aardvark framework. 
To include the technique in an Aardvark scenegraph, the Init-method must be called. All parameters, that possibly change the selection are provided as IMods. Therefore the selection auto-updates itself, when a parameter (camera view) changes. 
