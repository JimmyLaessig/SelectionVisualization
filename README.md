
Volume Selection is a technique that utilizes Shadow Volumes for visual selection on any data sets. The selection happens purely on the GPU without manipulating the underlying data. The selection works independently of the dataset, making it perfect for applications like Point-Cloud Rendering, where memory transfer is a bottleneck.  
The technique is implemented in F# using the Aardvark framework. 
To include the technique in an Aardvark scenegraph, the Init-method must be called. All parameters, that possibly change the selection are provided as IMods. Therefore the selection auto-updates itself, when a parameter (camera view) changes. 

##How to build

Windows:
- Visual Studio 2015,
- Visual FSharp Tools installed (we use 4.0 now) 
- run build.cmd which will install all dependencies
- msbuild src\Aardvark.sln or use VisualStudio to build the solution

Linux:
- install mono >= 4.2.3.0 (might work in older versions as well)
- install fsharp 4.0 (http://fsharp.org/use/linux/)
- run build.sh which will install all dependencies
- run xbuild src/Aardvark.sln

## Projects
### Visual Selection
### MyGpcWrapper
### Visual Selection Example


Windows:
- Visual Studio 2015,
- Visual FSharp Tools installed (we use 4.0 now) 
- run build.cmd which will install all dependencies
- msbuild src\Aardvark.sln or use VisualStudio to build the solution

The progam.fs provides an example implementation on a simple scene. 
[Aardvark](https://github.com/vrvis/aardvark)
[General Polygon Clipper](http://www.cs.man.ac.uk/~toby/gpc/)
