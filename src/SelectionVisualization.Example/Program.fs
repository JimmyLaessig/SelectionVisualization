open System

open System.Linq
open Aardvark.Base
open Aardvark.Base.Incremental
open Aardvark.Rendering.NanoVg
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.WinForms
open Aardvark.Base.Rendering

open CameraController

open Lasso
open SelectionVisualization
open Shaders
open Aardvark.Git
 



[<EntryPoint>]
let main argv = 
    Ag.initialize()
    Aardvark.Init()


    use app = new OpenGlApplication()
    let win = app.CreateSimpleRenderWindow(1)


    let planeEffect  = app.Runtime.PrepareEffect(win.FramebufferSignature, [DefaultSurfaces.trafo |> toEffect; DefaultSurfaces.constantColor (C4f(0.3, 0.3, 0.3, 1.0)) |> toEffect; DefaultSurfaces.simpleLighting |> toEffect]) :> ISurface      
    let lineEffect   = app.Runtime.PrepareEffect(win.FramebufferSignature, [DefaultSurfaces.thickLine |> toEffect; DefaultSurfaces.constantColor C4f.Red |> toEffect]) :> ISurface   
     
    win.Text <- "Aardvark rocks \\o/"
    
       
    // Init Camera Controller
    let cameraController = 
        controller {                    
            return! AFun.chain [
                CameraControllers.controlLook win.Mouse
                CameraControllers.controlWSAD win.Keyboard 2.0
                CameraControllers.controlPan win.Mouse 0.05
                // CameraControllers.controlZoom win.Mouse 0.05
                CameraControllers.controlScroll win.Mouse 0.1 0.004
                ]
        }

        
    // Init Camera
    let initialView = CameraView.lookAt (V3d(6,6,6)) V3d.Zero V3d.OOI

    let view = AFun.integrate cameraController initialView   
    let proj = win.Sizes |> Mod.map (fun s -> Frustum.perspective 60.0 0.1 100.0 (float s.X / float s.Y))
    let x = view |> Mod.map (CameraView.viewTrafo)

    // Lasso stuff:
    // Keyboard modifiers for this RenderControl
    let keyModifiers = State.initModifiers win 
    let wc = MouseKeyboard.Lasso.WorkingCopy
    
    //lasso state using modifiers and a WorkingCopy
    let lasso = State.initLasso view proj keyModifiers wc
   
    //add keybindings and mousebuttonbindings
    do MouseKeyboard.initMouse      (Mod.constant true) lasso win
    do MouseKeyboard.initKeyboard   lasso win

    // Init Basic Dummy Geometry
    let planeGeometry =
        IndexedGeometry (
            Mode = IndexedGeometryMode.TriangleList,
            IndexArray = [| 0; 1; 2; 0; 2; 3 |],
            IndexedAttributes =
                SymDict.ofList [
                    DefaultSemantic.Positions,                  [| V3f.OOO; V3f.IOO; V3f.IIO; V3f.OIO |] :> Array
                    DefaultSemantic.DiffuseColorCoordinates,    [| V2f.OO; V2f.IO; V2f.II; V2f.OI |] :> Array
                    DefaultSemantic.Normals,                    [| V3f.OOI; V3f.OOI; V3f.OOI; V3f.OOI |] :> Array
                    
                ]        
        )    
    // Init 2nd Basic Dummy Geometry
    let planeGeometry2 =
        IndexedGeometry (
            Mode = IndexedGeometryMode.TriangleList,
            IndexArray = [| 0; 1; 2; 0; 2; 3 |],
            IndexedAttributes =
                SymDict.ofList [
                    DefaultSemantic.Positions,                  [| V3f.OOI; V3f.IOI; V3f.III; V3f.OII |] :> Array
                    DefaultSemantic.DiffuseColorCoordinates,    [| V2f.OO; V2f.IO; V2f.II; V2f.OI |] :> Array
                    DefaultSemantic.Normals,                    [| V3f.OOI; V3f.OOI; V3f.OOI; V3f.OOI |] :> Array
                ]        
        )              

    // Basis Scenegraph
    let sceneGraph =
        planeGeometry 
            |> Sg.ofIndexedGeometry
            |> Sg.viewTrafo      (view |> Mod.map CameraView.viewTrafo)
            |> Sg.projTrafo      (proj |> Mod.map Frustum.projTrafo)
            |> Sg.surface        (planeEffect |> Mod.constant)
            |> Sg.pass           Rendering.RenderPass.main 
            |> Sg.cullMode       (CullMode.Clockwise |> Mod.constant)

   
    let sceneGraph = 
            planeGeometry2
            |> Sg.ofIndexedGeometry
            |> Sg.viewTrafo      (view |> Mod.map CameraView.viewTrafo)
            |> Sg.projTrafo      (proj |> Mod.map Frustum.projTrafo)
            |> Sg.surface        (planeEffect |> Mod.constant)
            |> Sg.pass           Rendering.RenderPass.main 
            |> Sg.cullMode       (CullMode.Clockwise |> Mod.constant)
            |> Sg.andAlso sceneGraph


    // Highlight Color
    let selectionColor = Mod.constant C4f.Red
    // Volume Color
    let volumeColor = Mod.constant (C4f(0.0f, 1.0f, 0.0f, 0.1f))
    // Selection Distance
    let selectionDistance = Mod.constant 5.0
    // View Transform
    let viewTrafo = view |> Mod.map CameraView.viewTrafo
    // Projection Transform
    let projTrafo = proj |> Mod.map Frustum.projTrafo
    // Show Volumes or not
    let showVolumes = Mod.constant false
    // Runtime
    let runtime = app.Runtime
    // Framebuffer
    let framebufferSignature = win.FramebufferSignature
    
    // Create VolumeSelection
    let (sg, renderPass) = SelectionVisualization.Init 
                                            sceneGraph 
                                            viewTrafo   
                                            projTrafo   
                                            lasso      
                                            selectionColor
                                            selectionDistance 
                                            volumeColor        
                                            showVolumes         
                                            Rendering.RenderPass.main 
                                            runtime                 
                                            framebufferSignature
    
    // Create RenderPass for after VolumeSelection 
    let renderPassLasso = Rendering.RenderPass.after "lassoPass" Rendering.RenderPassOrder.Arbitrary renderPass    
        
    // Scenegraph with temporary Lasso
    let sceneGraph_WithLasso = 
        LassoSg.withSg (Sg.dynamic sg) win lasso
            |> Sg.surface   (lineEffect |> Mod.constant)
            |> Sg.pass      renderPassLasso        
    

   // let config = BackendConfiguration.ManagedUnoptimized    
    let config = BackendConfiguration.NativeOptimized 
    let task =
        app.Runtime.CompileRender(win.FramebufferSignature,config, sceneGraph_WithLasso)
            |> DefaultOverlays.withStatistics


    let clear = app.Runtime.CompileClear(win.FramebufferSignature, Mod.constant C4f.White, Mod.constant 1.0)

    win.RenderTask <- RenderTask.ofList [clear; task]
    win.Run()
    0
