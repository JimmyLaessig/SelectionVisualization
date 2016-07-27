
Volume Selection is a technique that utilizes Shadow Volumes for visual selection on any data sets. The selection happens purely on the GPU without manipulating the underlying data. The selection works independently of the dataset, making it perfect for applications like Point-Cloud Rendering, where memory transfer is a bottleneck.  
The technique is implemented in F# using the [Aardvark](https://github.com/vrvis/aardvark) framework. The project uses the General Polygon Clipper library, which can be downloaded [here](http://www.cs.man.ac.uk/~toby/gpc/). #
Although GPC is free for academic use, we cannot provide the project including
a compiled version of GPC library. Therefore, in order to use this project, you need to download and build the library yourself: 
- Each method in the gpc.h file must be extended with __declspec(dllimport) attribute to be visible for other applications in the dll,   e.g.: 
  
  ___declspec(dllimport)	void gpc_polygon_to_tristrip (	gpc_polygon     *polygon, gpc_tristrip    *tristrip);
- Navigate to the folder of the GPC source code and execute the following command in order to compile the GPC library: 
  
  "PATH_TO_VISUAL_STUDIO\VC\bin\amd64\vcvars64.bat" && cl /LD gpc.c

The resulting DLL (gpc.dll) should be put besides your current executable of the project (typically bin/Release or bin/Debug). 
The application consists of three projects, two of which are compiled to a DLL and one example implementation compiled to an executable. 
###MyGpcWrapper
This project is a C# Wrapper library for the C++ implementation the General Polygon Clipper library. GPC Polygons are used in order to triangulate the screen-space selection polygon. 
###Visual Selection
This project is the core library of the application. In stores the logic for the visual selection and all resources.
###Visual Selection Example
The example project shows the usage of the visual selection library in a simple test scene. 

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

##Usage
To include the technique in an Aardvark scenegraph, the Init-method must be called. All parameters, that possibly change the selection are provided as IMods. Therefore the selection auto-updates itself, when a parameter (camera view) changes. 

